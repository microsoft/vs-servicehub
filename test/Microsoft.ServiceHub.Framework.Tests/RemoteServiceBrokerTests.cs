// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.IO.Pipes;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public partial class RemoteServiceBrokerTests : TestBase
{
	public RemoteServiceBrokerTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	public enum RemoteServiceBrokerKinds
	{
		LocalActivation,
		Pipes,
		MultiplexingChannel,
	}

	private interface IUnsupportedInterface
	{
		int SomeProperty { get; }
	}

	[Fact]
	public async Task ConnectToServerAsync_ValidatesInputs()
	{
		await Assert.ThrowsAsync<ArgumentNullException>(() => RemoteServiceBroker.ConnectToServerAsync((System.IO.Pipelines.IDuplexPipe)null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => RemoteServiceBroker.ConnectToServerAsync((IRemoteServiceBroker)null!));
	}

	[Fact]
	public async Task ConnectToMultiplexingServerAsync_ValidatesInputs()
	{
		await Assert.ThrowsAsync<ArgumentNullException>(() => RemoteServiceBroker.ConnectToMultiplexingServerAsync((Stream)null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => RemoteServiceBroker.ConnectToMultiplexingServerAsync((IRemoteServiceBroker)null!, null!));
		await Assert.ThrowsAsync<ArgumentNullException>(() => RemoteServiceBroker.ConnectToMultiplexingServerAsync(new EmptyRemoteServiceBroker(), null!));
	}

	[Fact]
	public async Task ConnectToMultiplexingServerAsync_AndDispose()
	{
		var server = new EmptyRemoteServiceBroker();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => server, this.TimeoutToken);
		Task brokerCompletion;
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);
			Assumes.True(server.ClientMetadata.HasValue);
			Assert.True(server.ClientMetadata.Value.SupportedConnections.HasFlag(RemoteServiceConnections.Multiplexing));
			AssertCommonServiceConnections(server.ClientMetadata.Value.SupportedConnections, localServiceHostOffered: false);
			await broker.OfferLocalServiceHostAsync(this.TimeoutToken);
			AssertCommonServiceConnections(server.ClientMetadata.Value.SupportedConnections, localServiceHostOffered: true);
			Assert.False(broker.Completion.IsCompleted);
			brokerCompletion = broker.Completion;
		}

		Assert.True(brokerCompletion.IsCompleted);

		// Verify that the server received the closing notification, and rethrow any of its exceptions.
		await serverTask.WithCancellation(this.TimeoutToken);
		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task ConnectToMultiplexingServerAsync_ServerDisconnects()
	{
		var server = new EmptyRemoteServiceBroker();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		var serverDisconnectSource = CancellationTokenSource.CreateLinkedTokenSource(this.TimeoutToken);
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => server, serverDisconnectSource.Token);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);
			serverDisconnectSource.Cancel();
			await broker.Completion.WithCancellation(this.TimeoutToken);
		}

		// Verify that the server received the closing notification, and rethrow any of its exceptions.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask).WithCancellation(this.TimeoutToken);
		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);
	}

	[Fact]
	[Trait("WindowsOnly", "true")]
	public async Task ConnectToServerAsync_IRemoteServiceBroker_ServerDisconnects()
	{
		var server = new EmptyRemoteServiceBroker();
		string guid = Guid.NewGuid().ToString();
		var resetEvent = new AsyncManualResetEvent();

		using (var pipe = new NamedPipeServerStream(
				 guid,
				 PipeDirection.InOut,
				 -1,
				 PipeTransmissionMode.Byte,
				 PipeOptions.Asynchronous))
		{
			var serverConnectionTask = Task.Run(async () =>
			{
				await pipe.WaitForConnectionAsync(this.TimeoutToken);
				var connection = (ServiceJsonRpcDescriptor.JsonRpcConnection)FrameworkServices.RemoteServiceBroker.ConstructRpcConnection(pipe.UsePipe());
				connection.AddLocalRpcTarget(server);
				connection.JsonRpc.Disconnected += (sender, e) =>
				{
					resetEvent.Set();
				};
				connection.StartListening();
			});

			using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(guid, this.TimeoutToken))
			{
			}

			await serverConnectionTask;

			// Verify that the server received the closing notification
			await resetEvent.WaitAsync(this.TimeoutToken);
		}
	}

	[Fact]
	public async Task ConnectToMultiplexingServerAsync_IRemoteServiceBroker_ServerDisconnects()
	{
		var server = new EmptyRemoteServiceBroker();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		var serverDisconnectSource = CancellationTokenSource.CreateLinkedTokenSource(this.TimeoutToken);
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => server, serverDisconnectSource.Token);

		using (MultiplexingStream clientMxStream = await MultiplexingStream.CreateAsync(pair.Item2, this.TimeoutToken))
		{
			using (MultiplexingStream.Channel hostServiceChannel = await clientMxStream.AcceptChannelAsync(string.Empty, this.TimeoutToken))
			{
				IRemoteServiceBroker serviceBroker = FrameworkServices.RemoteServiceBroker.ConstructRpc<IRemoteServiceBroker>(hostServiceChannel);
				using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(serviceBroker, clientMxStream, this.TimeoutToken))
				{
					this.SetupTraceListener(broker);
					serverDisconnectSource.Cancel();
					await broker.Completion.WithCancellation(this.TimeoutToken);
				}
			}
		}

		// Verify that the server received the closing notification, and rethrow any of its exceptions.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask).WithCancellation(this.TimeoutToken);
		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task ConnectToMultiplexingServerAsync_HandshakeFails()
	{
		var server = new AlwaysThrowServiceBroker();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => server, this.TimeoutToken);
		await Assert.ThrowsAsync<StreamJsonRpc.RemoteInvocationException>(() => RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.TimeoutToken));

		// Verify that the server received the closing notification, and rethrow any of its exceptions.
		await serverTask.WithCancellation(this.TimeoutToken);
		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);

		// Verify that the method took ownership of the stream and disposed it on failure.
		Assert.True(((IDisposableObservable)pair.Item2).IsDisposed);
	}

	[Fact]
	public async Task ConnectToMultiplexingServerAsync_Proxy_HandshakeFails()
	{
		var server = new AlwaysThrowServiceBroker();
		MultiplexingStream mx = await CreateStubMultiplexingStreamAsync(this.TimeoutToken);
		await Assert.ThrowsAsync<NotImplementedException>(() => RemoteServiceBroker.ConnectToMultiplexingServerAsync(server, mx, this.TimeoutToken));
		Assert.True(server.Disposed.IsSet);

		// The multiplexing stream should NOT be disposed, since the server was just one channel of that.
		Assert.False(((IDisposableObservable)mx).IsDisposed);
	}

	[Fact]
	public async Task ConnectToMultiplexingServerAsync_Proxy()
	{
		var server = new EmptyRemoteServiceBroker();
		MultiplexingStream mx = await CreateStubMultiplexingStreamAsync(this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(server, mx, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);
			Assumes.True(server.ClientMetadata.HasValue);
			Assert.True(server.ClientMetadata.Value.SupportedConnections.HasFlag(RemoteServiceConnections.Multiplexing));
			AssertCommonServiceConnections(server.ClientMetadata.Value.SupportedConnections, localServiceHostOffered: false);
			await broker.OfferLocalServiceHostAsync(this.TimeoutToken);
			AssertCommonServiceConnections(server.ClientMetadata.Value.SupportedConnections, localServiceHostOffered: true);
		}

		// Verify that the server received the closing notification.
		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);

		// The multiplexing stream should NOT be disposed, since the server was just one channel of that.
		Assert.False(((IDisposableObservable)mx).IsDisposed);
	}

	[Fact]
	public async Task ConnectToServerAsync_Pipe_AndDispose()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new EmptyRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);
			Assumes.True(server.ClientMetadata.HasValue);
			Assert.False(server.ClientMetadata.Value.SupportedConnections.HasFlag(RemoteServiceConnections.Multiplexing));
			AssertCommonServiceConnections(server.ClientMetadata.Value.SupportedConnections, localServiceHostOffered: false);
			await broker.OfferLocalServiceHostAsync(this.TimeoutToken);
			AssertCommonServiceConnections(server.ClientMetadata.Value.SupportedConnections, localServiceHostOffered: true);
		}

		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);

		// Verify that the method completed the duplex pipe we passed in.
		await Task.WhenAll(pair.Item2.Input.WaitForWriterCompletionAsync(), pair.Item2.Output.WaitForReaderCompletionAsync()).WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task ConnectToServerAsync_Pipe_HandshakeFails()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new AlwaysThrowServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		await Assert.ThrowsAsync<StreamJsonRpc.RemoteInvocationException>(() => RemoteServiceBroker.ConnectToServerAsync(pair.Item2, this.TimeoutToken));

		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);

		// Verify that the method completed the duplex pipe we passed in.
		await Task.WhenAll(pair.Item2.Input.WaitForWriterCompletionAsync(), pair.Item2.Output.WaitForReaderCompletionAsync()).WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task ConnectToServerAsync_Proxy_AndDispose()
	{
		var server = new EmptyRemoteServiceBroker();
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(server, this.TimeoutToken))
		{
			Assert.NotNull(server.ClientMetadata);
		}

		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task ConnectToServerAsync_HandshakeFails_AndDispose()
	{
		var server = new AlwaysThrowServiceBroker();
		await Assert.ThrowsAsync<NotImplementedException>(() => RemoteServiceBroker.ConnectToServerAsync(server, this.TimeoutToken));
		await server.Disposed.WaitAsync().WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task GetPipeToRemoteService_NoService()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new EmptyRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);
			System.IO.Pipelines.IDuplexPipe? pipe = await broker.GetPipeAsync(TestServices.DoesNotExist.Moniker, this.TimeoutToken);
			Assert.Null(pipe);
		}
	}

	[Fact]
	public async Task GetProxyToRemoteService_NoService()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new EmptyRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			ICalculator? rpc = await broker.GetProxyAsync<ICalculator>(TestServices.DoesNotExist, this.TimeoutToken);
			Assert.Null(rpc);
		}
	}

	[Fact]
	public async Task GetProxyToRemoteService_BadClrActivationPath()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new LocalActivationRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			ServiceActivationFailedException ex = await Assert.ThrowsAnyAsync<ServiceActivationFailedException>(() => broker.GetProxyAsync<object>(TestServices.DoesNotExist, this.TimeoutToken).AsTask());
			Assert.IsType<FileNotFoundException>(ex.InnerException);
		}
	}

	[Fact]
	public async Task GetPipeToRemoteService_BadClrActivationPath()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new LocalActivationRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			await broker.OfferLocalServiceHostAsync(this.TimeoutToken);
			ServiceActivationFailedException ex = await Assert.ThrowsAnyAsync<ServiceActivationFailedException>(() => broker.GetPipeAsync(TestServices.DoesNotExist.Moniker, this.TimeoutToken).AsTask());
			Assert.IsType<NotSupportedException>(ex.InnerException);
		}
	}

	[Theory]
	[InlineData(RemoteServiceBrokerKinds.LocalActivation)]
	[InlineData(RemoteServiceBrokerKinds.Pipes)]
	[Trait("WindowsOnly", "true")]
	public async Task GetProxyAsync(RemoteServiceBrokerKinds kind)
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		IRemoteServiceBroker server =
			kind == RemoteServiceBrokerKinds.LocalActivation ? (IRemoteServiceBroker)new LocalActivationRemoteServiceBroker() :
			kind == RemoteServiceBrokerKinds.Pipes ? new PipeRemoteServiceBroker() :
			throw new NotSupportedException();

		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			ICalculator? rpc = await broker.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
			Assert.Equal(8, await rpc!.AddAsync(3, 5));
		}
	}

	[Fact]
	public async Task GetProxyAsync_MultiplexingStream()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => new MultiplexingRemoteServiceBroker(mx), this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);
			ICalculator? rpc = await broker.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
			Assumes.NotNull(rpc);
			Assert.Equal(8, await rpc.AddAsync(3, 5));
		}

		await serverTask.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task GetProxyToRemoteService()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new LocalActivationRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			ICalculator? rpc = await broker.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
			Assumes.NotNull(rpc);
			Assert.Equal(8, await rpc.AddAsync(3, 5));
		}
	}

	[Fact]
	public async Task GetProxyAsync_ClientFailureCleansAndThrowsProperly()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		MultiplexingRemoteServiceBroker? testServer = null;
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => testServer = new MultiplexingRemoteServiceBroker(mx), this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);

			// Verify that the client-side exception propagates out to the caller.
			ServiceActivationFailedException actualException = await Assert.ThrowsAsync<ServiceActivationFailedException>(() => broker.GetProxyAsync<IUnsupportedInterface>(TestServices.Calculator, this.TimeoutToken).AsTask());
			Assert.IsType<NotSupportedException>(actualException.InnerException);

			// Ensure that the pipe was properly shut down as well.
			Assumes.NotNull(testServer!.LastIssuedChannel);

			var completed = new AsyncManualResetEvent();
