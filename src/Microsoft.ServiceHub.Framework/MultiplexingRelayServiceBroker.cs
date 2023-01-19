// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.IO.Pipelines;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An <see cref="IRemoteServiceBroker"/> which proffers all services from another <see cref="IServiceBroker"/>
/// over an existing <see cref="MultiplexingStream"/>.
/// </summary>
public class MultiplexingRelayServiceBroker : IRemoteServiceBroker, IDisposable, System.IAsyncDisposable
{
	/// <summary>
	/// The broker whose services are relayed by this instance.
	/// </summary>
	private readonly IServiceBroker serviceBroker;

	/// <summary>
	/// The multiplexing stream shared with the client. Never null.
	/// </summary>
	private readonly MultiplexingStream multiplexingStreamWithClient;

	/// <summary>
	/// The multiplexing channels currently offered (and not yet accepted or rejected) to the client.
	/// </summary>
	private readonly ConcurrentDictionary<Guid, MultiplexingStream.Channel> channelsOfferedToClient = new ConcurrentDictionary<Guid, MultiplexingStream.Channel>();

	/// <summary>
	/// An event to set upon disposal.
	/// </summary>
	private readonly AsyncManualResetEvent disposedEvent = new AsyncManualResetEvent();

	/// <summary>
	/// A value indicating whether to dispose of the <see cref="multiplexingStreamWithClient"/> upon disposal.
	/// </summary>
	private bool multiplexingStreamWithRemoteClientOwned;

	/// <summary>
	/// Initializes a new instance of the <see cref="MultiplexingRelayServiceBroker"/> class.
	/// </summary>
	/// <param name="serviceBroker">The service broker whose services should be multiplexed to the <paramref name="multiplexingStreamWithClient"/>.</param>
	/// <param name="multiplexingStreamWithClient">The multiplexing stream to proffer services on.</param>
	public MultiplexingRelayServiceBroker(IServiceBroker serviceBroker, MultiplexingStream multiplexingStreamWithClient)
	{
		this.serviceBroker = serviceBroker ?? throw new ArgumentNullException(nameof(serviceBroker));
		this.serviceBroker.AvailabilityChanged += this.OnAvailabilityChanged;
		this.multiplexingStreamWithClient = multiplexingStreamWithClient ?? throw new ArgumentNullException(nameof(multiplexingStreamWithClient));
	}

	/// <inheritdoc />
	public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

	/// <summary>
	/// Gets a <see cref="Task"/> that completes when this instance is disposed of.
	/// </summary>
	/// <remarks>
	/// This event will occur when the client disconnects from the relay,
	/// if the RPC library is configured to dispose target objects at that time.
	/// </remarks>
	public Task Completion => this.disposedEvent.WaitAsync();

	/// <summary>
	/// Initializes a new instance of the <see cref="MultiplexingRelayServiceBroker"/> class
	/// and establishes a <see cref="MultiplexingStream"/> protocol with the client over the given stream.
	/// </summary>
	/// <param name="serviceBroker">A broker for services to be relayed.</param>
	/// <param name="duplexStreamWithClient">
	/// The duplex stream over which the client will make RPC calls to the returned <see cref="IRemoteServiceBroker"/> instance.
	/// A multiplexing stream will be established on this stream and the client is expected to accept an offer for a channel with an <see cref="string.Empty"/> name.
	/// This object is considered "owned" by the returned <see cref="MultiplexingRelayServiceBroker"/> and will be disposed when the returned value is disposed,
	/// or disposed before this method throws.
	/// </param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A <see cref="MultiplexingRelayServiceBroker"/> that provides access to remote services, all over a multiplexing stream.</returns>
	/// <remarks>
	/// The <see cref="FrameworkServices.RemoteServiceBroker"/> is used as the wire protocol.
	/// </remarks>
	public static async Task<MultiplexingRelayServiceBroker> ConnectToServerAsync(IServiceBroker serviceBroker, Stream duplexStreamWithClient, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));
		Requires.NotNull(duplexStreamWithClient, nameof(duplexStreamWithClient));

		try
		{
			MultiplexingStream multiplexingStreamWithClient = await MultiplexingStream.CreateAsync(duplexStreamWithClient, cancellationToken).ConfigureAwait(false);
			MultiplexingStream.Channel clientChannel = await multiplexingStreamWithClient.OfferChannelAsync(string.Empty, cancellationToken).ConfigureAwait(false);
			var result = new MultiplexingRelayServiceBroker(serviceBroker, multiplexingStreamWithClient)
			{
				multiplexingStreamWithRemoteClientOwned = true,
			};
			FrameworkServices.RemoteServiceBroker.ConstructRpc(result, clientChannel);
			return result;
		}
		catch
		{
			duplexStreamWithClient.Dispose();
			throw;
		}
	}

	/// <inheritdoc />
	public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken = default)
	{
		if (!clientMetadata.SupportedConnections.HasFlag(RemoteServiceConnections.Multiplexing))
		{
			return Task.FromException(new NotSupportedException("The client must support multiplexing to use this service broker."));
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public async Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
	{
		Requires.NotNull(serviceMoniker, nameof(serviceMoniker));

		var requestId = Guid.NewGuid();

		options.MultiplexingStream = this.multiplexingStreamWithClient;
		IDuplexPipe? servicePipe = await this.serviceBroker.GetPipeAsync(serviceMoniker, options, cancellationToken).ConfigureAwait(false);
		if (servicePipe == null)
		{
			return default;
		}

		var channelOptions = new MultiplexingStream.ChannelOptions
		{
			ExistingPipe = servicePipe,
		};
		MultiplexingStream.Channel outerChannel = this.multiplexingStreamWithClient.CreateChannel(channelOptions);
		Assumes.True(this.channelsOfferedToClient.TryAdd(requestId, outerChannel));
		outerChannel.Acceptance.ContinueWith(_ => this.channelsOfferedToClient.TryRemove(requestId, out MultiplexingStream.Channel _), TaskScheduler.Default).Forget();

		return new RemoteServiceConnectionInfo
		{
			RequestId = requestId,
			MultiplexingChannelId = outerChannel.QualifiedId.Id,
		};
	}

	/// <inheritdoc />
	public Task CancelServiceRequestAsync(Guid serviceRequestId)
	{
		Verify.Operation(this.channelsOfferedToClient.TryRemove(serviceRequestId, out MultiplexingStream.Channel? channel), "Request to cancel a channel that is not awaiting acceptance.");
		channel.Dispose();
		return Task.CompletedTask;
	}

	/// <inheritdoc/>
	public virtual async ValueTask DisposeAsync()
	{
		this.serviceBroker.AvailabilityChanged -= this.OnAvailabilityChanged;
		if (this.multiplexingStreamWithRemoteClientOwned)
		{
			await this.multiplexingStreamWithClient.DisposeAsync().ConfigureAwait(false);
		}

		this.disposedEvent.Set();
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
}
