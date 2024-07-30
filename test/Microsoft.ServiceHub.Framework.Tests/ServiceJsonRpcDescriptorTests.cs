// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using StreamJsonRpc;
using Xunit;
using Xunit.Abstractions;

public partial class ServiceJsonRpcDescriptorTests : TestBase
{
	private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("Some name");
	private static readonly ServiceMoniker SomeOtherMoniker = new ServiceMoniker("Some other name");
	private static readonly MultiplexingStream.Options MultiplexingStreamOptions = new MultiplexingStream.Options();
	private static readonly JoinableTaskFactory SomeJoinableTaskFactory = new JoinableTaskContext().Factory;

	public ServiceJsonRpcDescriptorTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	private interface IUnsupportedInterface
	{
		int SomeProperty { get; }
	}

	[Fact]
	public void Ctor_ValidatesInputs()
	{
		Assert.Throws<ArgumentNullException>(() => new ServiceJsonRpcDescriptor(null!, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null));
	}

	[Fact]
	public void Ctor_SetsProperties()
	{
		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null);
		Assert.Same(SomeMoniker, descriptor.Moniker);
		Assert.Equal(ServiceJsonRpcDescriptor.Formatters.MessagePack, descriptor.Formatter);
		Assert.Equal(ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, descriptor.MessageDelimiter);
	}

	[Fact]
	public void Ctor_SetsPropertiesWithoutOptions()
	{
		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, null);
		Assert.Same(SomeMoniker, descriptor.Moniker);
		Assert.Equal(ServiceJsonRpcDescriptor.Formatters.MessagePack, descriptor.Formatter);
		Assert.Equal(ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, descriptor.MessageDelimiter);
		Assert.Null(descriptor.MultiplexingStreamOptions);
	}

	[Fact]
	public void Ctor_SetsPropertiesWithOptions()
	{
		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, MultiplexingStreamOptions);
		Assert.Same(SomeMoniker, descriptor.Moniker);
		Assert.Equal(ServiceJsonRpcDescriptor.Formatters.MessagePack, descriptor.Formatter);
		Assert.Equal(ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, descriptor.MessageDelimiter);

		Assert.NotNull(descriptor.MultiplexingStreamOptions);
		Assert.Equal(MultiplexingStreamOptions.ProtocolMajorVersion, descriptor.MultiplexingStreamOptions?.ProtocolMajorVersion);
		Assert.Equal(MultiplexingStreamOptions.SeededChannels, descriptor.MultiplexingStreamOptions?.SeededChannels);
		Assert.Equal(true, descriptor.MultiplexingStreamOptions?.IsFrozen);
	}

	[Fact]
	public void Ctor_SetsPropertiesWithClone()
	{
		ImmutableArray<Type> additionalServiceInterfaces = [typeof(IDisposable)];
		var descriptor = new TestServiceJsonRpcDescriptor(SomeMoniker, null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, MultiplexingStreamOptions);
		descriptor = (TestServiceJsonRpcDescriptor)descriptor
			.WithExceptionStrategy(descriptor.ExceptionStrategy == ExceptionProcessing.ISerializable ? ExceptionProcessing.CommonErrorData : ExceptionProcessing.ISerializable)
			.WithAdditionalServiceInterfaces(additionalServiceInterfaces);

		TestServiceJsonRpcDescriptor descriptorCopied = descriptor.CopyWithClone();

		Assert.Equal(ServiceJsonRpcDescriptor.Formatters.MessagePack, descriptorCopied.Formatter);
		Assert.Equal(ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, descriptorCopied.MessageDelimiter);

		Assert.NotNull(descriptorCopied.MultiplexingStreamOptions);
		Assert.Equal(MultiplexingStreamOptions.ProtocolMajorVersion, descriptorCopied.MultiplexingStreamOptions?.ProtocolMajorVersion);
		Assert.Equal(MultiplexingStreamOptions.SeededChannels, descriptorCopied.MultiplexingStreamOptions?.SeededChannels);
		Assert.True(descriptorCopied.MultiplexingStreamOptions?.IsFrozen);
		Assert.Equal(descriptor.ExceptionStrategy, descriptorCopied.ExceptionStrategy);
		Assert.Equal(additionalServiceInterfaces, descriptor.AdditionalServiceInterfaces);
	}

	[Fact]
	public void Protocol()
	{
		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null);
		Assert.Equal("json-rpc", descriptor.Protocol);
	}

	[Fact]
	public void CreateDefault_Obsolete()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();
		Assert.Equal(ServiceJsonRpcDescriptor.Formatters.UTF8, descriptor.Formatter);
		Assert.Equal(ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, descriptor.MessageDelimiter);
	}

	[Fact]
	public async Task ConstructRpc_CanReceiveCalls()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		descriptor.ConstructRpc(new Calculator(), pair.Item1.UsePipe());

		ICalculator calc = JsonRpc.Attach<ICalculator>(pair.Item2);
		Assert.Equal(8, await calc.AddAsync(3, 5));
	}

	[Fact]
	public async Task ConstructRpc_DisposesTargetOnDisconnection()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		var calcService = new Calculator();
		descriptor.ConstructRpc(calcService, pair.Item1.UsePipe());
		pair.Item2.Close();

		await calcService.WaitForDisposalAsync(this.TimeoutToken);
	}

	[Fact]
	public async Task ConstructRpc_CanMakeCalls()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();

		// server
		var rpc = JsonRpc.Attach(pair.Item1, new Calculator());

		// client
		ICalculator calc = descriptor.ConstructRpc<ICalculator>(pair.Item2.UsePipe());
		Assert.Equal(8, await calc.AddAsync(3, 5));
	}

	[Fact]
	public async Task ConstructRpc_WithLocalTargetObject()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();

		var rpcServer = JsonRpc.Attach(pair.Item1, new Calculator());
		var clientRpcTarget = new LocalTargetObject();
		ICalculator calc = descriptor.ConstructRpc<ICalculator>(clientRpcTarget, pair.Item2.UsePipe());
		await rpcServer.InvokeWithCancellationAsync(nameof(LocalTargetObject.Callback), cancellationToken: this.TimeoutToken);

		Assert.Equal(1, clientRpcTarget.CallbackInvocations);
	}

	[Fact]
	public async Task ConstructRpc_DisposeClosesConnection()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();
		(Stream, Stream) pair = FullDuplexStream.CreatePair();

		ICalculator calc = descriptor.ConstructRpc<ICalculator>(pair.Item2.UsePipe());
		((IDisposable)calc).Dispose();
		int bytesRead = await pair.Item1.ReadAsync(new byte[1], 0, 1, this.TimeoutToken);
		Assert.Equal(0, bytesRead);
	}

	[Theory]
	[InlineData(ServiceJsonRpcDescriptor.Formatters.UTF8SystemTextJson, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders)]
	[InlineData(ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders)]
	[InlineData(ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader)]
	[InlineData(ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader)]
	public async Task ConstructServer_ConstructClient(ServiceJsonRpcDescriptor.Formatters formatter, ServiceJsonRpcDescriptor.MessageDelimiters delimiter)
	{
		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, formatter, delimiter, multiplexingStreamOptions: null);
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		descriptor.ConstructRpc(new Calculator(), pair.Item1.UsePipe());
		ICalculator calc = descriptor.ConstructRpc<ICalculator>(pair.Item2.UsePipe());
		Assert.Equal(8, await calc.AddAsync(3, 5));
	}

	[Fact]
	public void Equality()
	{
		ServiceJsonRpcDescriptor descriptor1a = CreateDefault();
		ServiceJsonRpcDescriptor descriptor1b = CreateDefault();
		ServiceJsonRpcDescriptor descriptor2a = CreateDefault(SomeOtherMoniker);
		var descriptor3a = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null);
		var descriptor4a = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, multiplexingStreamOptions: null);

		Assert.Equal(descriptor1a, descriptor1b);
		Assert.NotEqual(descriptor1a, descriptor2a);
		Assert.NotEqual(descriptor1a, descriptor3a);
		Assert.NotEqual(descriptor1a, descriptor4a);
	}

	[Fact]
	public void Equals_Null()
	{
		Assert.False(new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null).Equals((object?)null));
		Assert.False(new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null).Equals((ServiceJsonRpcDescriptor?)null));
	}

	[Fact]
	public void GetHashCode_Unique()
	{
		ServiceJsonRpcDescriptor descriptor1a = CreateDefault();
		ServiceJsonRpcDescriptor descriptor1b = CreateDefault();
		ServiceJsonRpcDescriptor descriptor2a = CreateDefault(SomeOtherMoniker);

		Assert.Equal(descriptor1a.GetHashCode(), descriptor1b.GetHashCode());
		Assert.NotEqual(descriptor2a.GetHashCode(), descriptor1a.GetHashCode());
	}

	[Fact]
	public void UnsupportedFormatter()
	{
		var descriptor = new ServiceJsonRpcDescriptor(new ServiceMoniker("name"), clientInterface: null, (ServiceJsonRpcDescriptor.Formatters)(-1), ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		Assert.Throws<NotSupportedException>(() => descriptor.ConstructRpc<ICalculator>(pair.Item1));
	}

	[Fact]
	public void UnsupportedHandler()
	{
		var descriptor = new ServiceJsonRpcDescriptor(new ServiceMoniker("name"), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, (ServiceJsonRpcDescriptor.MessageDelimiters)(-1), multiplexingStreamOptions: null);
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		Assert.Throws<NotSupportedException>(() => descriptor.ConstructRpc<ICalculator>(pair.Item1));
	}

	[Fact]
	public async Task MessageOrderPreserved()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();

		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		var server = new BlockingServer();
		descriptor.ConstructRpc(server, pair.Item1);

		var client = JsonRpc.Attach(pair.Item2.AsStream());

		// Send two requests directly one after the other without awaiting.
		Task invocation1 = client.InvokeAsync(nameof(BlockingServer.SomeMethod), 1);
		Task invocation2 = client.InvokeAsync(nameof(BlockingServer.SomeMethod), 2);

		// Ensure the test has enough time to pass.
		await server.Entered.WaitAsync(this.TimeoutToken);

		// Offer a reasonable amount of time for the second invocation to execute if it could.
		await Task.Delay(AsyncDelay);

		// Verify that the method only executed once, and it was the first one.
		Assert.Equal(1, server.Entrances);
		Assert.Equal(1, server.LastArg);

		// Unblock the first one and verify that the second one also executes.
		server.AllowMethodExit.Set();
		while (server.Entrances == 1 && !this.TimeoutToken.IsCancellationRequested)
		{
			await server.Entered.WaitAsync(this.TimeoutToken);
		}

		Assert.Equal(2, server.Entrances);
		Assert.Equal(2, server.LastArg);
	}

	[Fact]
	public async Task CultureInfoPropertiesSetAndPreserved()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();

		CultureInfo.CurrentCulture = new CultureInfo("en-US");
		CultureInfo.CurrentUICulture = new CultureInfo("en-US");

		var options = new ServiceActivationOptions()
		{
			ClientCulture = new CultureInfo("es"),
			ClientUICulture = new CultureInfo("de"),
		};

		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		ServiceJsonRpcDescriptor.JsonRpcConnection connection;
		using (options.ApplyCultureToCurrentContext())
		{
			connection = (ServiceJsonRpcDescriptor.JsonRpcConnection)descriptor.ConstructRpcConnection(pair.Item1);
			connection.JsonRpc.AddLocalRpcMethod("test", new Func<Task>(async delegate
			{
				Assert.Equal(options.ClientCulture, CultureInfo.CurrentCulture);
				Assert.Equal(options.ClientUICulture, CultureInfo.CurrentUICulture);
				await Task.Yield();
				Assert.Equal(options.ClientCulture, CultureInfo.CurrentCulture);
				Assert.Equal(options.ClientUICulture, CultureInfo.CurrentUICulture);
				await Task.Yield().ConfigureAwait(false);
				Assert.Equal(options.ClientCulture, CultureInfo.CurrentCulture);
				Assert.Equal(options.ClientUICulture, CultureInfo.CurrentUICulture);
			}));
			connection.StartListening();
		}

		var client = JsonRpc.Attach(pair.Item2.AsStream());
		await client.InvokeWithCancellationAsync("test", cancellationToken: this.TimeoutToken);

		Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
		Assert.Equal("en-US", CultureInfo.CurrentUICulture.Name);
	}

	[Fact]
	public void UnsupportedInterface()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		Assert.Throws<NotSupportedException>(() => descriptor.ConstructRpc<IUnsupportedInterface>(pair.Item1));
	}

	[Fact]
	public async Task LargeMessage_OverMultiplexingChannel()
	{
		string longString = new string('a', 1024);

		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => new MultiplexingRelayServiceBroker(new TestServiceBroker(), mx), this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.CreateTestMXStreamOptions(), this.TimeoutToken))
		{
			IEchoService? client = await broker.GetProxyAsync<IEchoService>(TestServices.Echo, this.TimeoutToken);
			Assumes.NotNull(client);

			// Send a 100KB payload, which exceeds the default 64KB limit imposed by a Pipe.
			await client.ListAsync(Enumerable.Range(1, 100).Select(i => longString).ToList(), this.TimeoutToken);
		}
	}

	[Fact]
	public async Task CanSendOutOfBandStreams()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => new MultiplexingRelayServiceBroker(new TestServiceBroker(), mx), this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.CreateTestMXStreamOptions(), this.TimeoutToken))
		{
			IEchoService? client = await broker.GetProxyAsync<IEchoService>(TestServices.Echo.WithTraceSource(this.CreateTestTraceSource("EchoService Proxy")), this.TimeoutToken);
			Assumes.NotNull(client);
			(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();
			byte[] expected = new byte[] { 1, 2, 3 };
			await pipePair.Item1.Output.WriteAsync(expected, this.TimeoutToken);
			byte[] result = await client.ReadAndReturnAsync(pipePair.Item2, this.TimeoutToken);
			Assert.Equal<byte>(expected, result);
		}
	}

	[Fact]
	public void TraceSource_NullByDefault()
	{
		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
		Assert.Null(descriptor.TraceSource);
	}

	[Fact]
	public void WithTraceSource_ImpactsOnlyNewInstance()
	{
		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
		var traceSource = new TraceSource("my tracesource");
		ServiceRpcDescriptor descriptor2 = descriptor.WithTraceSource(traceSource);
		Assert.NotSame(descriptor, descriptor2);
		Assert.Same(traceSource, descriptor2.TraceSource);
		Assert.Null(descriptor.TraceSource);
	}

	[Fact]
	public async Task WithTraceSource_ImpactOnConstructedClient()
	{
		var myListener = new XunitTraceListener(this.Logger);
		var traceSource = new TraceSource("my tracesource")
		{
			Switch = { Level = SourceLevels.All },
			Listeners = { myListener },
		};

		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
		ServiceRpcDescriptor tracingDescriptor = descriptor.WithTraceSource(traceSource);

		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		descriptor.ConstructRpc(new Calculator(), pair.Item1);
		ICalculator calc = tracingDescriptor.ConstructRpc<ICalculator>(pair.Item2);

		Assert.Equal(8, await calc.AddAsync(3, 5));
		Assert.NotEmpty(myListener.TracedMessages);
	}

	[Fact]
	public async Task WithTraceSource_ImpactOnConstructedServer()
	{
		var myListener = new XunitTraceListener(this.Logger);
		var traceSource = new TraceSource("my tracesource")
		{
			Switch = { Level = SourceLevels.All },
			Listeners = { myListener },
		};

		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);
		ServiceRpcDescriptor tracingDescriptor = descriptor.WithTraceSource(traceSource);

		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		tracingDescriptor.ConstructRpc(new Calculator(), pair.Item1);
		ICalculator calc = descriptor.ConstructRpc<ICalculator>(pair.Item2);

		Assert.Equal(8, await calc.AddAsync(3, 5));
		Assert.NotEmpty(myListener.TracedMessages);
	}

	[Fact]
	public void WithJoinableTaskFactory_ImpactsJsonRpcProperty()
	{
		ServiceJsonRpcDescriptor descriptor = new(SomeMoniker, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
		ServiceRpcDescriptor jtfDescriptor = descriptor.WithJoinableTaskFactory(SomeJoinableTaskFactory);

		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();
		descriptor.ConstructRpc(new Calculator(), pair.Item1);
		ICalculator calc = jtfDescriptor.ConstructRpc<ICalculator>(pair.Item2);

		Assert.Same(SomeJoinableTaskFactory, ((IJsonRpcClientProxy)calc).JsonRpc.JoinableTaskFactory);
	}

	[Fact]
	public async Task CamelCasingDoesNotImpactDictionaryKeys()
	{
		ServiceJsonRpcDescriptor descriptor = CreateDefault();
		(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pair = FullDuplexStream.CreatePipePair();

		var echoService = new EchoService(default);
		descriptor
			.WithTraceSource(this.CreateTestTraceSource("Server", SourceLevels.Verbose))
			.ConstructRpc(echoService, pair.Item1);

		IEchoService proxy = descriptor
			.WithTraceSource(this.CreateTestTraceSource("Client", SourceLevels.Verbose))
			.ConstructRpc<IEchoService>(pair.Item2);
		Dictionary<string, string> result = await proxy.DictionaryAsync(
			new Dictionary<string, string>
			{
				{ "SourceId", "hi" },
			},
			this.TimeoutToken);
		Assert.True(result.ContainsKey("SourceId"));
	}

	[Fact]
	public Task WithMultiplexingStream_ReturnsSameType()
	{
		var descriptor = new TestServiceJsonRpcDescriptor(SomeMoniker, null, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader, MultiplexingStreamOptions);
		ServiceRpcDescriptor copyDescriptor = descriptor.WithMultiplexingStream(multiplexingStreamOptions: null);

		Assert.Equal(typeof(TestServiceJsonRpcDescriptor), copyDescriptor.GetType());

		// Ensure they aren't jus the same object and new object was created.
		Assert.False(ReferenceEquals(descriptor, copyDescriptor));

		return Task.CompletedTask;
	}

	[Fact]
	public void WithExceptionStrategy()
	{
		var descriptor = new ServiceJsonRpcDescriptor(SomeMoniker, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);

		// Assert the default value.
		Assert.Equal(ExceptionProcessing.CommonErrorData, descriptor.ExceptionStrategy);

		Assert.Same(descriptor, descriptor.WithExceptionStrategy(ExceptionProcessing.CommonErrorData));

		ServiceJsonRpcDescriptor modified = descriptor.WithExceptionStrategy(ExceptionProcessing.ISerializable);
		Assert.NotSame(descriptor, modified);
		Assert.Equal(ExceptionProcessing.ISerializable, modified.ExceptionStrategy);
		Assert.Equal(ExceptionProcessing.CommonErrorData, descriptor.ExceptionStrategy);
	}

	[Fact]
	public void WithAdditionalServiceInterfaces()
	{
		ImmutableArray<Type> additionalServiceInterfaces2 = [typeof(IDisposable)];
		ImmutableArray<Type> additionalServiceInterfaces3 = [typeof(System.IAsyncDisposable)];
		ServiceJsonRpcDescriptor descriptor = new(SomeMoniker, ServiceJsonRpcDescriptor.Formatters.MessagePack, ServiceJsonRpcDescriptor.MessageDelimiters.BigEndianInt32LengthHeader);
		ServiceJsonRpcDescriptor descriptor2 = descriptor.WithAdditionalServiceInterfaces(additionalServiceInterfaces2);
		Assert.NotSame(descriptor, descriptor2);
		Assert.Equal(additionalServiceInterfaces2, descriptor2.AdditionalServiceInterfaces);

		Assert.Same(descriptor2, descriptor2.WithAdditionalServiceInterfaces(additionalServiceInterfaces2));
		ServiceJsonRpcDescriptor descriptor3 = descriptor2.WithAdditionalServiceInterfaces(additionalServiceInterfaces3);
		Assert.NotSame(descriptor2, descriptor3);
		Assert.Equal(additionalServiceInterfaces3, descriptor3.AdditionalServiceInterfaces);

		Assert.Null(descriptor.WithAdditionalServiceInterfaces(null).AdditionalServiceInterfaces);
	}

	private static ServiceJsonRpcDescriptor CreateDefault(ServiceMoniker? moniker = null) => new ServiceJsonRpcDescriptor(moniker ?? SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);

	private class BlockingServer
	{
		internal AsyncAutoResetEvent Entered { get; } = new AsyncAutoResetEvent();

		internal int Entrances { get; private set; }

		internal ManualResetEventSlim AllowMethodExit { get; } = new ManualResetEventSlim();

		internal int LastArg { get; private set; }

		public void SomeMethod(int arg)
		{
			this.LastArg = arg;
			this.Entrances++;
			this.Entered.Set();
			this.AllowMethodExit.Wait();
		}
	}

	private class LocalTargetObject
	{
		public int CallbackInvocations { get; set; }

		public void Callback() => this.CallbackInvocations++;
	}

	private class TestServiceJsonRpcDescriptor : ServiceJsonRpcDescriptor
	{
		public TestServiceJsonRpcDescriptor(ServiceMoniker serviceMoniker, Type? clientInterface, Formatters formatter, MessageDelimiters messageDelimiter, MultiplexingStream.Options? multiplexingStreamOptions)
			: base(serviceMoniker, clientInterface, formatter, messageDelimiter, multiplexingStreamOptions)
		{
		}

		public TestServiceJsonRpcDescriptor(TestServiceJsonRpcDescriptor copy)
			: base(copy)
		{
		}

		public TestServiceJsonRpcDescriptor CopyWithClone()
		{
			return (TestServiceJsonRpcDescriptor)this.Clone();
		}

		protected override ServiceRpcDescriptor Clone()
		{
			return new TestServiceJsonRpcDescriptor(this);
		}
	}
}