#pragma warning disable CS0618 // Type or member is obsolete
			testServer.LastIssuedChannel.Input.OnWriterCompleted((ex, s) => completed.Set(), null);
#pragma warning restore CS0618 // Type or member is obsolete
			await completed.WaitAsync().WithCancellation(this.TimeoutToken);
		}
	}

	[Fact]
	public async Task GetProxyAsync_SetsActivationOptionsWhenUnset()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new LocalActivationRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			var authService = new MockAuthorizationService(new Dictionary<string, string> { { "c", "d" } });
			broker.SetAuthorizationService(authService);

			await broker.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
			Assert.Equal(CultureInfo.CurrentCulture, server.LastReceivedOptions.ClientCulture);
			Assert.Equal(CultureInfo.CurrentUICulture, server.LastReceivedOptions.ClientUICulture);
			Assert.Equal("d", server.LastReceivedOptions.ClientCredentials!["c"]);

			// Simulate updating the credentials
			authService.UpdateCredentials(new Dictionary<string, string> { { "f", "g" } });

			await broker.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
			Assert.Equal(CultureInfo.CurrentCulture, server.LastReceivedOptions.ClientCulture);
			Assert.Equal(CultureInfo.CurrentUICulture, server.LastReceivedOptions.ClientUICulture);
			Assert.Equal("g", server.LastReceivedOptions.ClientCredentials["f"]);
		}
	}

	[Fact]
	public async Task GetProxyAsync_PreservesPresetActivationOptions()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new LocalActivationRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			var authService = new MockAuthorizationService(new Dictionary<string, string> { { "c", "d" } });
			broker.SetAuthorizationService(authService);
			var options = new ServiceActivationOptions
			{
				ClientCulture = new CultureInfo("es"),
				ClientUICulture = new CultureInfo("es"),
				ClientCredentials = new Dictionary<string, string> { { "a", "b" } },
			};
			ICalculator? rpc = await broker.GetProxyAsync<ICalculator>(TestServices.Calculator, options: options, cancellationToken: this.TimeoutToken);
			Assert.Equal(options.ClientCulture, server.LastReceivedOptions.ClientCulture);
			Assert.Equal(options.ClientUICulture, server.LastReceivedOptions.ClientUICulture);
			Assert.Equal("b", server.LastReceivedOptions.ClientCredentials!["a"]);
			Assert.False(server.LastReceivedOptions.ClientCredentials.ContainsKey("c"));
		}
	}

	[Fact]
	public async Task GetProxyAsync_WithBidirectionalProxies_MissingClientRpcTarget()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => new MultiplexingRemoteServiceBroker(mx), this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);

			// We aren't providing a client RPC target, so the local RemoteServiceBroker should throw since the descriptor requires one.
			await Assert.ThrowsAsync<ArgumentException>(() => broker.GetProxyAsync<ICallMeBack>(TestServices.CallMeBack, this.TimeoutToken).AsTask());
		}

		await serverTask.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task GetProxyAsync_WithBidirectionalProxies()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => new MultiplexingRemoteServiceBroker(mx), this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);
			var clientReceiver = new CallMeBackClient();
			var options = new ServiceActivationOptions { ClientRpcTarget = clientReceiver };
			ICallMeBack? clientRpc = await broker.GetProxyAsync<ICallMeBack>(TestServices.CallMeBack, options, this.TimeoutToken);
			Assumes.NotNull(clientRpc);
			string message = "msg";
			await clientRpc.CallMeBackAsync(message, this.TimeoutToken);
			Assert.Equal(message, clientReceiver.LastMessage);
		}

		await serverTask.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task GetPipeAsync_SetsActivationOptionsWhenUnset()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new LocalActivationRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			var authService = new MockAuthorizationService(new Dictionary<string, string> { { "c", "d" } });
			broker.SetAuthorizationService(authService);

			// GetPipeAsync won't like that our mock doesn't return pipe instructions,
			// but that doesn't matter for what we're asserting.
			await broker.GetPipeAsync(TestServices.Calculator.Moniker, this.TimeoutToken).AsTask().NoThrowAwaitable();
			Assert.Equal(CultureInfo.CurrentCulture, server.LastReceivedOptions.ClientCulture);
			Assert.Equal(CultureInfo.CurrentUICulture, server.LastReceivedOptions.ClientUICulture);
			Assert.Equal("d", server.LastReceivedOptions.ClientCredentials!["c"]);

			// Simulate updating the credentials
			authService.UpdateCredentials(new Dictionary<string, string> { { "f", "g" } });

			// GetPipeAsync won't like that our mock doesn't return pipe instructions,
			// but that doesn't matter for what we're asserting.
			await broker.GetPipeAsync(TestServices.Calculator.Moniker, this.TimeoutToken).AsTask().NoThrowAwaitable();
			Assert.Equal(CultureInfo.CurrentCulture, server.LastReceivedOptions.ClientCulture);
			Assert.Equal(CultureInfo.CurrentUICulture, server.LastReceivedOptions.ClientUICulture);
			Assert.Equal("g", server.LastReceivedOptions.ClientCredentials["f"]);
			Assert.False(server.LastReceivedOptions.ClientCredentials.ContainsKey("c"));
		}
	}

	[Fact]
	public async Task GetPipeAsync_PreservesPresetActivationOptions()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new LocalActivationRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2))
		{
			this.SetupTraceListener(broker);
			var authService = new MockAuthorizationService(new Dictionary<string, string> { { "c", "d" } });
			broker.SetAuthorizationService(authService);
			var options = new ServiceActivationOptions
			{
				ClientCulture = new CultureInfo("es"),
				ClientUICulture = new CultureInfo("es"),
				ClientCredentials = new Dictionary<string, string> { { "a", "b" } },
			};

			// GetPipeAsync won't like that our mock doesn't return pipe instructions,
			// but that doesn't matter for what we're asserting.
			await broker.GetPipeAsync(TestServices.Calculator.Moniker, options, cancellationToken: this.TimeoutToken).AsTask().NoThrowAwaitable();
			Assert.Equal(options.ClientCulture, server.LastReceivedOptions.ClientCulture);
			Assert.Equal(options.ClientUICulture, server.LastReceivedOptions.ClientUICulture);
			Assert.Equal("b", server.LastReceivedOptions.ClientCredentials!["a"]);
			Assert.False(server.LastReceivedOptions.ClientCredentials.ContainsKey("c"));
		}
	}

	[Fact]
	public async Task GetPipeAsync_WithClientRpcProxy_Throws()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => new MultiplexingRemoteServiceBroker(mx), this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.TimeoutToken))
		{
			this.SetupTraceListener(broker);
			var clientReceiver = new CallMeBackClient();
			var options = new ServiceActivationOptions { ClientRpcTarget = clientReceiver };

			// Verify that GetPipeAsync throws if given an RPC client, since the pipe API doesn't set up RPC.
			await Assert.ThrowsAsync<ArgumentException>(() => broker.GetPipeAsync(TestServices.CallMeBack.Moniker, options, this.TimeoutToken).AsTask());
		}

		await serverTask.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task GetMethods_ReturnNullAfterConnectionLost()
	{
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new LocalActivationRemoteServiceBroker();
		FrameworkServices.RemoteServiceBroker.ConstructRpc(server, pair.Item1);
		RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToServerAsync(pair.Item2);
		this.SetupTraceListener(broker);
		await broker.DisposeAsync();

		ICalculator? rpc = await broker.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
		Assert.Null(rpc);

		System.IO.Pipelines.IDuplexPipe? pipe = await broker.GetPipeAsync(TestServices.Calculator.Moniker, this.TimeoutToken);
		Assert.Null(pipe);
	}

	private static void AssertCommonServiceConnections(RemoteServiceConnections remoteServiceConnections, bool localServiceHostOffered)
	{
		RemoteServiceConnections expectedConnections = RemoteServiceConnections.None
			| RemoteServiceConnections.IpcPipe;

		if (localServiceHostOffered)
		{
			expectedConnections |= RemoteServiceConnections.ClrActivation;
		}

		// Disregard the multiplexing flag since that can vary based on connection.
		// Our caller should assert its value separately.
		RemoteServiceConnections actualConnections = remoteServiceConnections & ~RemoteServiceConnections.Multiplexing;

		Assert.Equal(expectedConnections, actualConnections);
	}

	private static async Task<MultiplexingStream> CreateStubMultiplexingStreamAsync(CancellationToken cancellationToken)
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task<MultiplexingStream> mx1 = MultiplexingStream.CreateAsync(pair.Item1, cancellationToken);
		Task<MultiplexingStream> mx2 = MultiplexingStream.CreateAsync(pair.Item2, cancellationToken);
		return await mx1;
	}

	private void SetupTraceListener(RemoteServiceBroker broker)
	{
		Requires.NotNull(broker, nameof(broker));
		broker.TraceSource.Switch.Level = System.Diagnostics.SourceLevels.All;
		broker.TraceSource.Listeners.Add(new XunitTraceListener(this.Logger));
	}

	private class AlwaysThrowServiceBroker : IRemoteServiceBroker, IDisposable
	{
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		internal AsyncManualResetEvent Disposed { get; } = new AsyncManualResetEvent();

		public Task CancelServiceRequestAsync(Guid serviceRequestId)
		{
			throw new NotImplementedException();
		}

		public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken)
		{
			throw new NotImplementedException();
		}

		public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			throw new NotImplementedException();
		}

		public void Dispose()
		{
			Assert.False(this.Disposed.IsSet);
			this.Disposed.Set();
		}

		protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	private class EmptyRemoteServiceBroker : IRemoteServiceBroker, IDisposable
	{
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		internal AsyncManualResetEvent Disposed { get; } = new AsyncManualResetEvent();

		internal ServiceBrokerClientMetadata? ClientMetadata { get; set; }

		public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken)
		{
			this.ClientMetadata = clientMetadata;
			return Task.CompletedTask;
		}

		public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			RemoteServiceConnectionInfo result = default;
			return Task.FromResult(result);
		}

		public Task CancelServiceRequestAsync(Guid serviceRequestId)
		{
			throw new NotImplementedException();
		}

		public void Dispose()
		{
			Assert.False(this.Disposed.IsSet);
			this.Disposed.Set();
		}

		protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	private class LocalActivationRemoteServiceBroker : IRemoteServiceBroker
	{
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		internal ServiceActivationOptions LastReceivedOptions { get; set; }

		public Task HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public Task<RemoteServiceConnectionInfo> RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			this.LastReceivedOptions = options;
			RemoteServiceConnectionInfo result = default;
			if (serviceMoniker.Name == TestServices.Calculator.Moniker.Name)
			{
				result.ClrActivation = new RemoteServiceConnectionInfo.LocalCLRServiceActivation(typeof(Calculator).Assembly.Location, typeof(Calculator).FullName!);
			}
			else if (serviceMoniker.Name == TestServices.DoesNotExist.Moniker.Name)
			{
				result.ClrActivation = new RemoteServiceConnectionInfo.LocalCLRServiceActivation("doesnotexist.dll", "NotHere");
			}

			return Task.FromResult(result);
		}

		public Task CancelServiceRequestAsync(Guid serviceRequestId)
		{
			throw new NotImplementedException();
		}

		protected virtual void OnAvailabilityChanged(BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	private class CallMeBackClient : ICallMeBackClient
	{
		internal string? LastMessage { get; set; }

		public Task YouPhonedAsync(string message, CancellationToken cancellationToken)
		{
			this.LastMessage = message;
			return Task.CompletedTask;
		}
	}
}
