// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.IO.Pipes;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;

public partial class IpcPipeRelayServiceBrokerTests : TestBase, IAsyncLifetime
{
	private MockServiceBroker innerServer = new MockServiceBroker();
	private IpcRelayServiceBroker relayBroker;
	private RemoteServiceBroker relayClientBroker = null!; // initialized by InitializeAsync

	public IpcPipeRelayServiceBrokerTests(ITestOutputHelper logger)
		: base(logger)
	{
		this.relayBroker = new IpcRelayServiceBroker(this.innerServer);
	}

	public async ValueTask InitializeAsync()
	{
		this.relayClientBroker = await RemoteServiceBroker.ConnectToServerAsync(this.relayBroker);
	}

	public async ValueTask DisposeAsync()
	{
		if (this.relayClientBroker != null)
		{
			await this.relayClientBroker.DisposeAsync();
		}

		this.relayBroker.Dispose();
	}

	[Fact]
	public void Ctor_ValidatesInput()
	{
		Assert.Throws<ArgumentNullException>(() => new IpcRelayServiceBroker(null!));
	}

	[Fact]
	public async Task Completion()
	{
		Assert.False(this.relayBroker.Completion.IsCompleted);
		this.relayBroker.Dispose();
		await this.relayBroker.Completion.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task Handshake()
	{
		await Assert.ThrowsAsync<NotSupportedException>(() => this.relayBroker.HandshakeAsync(new ServiceBrokerClientMetadata { SupportedConnections = RemoteServiceConnections.Multiplexing }, this.TimeoutToken));
		await this.relayBroker.HandshakeAsync(new ServiceBrokerClientMetadata { SupportedConnections = RemoteServiceConnections.IpcPipe }, this.TimeoutToken);
	}

	[Fact]
	public async Task CompleteRequest()
	{
		ICalculator? calc = await this.relayClientBroker.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
		using (calc as IDisposable)
		{
			Assert.NotNull(calc);
			int result = await calc!.AddAsync(3, 5);
			Assert.Equal(8, result);
		}
	}

	[Fact]
	public async Task NoService()
	{
		Assert.Null(await this.relayClientBroker.GetProxyAsync<ICalculator>(TestServices.DoesNotExist, this.TimeoutToken));
	}

	[Fact]
	public async Task ServiceFactoryThrows()
	{
		await Assert.ThrowsAsync<ServiceActivationFailedException>(async () => await this.relayClientBroker.GetProxyAsync<ICalculator>(TestServices.Throws, this.TimeoutToken));
	}

	[Fact]
	public async Task CanceledRequest()
	{
		RemoteServiceConnectionInfo connectionInfo = await this.relayBroker.RequestServiceChannelAsync(TestServices.Calculator.Moniker, default, this.TimeoutToken);
		Assert.NotNull(connectionInfo.PipeName);
		Assert.NotNull(connectionInfo.RequestId);
		await this.relayBroker.CancelServiceRequestAsync(connectionInfo.RequestId!.Value);

		// Assert that the service is no longer offered.
		var pipe = new NamedPipeClientStream(connectionInfo.PipeName!);
		await Assert.ThrowsAsync<TimeoutException>(() => pipe.ConnectAsync((int)AsyncDelay.TotalMilliseconds, this.TimeoutToken));
	}

	[Fact]
	public async Task AvailabilityChangedEvent()
	{
		var eventArgs = new TaskCompletionSource<BrokeredServicesChangedEventArgs>();
		EventHandler<BrokeredServicesChangedEventArgs> handler = (s, e) =>
		{
			eventArgs.SetResult(e);
		};
		this.relayBroker.AvailabilityChanged += handler;
		this.innerServer.OnAvailabilityChanged(new BrokeredServicesChangedEventArgs(ImmutableHashSet<ServiceMoniker>.Empty, otherServicesImpacted: true));
		BrokeredServicesChangedEventArgs args = await eventArgs.Task.WithCancellation(this.TimeoutToken);
		Assert.NotNull(args);

		eventArgs = new TaskCompletionSource<BrokeredServicesChangedEventArgs>();
		this.relayBroker.AvailabilityChanged -= handler;
		this.innerServer.OnAvailabilityChanged(new BrokeredServicesChangedEventArgs(ImmutableHashSet<ServiceMoniker>.Empty, otherServicesImpacted: true));
		await Assert.ThrowsAsync<TimeoutException>(() => eventArgs.Task.WithTimeout(AsyncDelay));
	}
}
