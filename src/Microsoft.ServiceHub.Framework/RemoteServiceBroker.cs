// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable RS0026 // Do not add multiple public overloads with optional parameters

using System.Diagnostics;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Exposes a remote <see cref="IRemoteServiceBroker"/> service as a local <see cref="IServiceBroker"/>.
/// </summary>
[RequiresUnreferencedCode(Reasons.TypeLoad)]
public class RemoteServiceBroker : IServiceBroker, IDisposable, System.IAsyncDisposable
{
	private const string NETFrameworkDescription = ".NET Framework";
	private const string NETCoreDescription = ".NET Core";
	private const string NET5PlusDescription = ".NET";
	private static readonly Regex VersionFinder = new Regex(@"\d+\.\d+(\.\d+(\.\d+)?)?");
	private static readonly ServiceBrokerClientMetadata ClientMetadata = new ServiceBrokerClientMetadata
	{
		LocalServiceHost = GetLocalServiceHostInformation(),
		SupportedConnections = RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing, // Do not include RemoteServiceConnections.ClrActivation by default.
	};

	private readonly IRemoteServiceBroker remoteServiceBroker;
	private readonly MultiplexingStream? multiplexingStream;
	private readonly TaskCompletionSource<object?> completionSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>
	/// A value indicating whether to dispose of the <see cref="multiplexingStream"/> upon disposal.
	/// </summary>
	private bool multiplexingStreamOwned;

	/// <summary>
	/// The backing field for the <see cref="TraceSource"/> property.
	/// </summary>
	private TraceSource traceSource = new TraceSource(nameof(RemoteServiceBroker), SourceLevels.Warning);

	/// <summary>
	/// The data sent in the last handshake.
	/// </summary>
	private ServiceBrokerClientMetadata clientMetadata;

