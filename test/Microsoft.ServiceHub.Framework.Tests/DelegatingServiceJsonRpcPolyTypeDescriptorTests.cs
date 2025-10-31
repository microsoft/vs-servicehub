// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using StreamJsonRpc;

public partial class DelegatingServiceJsonRpcPolyTypeDescriptorTestsCopy : TestBase
{
	private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("Some name");
	private static readonly ServiceMoniker SomeOtherMoniker = new ServiceMoniker("Some other name");
	private static readonly MultiplexingStream.Options MultiplexingStreamOptions = new MultiplexingStream.Options();

	private static readonly ServiceRpcDescriptor EchoDescriptor = new TestServiceJsonRpcDescriptor(new ServiceMoniker("Echo"), ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
	private static readonly JoinableTaskFactory SomeJoinableTaskFactory = new JoinableTaskContext().Factory;

	public DelegatingServiceJsonRpcPolyTypeDescriptorTestsCopy(ITestOutputHelper logger)
		: base(logger)
	{
	}

	[Fact]
	public async Task CallsAreDelegatedAsync()
	{
		TestServiceJsonRpcDescriptor innerDescriptor = new TestServiceJsonRpcDescriptor(SomeMoniker, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
		ServiceJsonRpcDescriptor descriptor = new MyDelegatingDescriptor(innerDescriptor);
		(Stream, Stream) pair = FullDuplexStream.CreatePair();

		// server
		var rpc = JsonRpc.Attach(pair.Item1, new Calculator());

		// client
		ICalculator calc = descriptor.ConstructRpc<ICalculator>(pair.Item2.UsePipe(cancellationToken: this.TimeoutToken));
		Assert.Equal(8, await calc.AddAsync(3, 5));

		Assert.Contains("CreateConnection", innerDescriptor.MethodCalls);
		Assert.Contains("CreateFormatter", innerDescriptor.MethodCalls);
		Assert.Contains("CreateHandler", innerDescriptor.MethodCalls);
		Assert.Contains("CreateJsonRpc", innerDescriptor.MethodCalls);
	}

	[Fact]
	public void DescriptorChangesAreAppliedToInnerDescriptor()
	{
		TestServiceJsonRpcDescriptor? createFormatterCalledFrom = null;

		TestServiceJsonRpcDescriptor innerDescriptor = new TestServiceJsonRpcDescriptor(SomeMoniker, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
		innerDescriptor.OnCreateFormatter += (s, e) =>
		{
			createFormatterCalledFrom = (TestServiceJsonRpcDescriptor)s!;
		};

		innerDescriptor = (TestServiceJsonRpcDescriptor)innerDescriptor.WithExceptionStrategy(ExceptionProcessing.ISerializable);

		TraceSource traceSource = new TraceSource(nameof(this.DescriptorChangesAreAppliedToInnerDescriptor));

		ServiceRpcDescriptor descriptor = new MyDelegatingDescriptor(innerDescriptor)
			.WithExceptionStrategy(ExceptionProcessing.CommonErrorData)
			.WithJoinableTaskFactory(SomeJoinableTaskFactory)
			.WithTraceSource(traceSource);

		(Stream, Stream) pair = FullDuplexStream.CreatePair();

		var jsonRpcConnection = descriptor.ConstructRpcConnection(pair.Item2.UsePipe(cancellationToken: this.TimeoutToken)) as ServiceJsonRpcDescriptor.JsonRpcConnection;
		Assumes.NotNull(jsonRpcConnection);

		JsonRpc jsonRpc = jsonRpcConnection.JsonRpc;

		Assert.Equal(ExceptionProcessing.CommonErrorData, jsonRpc.ExceptionStrategy);
		Assert.Equal(SomeJoinableTaskFactory, jsonRpc.JoinableTaskFactory);
		Assert.Equal(traceSource, jsonRpc.TraceSource);
		Assert.NotNull(createFormatterCalledFrom);
		Assert.NotSame(createFormatterCalledFrom, innerDescriptor);
	}

	[Fact]
	public void DerivedImplementationIsCalledWhenOverrideIsPresent()
	{
		bool createFormatterCalled = false;
		bool createJsonRpcCalled = false;

		TestServiceJsonRpcDescriptor innerDescriptor = new TestServiceJsonRpcDescriptor(SomeMoniker, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);
		innerDescriptor.OnCreateFormatter += (s, e) =>
		{
			createFormatterCalled = true;
		};

		DelegatingDescriptorWithOverride descriptor = new DelegatingDescriptorWithOverride(innerDescriptor);
		descriptor.OnCreateJsonRpcCalled += (s, e) =>
		{
			createJsonRpcCalled = true;
		};

		(Stream, Stream) pair = FullDuplexStream.CreatePair();

		var jsonRpcConnection = descriptor.ConstructRpcConnection(pair.Item2.UsePipe(cancellationToken: this.TimeoutToken)) as ServiceJsonRpcDescriptor.JsonRpcConnection;
		Assumes.NotNull(jsonRpcConnection);
		Assert.True(createFormatterCalled);
		Assert.True(createJsonRpcCalled);
	}

	/// <summary>
	/// This test ensures that multiplexing stream changes are applied correctly.
	/// </summary>
	[Fact]
	public async Task CanSendOutOfBandStreams()
	{
		(Stream, Stream) pair = FullDuplexStream.CreatePair();
		Task serverTask = this.HostMultiplexingServerAsync(pair.Item1, mx => new MultiplexingRelayServiceBroker(new TestServiceBroker(), mx), this.TimeoutToken);
		using (RemoteServiceBroker broker = await RemoteServiceBroker.ConnectToMultiplexingServerAsync(pair.Item2, this.CreateTestMXStreamOptions(), this.TimeoutToken))
		{
			var innerDescriptor = (ServiceJsonRpcDescriptor)EchoDescriptor.WithTraceSource(this.CreateTestTraceSource("EchoService Proxy"));
			var descriptor = new MyDelegatingDescriptor(innerDescriptor);

			IEchoService? client = await broker.GetProxyAsync<IEchoService>(descriptor, this.TimeoutToken);
			Assumes.NotNull(client);
			(System.IO.Pipelines.IDuplexPipe, System.IO.Pipelines.IDuplexPipe) pipePair = FullDuplexStream.CreatePipePair();
			byte[] expected = new byte[] { 1, 2, 3 };
			await pipePair.Item1.Output.WriteAsync(expected, this.TimeoutToken);
			byte[] result = await client.ReadAndReturnAsync(pipePair.Item2, this.TimeoutToken);
			Assert.Equal<byte>(expected, result);
		}
	}

	private class TestServiceJsonRpcDescriptor : ServiceJsonRpcDescriptor
	{
		public EventHandler<EventArgs>? OnCreateFormatter;

		public TestServiceJsonRpcDescriptor(ServiceMoniker serviceMoniker, Formatters formatter, MessageDelimiters messageDelimiter)
			: base(serviceMoniker, formatter, messageDelimiter)
		{
		}

		private TestServiceJsonRpcDescriptor(TestServiceJsonRpcDescriptor copy)
			: base(copy)
		{
			this.OnCreateFormatter = copy.OnCreateFormatter;
		}

		public HashSet<string> MethodCalls { get; } = new HashSet<string>();

		protected override ServiceRpcDescriptor Clone()
		{
			return new TestServiceJsonRpcDescriptor(this);
		}

		protected override JsonRpcConnection CreateConnection(JsonRpc jsonRpc)
		{
			this.MethodCalls.Add(nameof(this.CreateConnection));
			return base.CreateConnection(jsonRpc);
		}

		protected override IJsonRpcMessageFormatter CreateFormatter()
		{
			this.MethodCalls.Add(nameof(this.CreateFormatter));
			this.OnCreateFormatter?.Invoke(this, EventArgs.Empty);
			return base.CreateFormatter();
		}

		protected override IJsonRpcMessageHandler CreateHandler(IDuplexPipe pipe, IJsonRpcMessageFormatter formatter)
		{
			this.MethodCalls.Add(nameof(this.CreateHandler));
			return base.CreateHandler(pipe, formatter);
		}

		protected override JsonRpc CreateJsonRpc(IJsonRpcMessageHandler handler)
		{
			this.MethodCalls.Add(nameof(this.CreateJsonRpc));
			return base.CreateJsonRpc(handler);
		}
	}

	private class MyDelegatingDescriptor : DelegatingServiceJsonRpcDescriptor
	{
		public MyDelegatingDescriptor(ServiceJsonRpcDescriptor innerDescriptor)
			: base(innerDescriptor)
		{
		}

		private MyDelegatingDescriptor(MyDelegatingDescriptor copyFrom)
			: base(copyFrom)
		{
		}

		protected override ServiceRpcDescriptor Clone()
		{
			return new MyDelegatingDescriptor(this);
		}
	}

	private class DelegatingDescriptorWithOverride : DelegatingServiceJsonRpcDescriptor
	{
		public EventHandler<EventArgs>? OnCreateJsonRpcCalled;

		public DelegatingDescriptorWithOverride(ServiceJsonRpcDescriptor innerDescriptor)
			: base(innerDescriptor)
		{
		}

		private DelegatingDescriptorWithOverride(DelegatingDescriptorWithOverride copyFrom)
			: base(copyFrom)
		{
			this.OnCreateJsonRpcCalled = copyFrom.OnCreateJsonRpcCalled;
		}

		protected override JsonRpc CreateJsonRpc(IJsonRpcMessageHandler handler)
		{
			this.OnCreateJsonRpcCalled?.Invoke(this, EventArgs.Empty);
			return new JsonRpc(handler);
		}

		protected override ServiceRpcDescriptor Clone()
		{
			return new DelegatingDescriptorWithOverride(this);
		}
	}
}
