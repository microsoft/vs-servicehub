// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using System.IO.Pipes;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Microsoft.Win32.SafeHandles;
using Nerdbank.Streams;
using Windows.Win32;
using IAsyncDisposable = System.IAsyncDisposable;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

internal class RemoteServiceBrokerWrapper : IRemoteServiceBroker, IDisposable
{
	// Keep in sync with property in {DevCore repo}/src/clr/utiltity/Microsoft.ServiceHub.Utiltity.Shared/Constants.cs
	private const string ServiceHubHostProcessIdActivationArgument = "__servicehub__HostProcessId";

	private readonly IServiceBroker serviceBroker;
	private ImmutableDictionary<Guid, IAsyncDisposable> remoteServiceRequests = ImmutableDictionary<Guid, IAsyncDisposable>.Empty;

	public RemoteServiceBrokerWrapper(IServiceBroker serviceBroker)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		this.serviceBroker = serviceBroker;
		this.serviceBroker.AvailabilityChanged += this.OnAvailabilityChanged;
	}

	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	/// <inheritdoc />
	public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken = default)
	{
		if (!clientMetadata.SupportedConnections.HasFlag(RemoteServiceConnections.IpcPipe))
		{
			// Only support pipes.
			throw new NotSupportedException();
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken = default)
	{
		return this.RequestServiceChannelAsync(
			() => this.serviceBroker.GetPipeAsync(serviceMoniker, serviceActivationOptions, cancellationToken),
			serviceMoniker,
			serviceActivationOptions,
			cancellationToken);
	}

	/// <inheritdoc />
	public async Task CancelServiceRequestAsync(Guid serviceRequestId)
	{
		if (ImmutableInterlocked.TryRemove(ref this.remoteServiceRequests, serviceRequestId, out IAsyncDisposable? server))
		{
			await server.DisposeAsync().ConfigureAwait(false);
		}
	}

	public void Dispose()
	{
		this.serviceBroker.AvailabilityChanged -= this.OnAvailabilityChanged;
	}

	internal Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(
		Func<ValueTask<IDuplexPipe?>> getPipeAsyncDelegate,
		ServiceMoniker serviceMoniker,
		ServiceActivationOptions serviceActivationOptions,
		CancellationToken cancellationToken)
	{
		var requestId = Guid.NewGuid();

		if (serviceActivationOptions.ActivationArguments == null ||
			!serviceActivationOptions.ActivationArguments.TryGetValue(ServiceHubHostProcessIdActivationArgument, out string? hostProcessidString) ||
			!int.TryParse(hostProcessidString, out int activationOptionProcessId))
		{
			throw new InvalidOperationException(string.Format(Strings.Error_InvalidExternalClientProcessPid, nameof(ServiceActivationOptions), ServiceHubHostProcessIdActivationArgument));
		}

		IIpcServer server = ServerFactory.Create(
			async (stream) =>
			{
#if NET5_0_OR_GREATER
				if (OperatingSystem.IsWindowsVersionAtLeast(8))
#else
				if (Environment.OSVersion.Platform == PlatformID.Win32NT)
#endif
				{
					if (!(stream is PipeStream { SafePipeHandle: SafePipeHandle handle }))
					{
						throw new InvalidOperationException(Strings.NamedPipeClientCouldNotBeValidated);
					}

					if (!PInvoke.GetNamedPipeClientProcessId(handle, out uint clientProcessId))
					{
						throw new InvalidOperationException(Strings.CouldNotGetProcessId);
					}

					if (clientProcessId != activationOptionProcessId)
					{
						throw new InvalidOperationException(string.Format(Strings.FailedToValidateClient, serviceMoniker.ToString()));
					}
				}

				// Cancelling the service request ends up disposing the server and removing it from the list of
				// active requests which is exactly the behavior we want here.
				// This task is run anonymously because a server cannot be disposed if it is inside
				// its OnConnected callback so it will be result in a deadlock.
				Task.Run(() => this.CancelServiceRequestAsync(requestId)).Forget();

				IDuplexPipe? servicePipe = await getPipeAsyncDelegate().ConfigureAwait(false);

				if (servicePipe != null)
				{
					IDuplexPipe serverPipe = stream.UsePipe();

					// Link the two pipes so that all incoming/outgoing calls get forwarded
					ServiceBrokerUtilities.LinkAsync(serverPipe.Input, servicePipe.Output, cancellationToken).Forget();
					ServiceBrokerUtilities.LinkAsync(servicePipe.Input, serverPipe.Output, cancellationToken).Forget();
				}
				else
				{
					await stream.DisposeAsync().ConfigureAwait(false);
				}
			},
			new ServerFactory.ServerOptions
			{
				TraceSource = new TraceSource($"{this.serviceBroker.GetType().Name}-{serviceMoniker.Name}"),
			});

		ImmutableInterlocked.TryAdd(ref this.remoteServiceRequests, requestId, server);

		return Task.FromResult(new RemoteServiceConnectionInfo
		{
			RequestId = requestId,
			PipeName = server.Name,
		});
	}

	private void OnAvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs args)
	{
		this.AvailabilityChanged?.Invoke(this, args);
	}
}