	/// <summary>
	/// The authorization service that can acquire fresh client credentials.
	/// </summary>
	private AuthorizationServiceClient? authorizationServiceClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="RemoteServiceBroker"/> class.
	/// </summary>
	/// <param name="remoteServiceBroker">The proxy to the remote service broker.</param>
	/// <param name="clientMetadata">The client metadata transmitted in the handshake.</param>
	private RemoteServiceBroker(IRemoteServiceBroker remoteServiceBroker, ServiceBrokerClientMetadata clientMetadata)
	{
		this.remoteServiceBroker = remoteServiceBroker ?? throw new ArgumentNullException(nameof(remoteServiceBroker));
		this.remoteServiceBroker.AvailabilityChanged += this.OnAvailabilityChanged;
		this.clientMetadata = clientMetadata;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="RemoteServiceBroker"/> class
	/// that may utilize a multiplexing stream to proffer services.
	/// </summary>
	/// <param name="remoteServiceBroker">The proxy to the remote service broker.</param>
	/// <param name="multiplexingStream">The multiplexing stream on which the requested services may be exposed. Must not be null.</param>
	/// <param name="clientMetadata">The client metadata transmitted in the handshake.</param>
	private RemoteServiceBroker(IRemoteServiceBroker remoteServiceBroker, MultiplexingStream multiplexingStream, ServiceBrokerClientMetadata clientMetadata)
		: this(remoteServiceBroker, clientMetadata)
	{
		this.multiplexingStream = multiplexingStream ?? throw new ArgumentNullException(nameof(multiplexingStream));
	}

	/// <inheritdoc />
	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	private enum TraceEvents
	{
		/// <summary>
		/// Indicates a failure in requesting a service (not just a negative result from the remote service broker).
		/// </summary>
		ServiceRequestFailure = 1,

		/// <summary>
		/// Indicates a response for a service indicates the service was not available.
		/// </summary>
		RequestedServiceUnavailable = 2,

		/// <summary>
		/// Indicates the remote service broker proposed we connect to a service using a means we did not support.
		/// </summary>
		IncompatibleServiceConnection = 3,

		/// <summary>
		/// A service was offered, but a failure occurred while trying to connect to it.
		/// </summary>
		ServiceConnectionFailure = 4,

		/// <summary>
		/// This instance was explicitly disposed.
		/// </summary>
		Disposed = 5,
	}

	/// <summary>
	/// Gets a <see cref="Task"/> that completes when this instance is disposed or the underlying <see cref="Stream"/> it was created with (if applicable) is closed.
	/// </summary>
	public Task Completion => this.completionSource.Task;

	/// <summary>
	/// Gets or sets the <see cref="System.Diagnostics.TraceSource"/> this instance will use for trace messages.
	/// </summary>
	/// <value>Never null.</value>
	public TraceSource TraceSource
	{
		get => this.traceSource;
		set
		{
			Requires.NotNull(value, nameof(value));
			this.traceSource = value;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="RemoteServiceBroker"/> class
	/// that connects to an <see cref="IRemoteServiceBroker"/> on the default channel
	/// after establishing a <see cref="MultiplexingStream"/> on the given <see cref="Stream"/>.
	/// </summary>
	/// <param name="duplexStream">
	/// A full duplex stream on which to create a multiplexing stream.
	/// This multiplexing stream is expected to offer a default channel (<see cref="string.Empty"/> name) with a
	/// <see cref="IRemoteServiceBroker"/> service.
	/// This object is considered "owned" by the returned <see cref="RemoteServiceBroker"/> and will be disposed when the returned value is disposed,
	/// or disposed before this method throws.
	/// </param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>An <see cref="IServiceBroker"/> that provides access to remote services.</returns>
	public static Task<RemoteServiceBroker> ConnectToMultiplexingServerAsync(Stream duplexStream, CancellationToken cancellationToken = default) => ConnectToMultiplexingServerAsync(duplexStream, options: null, cancellationToken: cancellationToken);

	/// <inheritdoc cref="ConnectToMultiplexingServerAsync(Stream, MultiplexingStream.Options?, TraceSource?, CancellationToken)"/>
	public static Task<RemoteServiceBroker> ConnectToMultiplexingServerAsync(Stream duplexStream, MultiplexingStream.Options? options, CancellationToken cancellationToken = default) => ConnectToMultiplexingServerAsync(duplexStream, options, traceSource: null, cancellationToken);

	/// <summary>
	/// Initializes a new instance of the <see cref="RemoteServiceBroker"/> class
	/// that connects to an <see cref="IRemoteServiceBroker"/> on the default channel
	/// after establishing a <see cref="MultiplexingStream"/> on the given <see cref="Stream"/>.
	/// </summary>
	/// <param name="duplexStream">
	/// A full duplex stream on which to create a multiplexing stream.
	/// This multiplexing stream is expected to offer a default channel (<see cref="string.Empty"/> name) with a
	/// <see cref="IRemoteServiceBroker"/> service.
	/// This object is considered "owned" by the returned <see cref="RemoteServiceBroker"/> and will be disposed when the returned value is disposed,
	/// or disposed before this method throws.
	/// </param>
	/// <param name="options">Options to pass along to the created <see cref="MultiplexingStream"/> on creation.</param>
	/// <param name="traceSource">An optional means of logging activity.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>An <see cref="IServiceBroker"/> that provides access to remote services.</returns>
	public static async Task<RemoteServiceBroker> ConnectToMultiplexingServerAsync(Stream duplexStream, MultiplexingStream.Options? options, TraceSource? traceSource, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(duplexStream, nameof(duplexStream));

		try
		{
			// We construct several disposable objects here, but these all ultimately hook the original stream to self-dispose
			// when the underlying stream is disposed, so we only need one try/catch level here.
			MultiplexingStream multiplexingStream = await MultiplexingStream.CreateAsync(duplexStream, options, cancellationToken).ConfigureAwait(false);
			MultiplexingStream.Channel serviceBrokerChannel = await multiplexingStream.AcceptChannelAsync(string.Empty, cancellationToken).ConfigureAwait(false);
			IRemoteServiceBroker serviceBroker = FrameworkServices.RemoteServiceBroker
				.WithTraceSource(traceSource)
				.ConstructRpc<IRemoteServiceBroker>(serviceBrokerChannel);
			RemoteServiceBroker result = await ConnectToMultiplexingServerAsync(serviceBroker, multiplexingStream, cancellationToken).ConfigureAwait(false);
			result.multiplexingStreamOwned = true;
			multiplexingStream.Completion.ApplyResultTo(result.completionSource);
			return result;
		}
		catch
		{
			await duplexStream.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="RemoteServiceBroker"/> class.
	/// </summary>
	/// <param name="serviceBroker">
	/// An existing proxy established to acquire remote services.
	/// This object is considered "owned" by the returned <see cref="RemoteServiceBroker"/> and will be disposed when the returned value is disposed,
	/// or disposed before this method throws.
	/// </param>
	/// <param name="multiplexingStream">A multiplexing stream that underlies the <paramref name="serviceBroker"/> proxy.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>An <see cref="IServiceBroker"/> that provides access to remote services.</returns>
	/// <remarks>
	/// The <see cref="FrameworkServices.RemoteServiceBroker"/> is used as the wire protocol.
	/// </remarks>
	public static async Task<RemoteServiceBroker> ConnectToMultiplexingServerAsync(IRemoteServiceBroker serviceBroker, MultiplexingStream multiplexingStream, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		Requires.NotNull(multiplexingStream, nameof(multiplexingStream));

		try
		{
			ServiceBrokerClientMetadata clientMetadata = ClientMetadata;
			await serviceBroker.HandshakeAsync(clientMetadata, cancellationToken).ConfigureAwait(false);
			var result = new RemoteServiceBroker(serviceBroker, multiplexingStream, clientMetadata);
			multiplexingStream.Completion.ApplyResultTo(result.completionSource);
			return result;
		}
		catch
		{
			(serviceBroker as IDisposable)?.Dispose();
			throw;
		}
	}

	/// <inheritdoc cref="ConnectToServerAsync(IDuplexPipe, TraceSource?, CancellationToken)"/>
	public static Task<RemoteServiceBroker> ConnectToServerAsync(IDuplexPipe pipe, CancellationToken cancellationToken = default) => ConnectToServerAsync(pipe, traceSource: null, cancellationToken);

	/// <summary>
	/// Initializes a new instance of the <see cref="RemoteServiceBroker"/> class.
	/// </summary>
	/// <param name="pipe">
	/// A duplex pipe over which to exchange JSON-RPC messages with an
	/// <see cref="IRemoteServiceBroker"/> service.
	/// This object is considered "owned" by the returned <see cref="RemoteServiceBroker"/> and will be completed when the returned value is disposed,
	/// or completed before this method throws.
	/// </param>
	/// <param name="traceSource">An optional means of logging activity.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>An <see cref="IServiceBroker"/> that provides access to remote services.</returns>
	/// <remarks>
	/// The <see cref="FrameworkServices.RemoteServiceBroker"/> is used as the wire protocol.
	/// </remarks>
	public static Task<RemoteServiceBroker> ConnectToServerAsync(IDuplexPipe pipe, TraceSource? traceSource, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(pipe, nameof(pipe));

		IRemoteServiceBroker serviceBroker = FrameworkServices.RemoteServiceBroker
			.WithTraceSource(traceSource)
			.ConstructRpc<IRemoteServiceBroker>(pipe);

		// If this ends up throwing (or rather, returning a faulted Task) it will have disposed the serviceBroker,
		// which *should* dispose the pipe we handed to it as well. So we don't need to wrap this with try/catch here.
		// We have a test to assert this behavior.
		return ConnectToServerAsync(serviceBroker, cancellationToken);
	}

	/// <inheritdoc cref="ConnectToServerAsync(string, TraceSource?, CancellationToken)"/>
	public static Task<RemoteServiceBroker> ConnectToServerAsync(string pipeName, CancellationToken cancellationToken = default) => ConnectToServerAsync(pipeName, traceSource: null, cancellationToken);

	/// <summary>
	/// Initializes a new instance of the <see cref="RemoteServiceBroker"/> class.
	/// </summary>
	/// <param name="pipeName">
	/// The name of a pipe over which to exchange JSON-RPC messages with an
	/// <see cref="IRemoteServiceBroker"/> service.
	/// </param>
	/// <param name="traceSource">An optional means of logging activity.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>An <see cref="IServiceBroker"/> that provides access to remote services.</returns>
	/// <remarks>
	/// The <see cref="FrameworkServices.RemoteServiceBroker"/> is used as the wire protocol.
	/// </remarks>
	public static async Task<RemoteServiceBroker> ConnectToServerAsync(string pipeName, TraceSource? traceSource, CancellationToken cancellationToken = default)
	{
		Requires.NotNullOrEmpty(pipeName, nameof(pipeName));

		IDuplexPipe pipe = await ConnectToPipeAsync(pipeName, cancellationToken).ConfigureAwait(false);
		IRemoteServiceBroker serviceBroker = FrameworkServices.RemoteServiceBroker
			.WithTraceSource(traceSource)
			.ConstructRpc<IRemoteServiceBroker>(pipe);

		// If this ends up throwing (or rather, returning a faulted Task) it will have disposed the serviceBroker,
		// which *should* dispose the pipe we handed to it as well. So we don't need to wrap this with try/catch here.
		// We have a test to assert this behavior.
		return await ConnectToServerAsync(serviceBroker, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="RemoteServiceBroker"/> class.
	/// </summary>
	/// <param name="serviceBroker">
	/// An existing proxy established to acquire remote services.
	/// This object is considered "owned" by the returned <see cref="RemoteServiceBroker"/> and will be disposed when the returned value is disposed,
	/// or disposed before this method throws.
	/// </param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>An <see cref="IServiceBroker"/> that provides access to remote services.</returns>
	/// <remarks>
	/// The <see cref="FrameworkServices.RemoteServiceBroker"/> is used as the wire protocol.
	/// </remarks>
	public static async Task<RemoteServiceBroker> ConnectToServerAsync(IRemoteServiceBroker serviceBroker, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));

		try
		{
			ServiceBrokerClientMetadata clientMetadata = ClientMetadata;
			clientMetadata.SupportedConnections &= ~RemoteServiceConnections.Multiplexing;
			await serviceBroker.HandshakeAsync(clientMetadata, cancellationToken).ConfigureAwait(false);
			return new RemoteServiceBroker(serviceBroker, clientMetadata);
		}
		catch
		{
			(serviceBroker as IDisposable)?.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Offers the local environment as a host for services proffered by the remote service broker when they can be activated locally.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A task that completes after the service broker has acknowledged the local service host.</returns>
	public async Task OfferLocalServiceHostAsync(CancellationToken cancellationToken = default)
	{
		ServiceBrokerClientMetadata clientMetadata = this.clientMetadata;
		if (!clientMetadata.SupportedConnections.HasFlag(RemoteServiceConnections.ClrActivation))
		{
			clientMetadata.SupportedConnections |= RemoteServiceConnections.ClrActivation;

			if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
			{
				this.TraceSource.TraceInformation("Offering self as a local service host with supported connections: {0}", clientMetadata.SupportedConnections);
			}

			await this.remoteServiceBroker.HandshakeAsync(clientMetadata, cancellationToken).ConfigureAwait(false);
			this.clientMetadata = clientMetadata;
		}
	}

	/// <summary>
	/// Sets the authorization service to use to obtain the default value for <see cref="ServiceActivationOptions.ClientCredentials"/>
	/// for all service requests that do not explicitly provide it.
	/// </summary>
	/// <param name="authorizationService">The authorization service. May be <see langword="null"/> to clear a previously set value.</param>
	/// <param name="joinableTaskFactory">A means to avoid deadlocks if the authorization service requires the main thread. May be null.</param>
	/// <remarks>
	/// This method is free threaded, but not thread-safe. It should not be called concurrently with itself.
	/// </remarks>
	[Obsolete("Use the overload that does not accept a JoinableTaskFactory instead. This overload will be removed in a future release.", error: true)]
	public void SetAuthorizationService(IAuthorizationService? authorizationService, JoinableTaskFactory? joinableTaskFactory)
	{
		this.SetAuthorizationService(authorizationService);
	}

	/// <summary>
	/// Sets the authorization service to use to obtain the default value for <see cref="ServiceActivationOptions.ClientCredentials"/>
	/// for all service requests that do not explicitly provide it.
	/// </summary>
	/// <param name="authorizationService">The authorization service. May be <see langword="null"/> to clear a previously set value.</param>
	/// <remarks>
	/// This method is free threaded, but not thread-safe. It should not be called concurrently with itself.
	/// </remarks>
	public void SetAuthorizationService(IAuthorizationService? authorizationService)
	{
		if (this.authorizationServiceClient?.AuthorizationService != authorizationService)
		{
			this.authorizationServiceClient?.Dispose();
			this.authorizationServiceClient = authorizationService != null ? new AuthorizationServiceClient(authorizationService, ownsAuthorizationService: false) : null;
		}
	}

	/// <inheritdoc />
	public async ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(serviceMoniker, nameof(serviceMoniker));
		Requires.Argument(options.ClientRpcTarget == null, nameof(options), "A non-null value for {0} is not supported by this method.", nameof(options.ClientRpcTarget));

		options = await this.ApplyActivationOptionDefaultsAsync(options, cancellationToken).ConfigureAwait(false);
		RemoteServiceConnectionInfo remoteConnectionInfo;
		try
		{
			remoteConnectionInfo = await this.remoteServiceBroker.RequestServiceChannelAsync(serviceMoniker, options, cancellationToken).ConfigureAwait(false);
		}
		catch (ObjectDisposedException)
		{
			// If we've lost our connection and/or been disposed, be polite during a delay between that event and when folks stop using our service broker
			// by simply returning null for the service. When aggregated with other service brokers, this provides the graceful fallback path that folks want.
			if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Error))
			{
				this.TraceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.ServiceRequestFailure, "Remote connection lost. Unable to complete request for service.");
			}

			return null;
		}

		try
		{
			remoteConnectionInfo.ThrowIfOutsideAllowedConnections(this.clientMetadata.SupportedConnections);
			if (remoteConnectionInfo.IsEmpty)
			{
				if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Warning))
				{
					this.TraceSource.TraceEvent(TraceEventType.Warning, (int)TraceEvents.RequestedServiceUnavailable, "Service \"{0}\" unavailable.", serviceMoniker);
				}

				return null;
			}
			else if (remoteConnectionInfo.MultiplexingChannelId.HasValue && this.multiplexingStream != null)
			{
				if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
				{
					this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.RequestedServiceUnavailable, "Service \"{0}\" available over MultiplexingStream channel {1}.", serviceMoniker, remoteConnectionInfo.MultiplexingChannelId.Value);
				}

				return this.multiplexingStream.AcceptChannel(remoteConnectionInfo.MultiplexingChannelId.Value);
			}
			else if (!string.IsNullOrWhiteSpace(remoteConnectionInfo.PipeName))
			{
				if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
				{
					this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.RequestedServiceUnavailable, "Service \"{0}\" available over named pipe \"{1}\".", serviceMoniker, remoteConnectionInfo.PipeName);
				}

				return await ConnectToPipeAsync(remoteConnectionInfo.PipeName!, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Error))
				{
					this.TraceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.IncompatibleServiceConnection, "Service \"{0}\" was available but over an incompatible connection.", serviceMoniker);
				}

