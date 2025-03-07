// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using StreamJsonRpc;

public class MultiplexingRelayServiceBrokerTests : TestBase
{
	private IServiceBroker innerServer = new MockServiceBroker();

	public MultiplexingRelayServiceBrokerTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	[Fact]
	public async Task ConnectToServer_Stream()
	{
		RemoteServiceBroker client = await this.GetRelayClientAsync();
		await this.AssertCalculatorService(client);
	}

	[Fact]
	public async Task ConnectToServer_MultiplexingStream()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task<RemoteServiceBroker> clientTask = RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item1, this.TimeoutToken);
		Task<MultiplexingStream> serverTask = MultiplexingStream.CreateAsync(pair.Item2, this.TimeoutToken);
		MultiplexingStream serverMxStream = await serverTask;
		MultiplexingStream.Channel relayChannel = await serverMxStream.OfferChannelAsync(string.Empty, this.TimeoutToken);
		var relay = new MultiplexingRelayServiceBroker(this.innerServer, serverMxStream);
		FrameworkServices.RemoteServiceBroker.ConstructRpc(relay, relayChannel);

		await this.AssertCalculatorService(await clientTask);
	}

	[Fact]
	public async Task CancelPendingServiceRequest()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task<MultiplexingRelayServiceBroker> relayTask = MultiplexingRelayServiceBroker.ConnectToServerAsync(this.innerServer, pair.Item2, this.TimeoutToken);
		MultiplexingStream clientMx = await MultiplexingStream.CreateAsync(pair.Item1, this.TimeoutToken);
		MultiplexingStream.Channel clientChannel = await clientMx.AcceptChannelAsync(string.Empty, this.TimeoutToken);
		await relayTask.WithCancellation(this.TimeoutToken);

		IRemoteServiceBroker clientBrokerProxy = FrameworkServices.RemoteServiceBroker.ConstructRpc<IRemoteServiceBroker>(clientChannel);
		RemoteServiceConnectionInfo connectionInfo = await clientBrokerProxy.RequestServiceChannelAsync(TestServices.Calculator.Moniker, cancellationToken: this.TimeoutToken);
		await clientBrokerProxy.CancelServiceRequestAsync(connectionInfo.RequestId!.Value);

		// This test fails about 50% of the time without this sleep
		Thread.Sleep(50);

		// Assert that the offer for the calculator channel is rescinded.
		Assert.ThrowsAny<InvalidOperationException>(() => clientMx.AcceptChannel(connectionInfo.MultiplexingChannelId!.Value));
	}

	[Fact]
	public async Task RequestNonExistentService()
	{
		RemoteServiceBroker client = await this.GetRelayClientAsync();
		IDuplexPipe? pipe = await client.GetPipeAsync(TestServices.DoesNotExist.Moniker, this.TimeoutToken);
		Assert.Null(pipe);
	}

	[Fact]
	public async Task Handshake_WithoutMultiplexing()
	{
		IRemoteServiceBroker client = await this.GetRemoteClientProxyAsync();
		await Assert.ThrowsAsync<RemoteInvocationException>(() => client.HandshakeAsync(new ServiceBrokerClientMetadata { SupportedConnections = RemoteServiceConnections.IpcPipe }, this.TimeoutToken));
	}

	[Fact]
	public async Task ConnectToServerAsync_Canceled()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MultiplexingRelayServiceBroker.ConnectToServerAsync(new MockServiceBroker(), pair.Item1, new CancellationToken(canceled: true)));
		Assert.True(((IDisposableObservable)pair.Item1).IsDisposed);
	}

	[Fact]
	public async Task Ctor_ValidatesInputs()
	{
		Assert.Throws<ArgumentNullException>(() => new MultiplexingRelayServiceBroker(null!, null!));
		Assert.Throws<ArgumentNullException>(() => new MultiplexingRelayServiceBroker(new MockServiceBroker(), null!));

		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task<MultiplexingStream> clientTask = MultiplexingStream.CreateAsync(pair.Item1, this.TimeoutToken);
		Task<MultiplexingStream> serverTask = MultiplexingStream.CreateAsync(pair.Item2, this.TimeoutToken);

		MultiplexingStream clientMx = await clientTask;
		Assert.Throws<ArgumentNullException>(() => new MultiplexingRelayServiceBroker(null!, clientMx));
	}

	[Fact]
	public async Task Completion_ClientClosed()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task<RemoteServiceBroker> clientTask = RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item1, this.TimeoutToken);
		Task<MultiplexingStream> serverTask = MultiplexingStream.CreateAsync(pair.Item2, this.TimeoutToken);
		MultiplexingStream serverMxStream = await serverTask;
		MultiplexingStream.Channel relayChannel = await serverMxStream.OfferChannelAsync(string.Empty, this.TimeoutToken);
		var relay = new MultiplexingRelayServiceBroker(this.innerServer, serverMxStream);
		FrameworkServices.RemoteServiceBroker.ConstructRpc(relay, relayChannel);
		Assert.False(relay.Completion.IsCompleted);

		relayChannel.Dispose();
		await relay.Completion.WithCancellation(this.TimeoutToken);
	}

	private async Task<RemoteServiceBroker> GetRelayClientAsync()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task<RemoteServiceBroker> clientTask = RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item1, this.TimeoutToken);
		Task<MultiplexingRelayServiceBroker> relayTask = MultiplexingRelayServiceBroker.ConnectToServerAsync(this.innerServer, pair.Item2, this.TimeoutToken);

		await relayTask.WithCancellation(this.TimeoutToken);
		return await clientTask.WithCancellation(this.TimeoutToken);
	}

	private async Task<IRemoteServiceBroker> GetRemoteClientProxyAsync()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task<MultiplexingRelayServiceBroker> relayTask = MultiplexingRelayServiceBroker.ConnectToServerAsync(this.innerServer, pair.Item2, this.TimeoutToken);
		MultiplexingStream clientMx = await MultiplexingStream.CreateAsync(pair.Item1, this.TimeoutToken);
		MultiplexingStream.Channel clientChannel = await clientMx.AcceptChannelAsync(string.Empty, this.TimeoutToken);
		await relayTask.WithCancellation(this.TimeoutToken);

		IRemoteServiceBroker clientBrokerProxy = FrameworkServices.RemoteServiceBroker.ConstructRpc<IRemoteServiceBroker>(clientChannel);
		return clientBrokerProxy;
	}

	private async Task AssertCalculatorService(IServiceBroker serviceBroker)
	{
		try
		{
			ICalculator? rpc = await serviceBroker.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
			Assumes.NotNull(rpc);
			Assert.Equal(8, await rpc.AddAsync(3, 5).WithCancellation(this.TimeoutToken));
			((IDisposable)rpc).Dispose();
		}
		finally
		{
			(serviceBroker as IDisposable)?.Dispose();
		}
	}
}
