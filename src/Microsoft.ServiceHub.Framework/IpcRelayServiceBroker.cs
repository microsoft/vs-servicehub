// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.VisualStudio.Threading;
using IAsyncDisposable = System.IAsyncDisposable;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An <see cref="IRemoteServiceBroker"/> which proffers all services from another <see cref="IServiceBroker"/>
/// over named pipes on Windows or Unix domain sockets on other operating systems.
/// </summary>
public class IpcRelayServiceBroker : IRemoteServiceBroker, IDisposable
{
	private readonly IServiceBroker serviceBroker;

	/// <summary>
	/// An event to set upon disposal.
	/// </summary>
	private readonly AsyncManualResetEvent disposedEvent = new AsyncManualResetEvent();

	private ImmutableDictionary<Guid, IAsyncDisposable> remoteServiceRequests = ImmutableDictionary<Guid, IAsyncDisposable>.Empty;

	/// <summary>
	/// Initializes a new instance of the <see cref="IpcRelayServiceBroker"/> class.
	/// </summary>
	/// <param name="serviceBroker">The service broker whose services are to be exposed.</param>
	public IpcRelayServiceBroker(IServiceBroker serviceBroker)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		this.serviceBroker = serviceBroker;
	}

	/// <inheritdoc/>
	public event EventHandler<BrokeredServicesChangedEventArgs> AvailabilityChanged
	{
		add => this.serviceBroker.AvailabilityChanged += value;
		remove => this.serviceBroker.AvailabilityChanged -= value;
	}

	/// <summary>
	/// Gets the logging mechanism.
	/// </summary>
	public TraceSource? TraceSource { get; init; }

	/// <summary>
	/// Gets a <see cref="Task"/> that completes when this instance is disposed of.
	/// </summary>
	/// <remarks>
	/// This event will occur when the client disconnects from the relay,
	/// if the RPC library is configured to dispose target objects at that time.
	/// </remarks>
	public Task Completion => this.disposedEvent.WaitAsync();

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
	public async Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken = default)
	{
		DisposableBag faultOrCancelBag = new();
		try
		{
			IDuplexPipe? servicePipe = await this.serviceBroker.GetPipeAsync(serviceMoniker, serviceActivationOptions, cancellationToken).ConfigureAwait(false);
			if (servicePipe == null)
			{
				return default;
			}

			faultOrCancelBag.AddDisposable(new DisposeAction(() =>
			{
				servicePipe.Input?.Complete();
				servicePipe.Output?.Complete();
			}));

			var requestId = Guid.NewGuid();

			IIpcServer server = ServerFactory.Create(
				stream => this.HandleIncomingConnectionAsync(stream, requestId, servicePipe),
				new ServerFactory.ServerOptions { TraceSource = this.TraceSource });
			Assumes.True(faultOrCancelBag.TryAddDisposable(server));

			ImmutableInterlocked.TryAdd(ref this.remoteServiceRequests, requestId, faultOrCancelBag);

			return new RemoteServiceConnectionInfo
			{
				RequestId = requestId,
				PipeName = server.Name,
			};
		}
		catch
		{
			await faultOrCancelBag.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <inheritdoc />
	public async Task CancelServiceRequestAsync(Guid serviceRequestId)
	{
		if (ImmutableInterlocked.TryRemove(ref this.remoteServiceRequests, serviceRequestId, out IAsyncDisposable? disposable))
		{
			await disposable.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <inheritdoc/>
	public void Dispose()
	{
		this.Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Disposes managed and unmanaged resources owned by this instance.
	/// </summary>
	/// <param name="disposing"><see langword="true" /> if this object is being disposed; <see langword="false" /> if it is being finalized.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			this.disposedEvent.Set();
		}
	}

	private async Task HandleIncomingConnectionAsync(Stream stream, Guid requestId, IDuplexPipe servicePipe)
	{
		// Once a connection is made (or fails), it is no longer cancelable.
		ImmutableInterlocked.TryRemove(ref this.remoteServiceRequests, requestId, out IAsyncDisposable _);

		// Link the two pipes so that all incoming/outgoing calls get forwarded
		await Task.WhenAll(
			stream.CopyToAsync(servicePipe.Output),
			servicePipe.Input.CopyToAsync(stream)).ConfigureAwait(false);
	}
}