				throw new NotSupportedException($"Unrecognized or unsupported connection instructions for service \"{serviceMoniker}\".");
			}
		}
		catch (Exception ex)
		{
			if (remoteConnectionInfo.RequestId.HasValue)
			{
				await this.remoteServiceBroker.CancelServiceRequestAsync(remoteConnectionInfo.RequestId.Value).ConfigureAwait(false);
			}

			if (cancellationToken.IsCancellationRequested && ex is OperationCanceledException)
			{
				// If canceled, rethrow the original exception rather than wrapping it.
				throw;
			}

			if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Error))
			{
				this.TraceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.ServiceConnectionFailure, "Service \"{0}\" was available but connecting to it failed: {1}", serviceMoniker, ex);
			}

			throw new ServiceActivationFailedException(serviceMoniker, ex);
		}
	}

	/// <inheritdoc />
	public async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		where T : class
	{
		Requires.NotNull(serviceDescriptor, nameof(serviceDescriptor));
		Requires.Argument(serviceDescriptor.ClientInterface == null || options.ClientRpcTarget != null, nameof(options), "{0} must be set to a non-null value for this service.", nameof(options.ClientRpcTarget));

		options = await this.ApplyActivationOptionDefaultsAsync(options, cancellationToken).ConfigureAwait(false);
		RemoteServiceConnectionInfo remoteConnectionInfo;
		try
		{
			remoteConnectionInfo = await this.remoteServiceBroker.RequestServiceChannelAsync(serviceDescriptor.Moniker, options, cancellationToken).ConfigureAwait(false);
		}
		catch (RemoteInvocationException ex)
		{
			throw new ServiceActivationFailedException(serviceDescriptor.Moniker, ex);
		}
		catch (ObjectDisposedException)
		{
			// If we've lost our connection and/or been disposed, be polite during a delay between that event and when folks stop using our service broker
			// by simply returning null for the service. When aggregated with other service brokers, this provides the graceful fallback path that folks want.
			return null;
		}

		IDuplexPipe? pipe = null;
		object? serviceObject = null;
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (remoteConnectionInfo.IsEmpty)
			{
				return null;
			}
			else if (remoteConnectionInfo.MultiplexingChannelId.HasValue && this.multiplexingStream != null)
			{
				if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
				{
					this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.RequestedServiceUnavailable, "Service \"{0}\" available over MultiplexingStream channel {1}.", serviceDescriptor.Moniker, remoteConnectionInfo.MultiplexingChannelId.Value);
				}

				pipe = this.multiplexingStream.AcceptChannel(remoteConnectionInfo.MultiplexingChannelId.Value);
			}
			else if (!string.IsNullOrWhiteSpace(remoteConnectionInfo.PipeName))
			{
				if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
				{
					this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.RequestedServiceUnavailable, "Service \"{0}\" available over named pipe \"{1}\".", serviceDescriptor.Moniker, remoteConnectionInfo.PipeName);
				}

				pipe = await ConnectToPipeAsync(remoteConnectionInfo.PipeName!, cancellationToken).ConfigureAwait(false);
			}
			else if (remoteConnectionInfo.ClrActivation != null)
			{
				if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
				{
					this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.RequestedServiceUnavailable, "Service \"{0}\" available for local activation as {1} from \"{2}\".", serviceDescriptor.Moniker, remoteConnectionInfo.ClrActivation.FullTypeName, remoteConnectionInfo.ClrActivation.AssemblyPath);
				}

				serviceObject = ActivateLocalService(remoteConnectionInfo.ClrActivation);
				return (T)serviceObject;
			}
			else
			{
				if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Error))
				{
					this.TraceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.IncompatibleServiceConnection, "Service \"{0}\" was available but over an incompatible connection.", serviceDescriptor.Moniker);
				}

				throw new NotSupportedException($"Unrecognized or unsupported connection instructions for service \"{serviceDescriptor.Moniker}\".");
			}

			if (this.multiplexingStream is object)
			{
				// Stream can be setup only for ServiceJsonRpcDescriptor.
				// We encourage users to migrate to descriptors configured with ServiceJsonRpcDescriptor.WithMultiplexingStream(MultiplexingStream.Options).
				if (serviceDescriptor is ServiceJsonRpcDescriptor serviceJsonRpcDescriptor && serviceJsonRpcDescriptor.MultiplexingStreamOptions is null)
				{
#pragma warning disable CS0618 // Type or member is obsolete, only for backward compatibility.
					serviceDescriptor = serviceJsonRpcDescriptor.WithMultiplexingStream(this.multiplexingStream);
#pragma warning restore CS0618 // Type or member is obsolete
				}
			}

			return serviceDescriptor.ConstructRpcForClient<T>(options.ClientRpcTarget, pipe);
		}
		catch (Exception ex)
		{
			(serviceObject as IDisposable)?.Dispose();

			await (pipe?.Input?.CompleteAsync(ex) ?? default).ConfigureAwait(false);
			await (pipe?.Output?.CompleteAsync(ex) ?? default).ConfigureAwait(false);

			// Cancel the service request if we haven't already connected to it and if it's cancelable.
			if (pipe == null && remoteConnectionInfo.RequestId.HasValue)
			{
				await this.remoteServiceBroker.CancelServiceRequestAsync(remoteConnectionInfo.RequestId.Value).ConfigureAwait(false);
			}

			if (cancellationToken.IsCancellationRequested && ex is OperationCanceledException)
			{
				// If canceled, rethrow the original exception rather than wrapping it.
				throw;
			}

			if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Error))
			{
				this.TraceSource.TraceEvent(TraceEventType.Error, (int)TraceEvents.ServiceConnectionFailure, "Service \"{0}\" was available but connecting to it failed: {1}", serviceDescriptor.Moniker, ex);
			}

			throw new ServiceActivationFailedException(serviceDescriptor.Moniker, ex);
		}
	}

	/// <inheritdoc />
	public virtual async ValueTask DisposeAsync()
	{
		this.remoteServiceBroker.AvailabilityChanged -= this.OnAvailabilityChanged;
		(this.remoteServiceBroker as IDisposable)?.Dispose();
		if (this.multiplexingStreamOwned && this.multiplexingStream is object)
		{
			await this.multiplexingStream.DisposeAsync().ConfigureAwait(false);
		}

		this.completionSource.TrySetResult(null);

		if (this.TraceSource.Switch.ShouldTrace(TraceEventType.Information))
		{
			this.TraceSource.TraceEvent(TraceEventType.Information, (int)TraceEvents.Disposed, "This instance has been disposed.");
		}

		this.authorizationServiceClient?.Dispose();
	}

	/// <inheritdoc />
	[Obsolete("Use DisposeAsync instead.")]
	public void Dispose()
	{
		this.Dispose(true);
	}

	/// <summary>
	/// Disposes of managed and/or unmanaged resources.
	/// </summary>
	/// <param name="disposing"><see langword="true"/> to dispose of managed resources as well as unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
	[Obsolete("Override DisposeAsync instead.")]
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
			this.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
		}
	}

	/// <summary>
	/// Raises the <see cref="AvailabilityChanged"/> event.
	/// </summary>
	/// <param name="sender">This parameter is ignored. The event will be raised with "this" as the sender.</param>
	/// <param name="args">Details regarding what changes have occurred.</param>
	protected virtual void OnAvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);

	private static async Task<IDuplexPipe> ConnectToPipeAsync(string pipeName, CancellationToken cancellationToken)
	{
		return (await ServerFactory.ConnectAsync(pipeName, cancellationToken).ConfigureAwait(false))
			.UsePipe(cancellationToken: CancellationToken.None);
	}

	/// <summary>
	/// Activates a service within the current AppDomain.
	/// </summary>
	/// <param name="serviceActivation">Details on which service to activate.</param>
	/// <returns>The activated service object.</returns>
	[RequiresUnreferencedCode(Reasons.TypeLoad)]
	private static object ActivateLocalService(RemoteServiceConnectionInfo.LocalCLRServiceActivation serviceActivation)
	{
		Requires.NotNull(serviceActivation, nameof(serviceActivation));

		var assembly = Assembly.LoadFrom(serviceActivation.AssemblyPath);
		Type? type = assembly.GetType(serviceActivation.FullTypeName, throwOnError: true);
		if (type is object)
		{
			object? service = Activator.CreateInstance(type);
			if (service is object)
			{
				return service;
			}
		}

		throw new ServiceCompositionException("Unable to activate the type.");
	}

	/// <summary>
	/// Prepares a description of the kind of service host we can proffer.
	/// </summary>
	/// <returns>A description of our own local service host capabilities.</returns>
	private static ServiceHostInformation GetLocalServiceHostInformation()
	{
		var serviceHostInformation = new ServiceHostInformation
		{
			OperatingSystem =
				RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ServiceHostOperatingSystem.Windows :
				RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? ServiceHostOperatingSystem.Linux :
				RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ServiceHostOperatingSystem.OSX :
				throw new NotSupportedException("Unrecognized OS"),
			ProcessArchitecture = RuntimeInformation.ProcessArchitecture,
			Runtime =
				RuntimeInformation.FrameworkDescription.StartsWith(NETFrameworkDescription, StringComparison.Ordinal) ? ServiceHostRuntime.NETFramework :
				RuntimeInformation.FrameworkDescription.StartsWith(NETCoreDescription, StringComparison.Ordinal) ? ServiceHostRuntime.NETCore :
				RuntimeInformation.FrameworkDescription.StartsWith(NET5PlusDescription, StringComparison.Ordinal) ? ServiceHostRuntime.NETCore :
				Type.GetType("Mono.Runtime") != null ? ServiceHostRuntime.Mono :
				throw new NotSupportedException("Unrecognized FrameworkDescription: " + RuntimeInformation.FrameworkDescription),
			RuntimeVersion = Version.Parse(VersionFinder.Match(RuntimeInformation.FrameworkDescription).Value),
		};
		if (serviceHostInformation.Runtime == ServiceHostRuntime.NETCore)
		{
			serviceHostInformation.RuntimeVersion = Environment.Version;
		}

		return serviceHostInformation;
	}

	private async Task<ServiceActivationOptions> ApplyActivationOptionDefaultsAsync(ServiceActivationOptions options, CancellationToken cancellationToken)
	{
		options.SetClientDefaults();
		if (this.authorizationServiceClient != null && options.ClientCredentials == null)
		{
			options.ClientCredentials = await this.authorizationServiceClient.GetCredentialsAsync(cancellationToken).ConfigureAwait(false);
		}

		return options;
	}
}
