// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using PolyType;
using StreamJsonRpc;
using StreamJsonRpc.Protocol;

public abstract partial class ServiceRpcDescriptor_ProxyTestBase : TestBase
{
	public ServiceRpcDescriptor_ProxyTestBase(ITestOutputHelper logger)
		: base(logger)
	{
	}

	[JsonRpcContract]
	[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
	[JsonRpcProxyInterfaceGroup(typeof(ISomeService2))]
	internal partial interface ISomeService
	{
		event EventHandler<PropertyChangedEventArgs> PropertyChanged;

		[JsonRpcContract]
		[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
		public partial interface IPublicInterfaceUnderInternalOne
		{
		}

		Task<int> AddAsync(int a, int b);

		ValueTask<int> AddValueAsync(int a, int b);

		Task Throws();

		Task NoOp(CancellationToken cancellationToken);

		Task GetFaultedTask(bool yieldFirst, CancellationToken cancellationToken);

		Task<int> GetFaultedTaskOfT(bool yieldFirst, CancellationToken cancellationToken);

		ValueTask GetFaultedValueTask(bool yieldFirst, CancellationToken cancellationToken);

		ValueTask<int> GetFaultedValueTaskOfT(bool yieldFirst, CancellationToken cancellationToken);

		Task<ExternalTestAssembly.InternalType> MethodReturnsInternalTypeFromOtherAssemblyAsync(CancellationToken cancellationToken);

		Task FaultWithErrorCodeAsync(string topLevelMessage, int errorCode, string secondaryMessage, int hresult);

		Task YieldThenThrow();

		Task<Guid> GetIdentifier();
	}

	[JsonRpcContract]
	[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
	internal partial interface ISomeServiceDisposable : ISomeService, IDisposable
	{
	}

	[JsonRpcContract]
	[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
	internal partial interface ISomeService2
	{
		event EventHandler<PropertyChangingEventArgs> PropertyChanging;

		ValueTask<int> AddValue2Async(int a, int b);

		Task<Guid> GetIdentifier();
	}

	[JsonRpcContract]
	[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
	internal partial interface ISomeServiceNotImplemented
	{
	}

	protected abstract ServiceRpcDescriptor SomeDescriptor { get; }

	[Fact]
	public async Task TargetNotDisposable()
	{
		ISomeService target = new SomeNonDisposableService();
		ISomeService? proxy = this.CreateProxy(target);
		Assumes.NotNull(proxy);
		Assert.IsNotType<SomeNonDisposableService>(proxy);
		IDisposable disposableProxy = Assert.IsAssignableFrom<IDisposable>(proxy);
		disposableProxy.Dispose();

		// Verify that after disposal, the proxy acts like an RPC proxy
		await Assert.ThrowsAsync<ObjectDisposedException>(() => proxy.AddAsync(1, 2));
	}

	[Fact]
	public void ConcreteTypeArgument()
	{
		// We don't generate proxies for concrete types (classes). Only interfaces. Verify that we throw the right exception.
		Assert.Throws<NotSupportedException>(() => this.CreateProxy(new SomeNonDisposableService()));
	}

	[Fact]
	public async Task ForwardsMethodCalls()
	{
		ISomeService target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy(target);
		Assumes.NotNull(proxy);

		Assert.Equal(await target.AddAsync(1, 2), await proxy.AddAsync(1, 2));
		Assert.Equal(await target.AddValueAsync(1, 2), await proxy.AddValueAsync(1, 2));
	}

	[Fact]
	public async Task MethodsThrowObjectDisposedException()
	{
		ISomeService target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy(target);
		Assumes.NotNull(proxy);
		((IDisposable)proxy).Dispose();

		await Assert.ThrowsAsync<ObjectDisposedException>(() => proxy.AddAsync(1, 2));
	}

	[Fact]
	public async Task CatchesAndRethrows_Direct()
	{
		ISomeService target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy(target);
		Assumes.NotNull(proxy);

		await Assert.ThrowsAsync<RemoteInvocationException>(() => proxy.Throws());
	}

	[Theory]
	[CombinatorialData]
	public async Task CatchesAndRethrows_FaultedTasks(bool yieldFirst, ExceptionProcessing exceptionStrategy)
	{
		ISomeService target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy(target, this.DescriptorWithExceptionStrategy(this.SomeDescriptor, exceptionStrategy));
		Assumes.NotNull(proxy);
		AssertInnerException(await Assert.ThrowsAsync<RemoteInvocationException>(() => proxy.YieldThenThrow()));
		AssertInnerException(await Assert.ThrowsAsync<RemoteInvocationException>(() => proxy.GetFaultedTask(yieldFirst, CancellationToken.None)));
		AssertInnerException(await Assert.ThrowsAsync<RemoteInvocationException>(() => proxy.GetFaultedTaskOfT(yieldFirst, CancellationToken.None)));
		AssertInnerException(await Assert.ThrowsAsync<RemoteInvocationException>(async () => await proxy.GetFaultedValueTask(yieldFirst, CancellationToken.None)));
		AssertInnerException(await Assert.ThrowsAsync<RemoteInvocationException>(async () => await proxy.GetFaultedValueTaskOfT(yieldFirst, CancellationToken.None)));

		void AssertInnerException(RemoteInvocationException exception)
		{
			switch (exceptionStrategy)
			{
				case ExceptionProcessing.CommonErrorData:
					Assert.Null(exception.InnerException);
					break;
				case ExceptionProcessing.ISerializable:
					Assert.IsType<InvalidOperationException>(exception.InnerException);
					Assert.Equal(exception.Message, exception.InnerException!.Message);
					break;
				default: throw new NotSupportedException();
			}
		}
	}

	[Theory]
	[CombinatorialData]
	public async Task CatchesAndRethrows_CanceledTasks(bool yieldFirst)
	{
		ISomeService target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy(target);
		Assumes.NotNull(proxy);

		var canceled = new CancellationToken(canceled: true);
		Assert.Equal(canceled, (await Assert.ThrowsAsync<OperationCanceledException>(() => proxy.GetFaultedTask(yieldFirst, canceled))).CancellationToken);
		Assert.Equal(canceled, (await Assert.ThrowsAsync<OperationCanceledException>(() => proxy.GetFaultedTaskOfT(yieldFirst, canceled))).CancellationToken);
		Assert.Equal(canceled, (await Assert.ThrowsAsync<OperationCanceledException>(async () => await proxy.GetFaultedValueTask(yieldFirst, canceled))).CancellationToken);
		Assert.Equal(canceled, (await Assert.ThrowsAsync<OperationCanceledException>(async () => await proxy.GetFaultedValueTaskOfT(yieldFirst, canceled))).CancellationToken);
	}

	[Fact]
	public async Task PrecanceledRequestNeverInvokesMethod()
	{
		ISomeService target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy(target);
		Assumes.NotNull(proxy);

		var canceled = new CancellationToken(canceled: true);
		await Assert.ThrowsAsync<OperationCanceledException>(() => proxy.NoOp(canceled));
	}

	[Fact]
	public async Task ForwardsEvents()
	{
		var target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy<ISomeService>(target);
		Assumes.NotNull(proxy);

		var lastEvent = new TaskCompletionSource<PropertyChangedEventArgs>();
		EventHandler<PropertyChangedEventArgs> handler = (s, e) => lastEvent.SetResult(e);
		proxy.PropertyChanged += handler;

		target.OnPropertyChanged("expected");
		PropertyChangedEventArgs result = await lastEvent.Task.WithCancellation(this.TimeoutToken);
		Assert.Equal("expected", result.PropertyName);

		lastEvent = new TaskCompletionSource<PropertyChangedEventArgs>();
		proxy.PropertyChanged -= handler;
		target.OnPropertyChanged("notexpected");
		await Assert.ThrowsAsync<OperationCanceledException>(() => lastEvent.Task.WithCancellation(ExpectedTimeoutToken));
	}

	[Fact]
	public void EventsIgnoredAfterDisposal()
	{
		var target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy<ISomeService>(target);
		Assumes.NotNull(proxy);
		((IDisposable)proxy).Dispose();

		bool raised = false;
		EventHandler<PropertyChangedEventArgs> handler = (s, e) => raised = true;
		proxy.PropertyChanged += handler;
		target.OnPropertyChanged("name");
		Assert.False(raised);

		proxy.PropertyChanged -= handler;
	}

	[Fact]
	public async Task CallMethodThatReturnsInternalTypeFromAnotherAssembly()
	{
		ISomeService target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy(target);
		Assumes.NotNull(proxy);
		Assert.NotNull(await proxy.MethodReturnsInternalTypeFromOtherAssemblyAsync(this.TimeoutToken));
	}

	[Fact]
	public void CreateProxy_ForPublicInterfaceNestedUnderInternalInterface()
	{
		this.CreateProxy<ISomeService.IPublicInterfaceUnderInternalOne>(new SomeNonDisposableService());
	}

	[Theory, PairwiseData]
	public async Task ErrorCodeAndDataArePreserved(ExceptionProcessing exceptionStrategy)
	{
		const string msg1 = "msg1";
		const string msg2 = "msg2";
		const int hresult = unchecked((int)0x80000003);
		const int errorCode = 15;

		ISomeService target = new SomeDisposableService();
		ISomeService proxy = this.CreateProxy(target, this.DescriptorWithExceptionStrategy(this.SomeDescriptor, exceptionStrategy));
		Assumes.NotNull(proxy);
		RemoteInvocationException ex = await Assert.ThrowsAsync<RemoteInvocationException>(() => proxy.FaultWithErrorCodeAsync(msg1, errorCode, msg2, hresult));
		Assert.Null(ex.InnerException);
		Assert.Equal(msg1, ex.Message);
		Assert.Equal(errorCode, ex.ErrorCode);

		CommonErrorData deserializedErrorData = Assert.IsType<CommonErrorData>(ex.DeserializedErrorData);
		Assert.Equal(msg2, deserializedErrorData.Message);
		Assert.Equal(hresult, deserializedErrorData.HResult);

		// From this point on, the behavior varies by formatter, which doesn't apply in the local case.
		CommonErrorData? errorDetail =
			ex.ErrorData as CommonErrorData ??
			(ex.ErrorData as JToken)?.ToObject<CommonErrorData>();
		Assumes.NotNull(errorDetail);
		Assert.Equal(msg2, errorDetail.Message);
		Assert.Equal(hresult, errorDetail.HResult);
	}

	[Fact]
	public async Task CreateProxy_WithIDisposableInterface()
	{
		ISomeServiceDisposable target = new SomeDisposableService();
		ISomeServiceDisposable proxy = this.CreateProxy(target);
		Assumes.NotNull(proxy);
		Assert.Equal(await target.AddAsync(1, 2), await proxy.AddAsync(1, 2));
	}

	[Fact]
	public void ProxyImplementsIDisposableObservable()
	{
		ISomeServiceDisposable target = new SomeDisposableService();
		ISomeServiceDisposable proxy = this.CreateProxy(target);
		var observablyDisposableProxy = (IDisposableObservable)proxy;
		Assert.False(observablyDisposableProxy.IsDisposed);
		proxy.Dispose();
		Assert.True(observablyDisposableProxy.IsDisposed);
	}

	[Fact]
	public async Task WithAdditionalServiceInterfaces()
	{
		SomeNonDisposableService service = new();
		ISomeService proxy = this.CreateProxy<ISomeService>(service, this.DescriptorWithAdditionalServiceInterfaces(this.SomeDescriptor, [typeof(ISomeService2)]));

		// Test method calls
		Assert.Equal(3, await proxy.AddAsync(1, 2));

		// Test events.
		TaskCompletionSource<string?> propertyChangedRaised = new();
		proxy.PropertyChanged += (s, e) => propertyChangedRaised.SetResult(e.PropertyName);
		service.OnPropertyChanged("A");
		Assert.Equal("A", await propertyChangedRaised.Task.WithCancellation(this.TimeoutToken));

		// Test method calls on the additional interface.
		ISomeService2 proxy2 = Assert.IsAssignableFrom<ISomeService2>(proxy);
		Assert.Equal(4, await proxy2.AddValue2Async(1, 3));

		// Test events on the additional interface.
		TaskCompletionSource<string?> propertyChangingRaised = new();
		proxy2.PropertyChanging += (s, e) => propertyChangingRaised.SetResult(e.PropertyName);
		service.OnPropertyChanging("b");
		Assert.Equal("b", await propertyChangingRaised.Task.WithCancellation(this.TimeoutToken));
	}

	[Fact]
	public async Task WithAdditionalServiceInterfaces_RequestWithRedundantAddlIface()
	{
		SomeNonDisposableService service = new();
		ISomeService2 proxy = this.CreateProxy<ISomeService2>(service, this.DescriptorWithAdditionalServiceInterfaces(this.SomeDescriptor, [typeof(ISomeService2)]));
		Assert.Equal(4, await proxy.AddValue2Async(1, 3));
	}

	[Fact]
	public async Task WithAdditionalServiceInterfaces_NonUniqueAddlIfaceList()
	{
		SomeNonDisposableService service = new();
		ISomeService proxy = this.CreateProxy<ISomeService>(service, this.DescriptorWithAdditionalServiceInterfaces(this.SomeDescriptor, [typeof(ISomeService2), typeof(ISomeService2)]));
		Assert.Equal(4, await proxy.AddValueAsync(1, 3));
	}

	[Fact]
	public void WithAdditionalServiceInterfaces_MainInterfaceDerivesFromOptional()
	{
		SomeDisposableService service = new();

		// Verify that creating the proxy doesn't throw (due to a failure in generating the proxy type).
		ISomeServiceDisposable proxy = this.CreateProxy<ISomeServiceDisposable>(service, this.DescriptorWithAdditionalServiceInterfaces(this.SomeDescriptor, [typeof(ISomeService)]));
	}

	[return: NotNullIfNotNull("target")]
	protected abstract T? CreateProxy<T>(T? target, ServiceRpcDescriptor descriptor)
		where T : class;

	[return: NotNullIfNotNull("target")]
	protected T? CreateProxy<T>(T? target)
		where T : class => this.CreateProxy(target, this.SomeDescriptor);

	protected abstract ServiceRpcDescriptor DescriptorWithAdditionalServiceInterfaces(ServiceRpcDescriptor descriptor, ImmutableArray<Type>? additionalServiceInterfaces);

	protected abstract ServiceRpcDescriptor DescriptorWithExceptionStrategy(ServiceRpcDescriptor descriptor, ExceptionProcessing strategy);

	private protected class SomeNonDisposableService : ISomeService, ISomeService2, ISomeService.IPublicInterfaceUnderInternalOne
	{
		private Guid identifier = Guid.NewGuid();

		public event EventHandler<PropertyChangedEventArgs>? PropertyChanged;

		public event EventHandler<PropertyChangingEventArgs>? PropertyChanging;

		internal bool NoReturnValue_Invoked { get; set; }

		public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);

		public ValueTask<int> AddValueAsync(int a, int b) => new ValueTask<int>(a + b);

		public ValueTask<int> AddValue2Async(int a, int b) => new ValueTask<int>(a + b);

		// Throws directly (not in an async method) such that the exception is thrown rather than returning a faulted task.
		public Task Throws() => throw new InvalidOperationException();

		public Task NoOp(CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}

		public async Task GetFaultedTask(bool yieldFirst, CancellationToken cancellationToken)
		{
			if (yieldFirst)
			{
				await Task.Yield();
			}

			cancellationToken.ThrowIfCancellationRequested();
			throw new InvalidOperationException();
		}

		public async Task<int> GetFaultedTaskOfT(bool yieldFirst, CancellationToken cancellationToken)
		{
			if (yieldFirst)
			{
				await Task.Yield();
			}

			cancellationToken.ThrowIfCancellationRequested();
			throw new InvalidOperationException();
		}

		public async ValueTask GetFaultedValueTask(bool yieldFirst, CancellationToken cancellationToken)
		{
			if (yieldFirst)
			{
				await Task.Yield();
			}

			cancellationToken.ThrowIfCancellationRequested();
			throw new InvalidOperationException();
		}

		public async ValueTask<int> GetFaultedValueTaskOfT(bool yieldFirst, CancellationToken cancellationToken)
		{
			if (yieldFirst)
			{
				await Task.Yield();
			}

			cancellationToken.ThrowIfCancellationRequested();
			throw new InvalidOperationException();
		}

		public Task<ExternalTestAssembly.InternalType> MethodReturnsInternalTypeFromOtherAssemblyAsync(CancellationToken cancellationToken)
		{
			return Task.FromResult(new ExternalTestAssembly.InternalType());
		}

		public Task FaultWithErrorCodeAsync(string topLevelMessage, int errorCode, string secondaryMessage, int hresult)
		{
			return Task.FromException(new LocalRpcException(topLevelMessage)
			{
				ErrorCode = errorCode,
				ErrorData = new CommonErrorData
				{
					Message = secondaryMessage,
					HResult = hresult,
				},
			});
		}

		public async Task YieldThenThrow()
		{
			await Task.Yield();
			throw new InvalidOperationException();
		}

		public Task<Guid> GetIdentifier()
		{
			return Task.FromResult(this.identifier);
		}

		internal void OnPropertyChanged(string propertyName) => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		internal void OnPropertyChanging(string propertyName) => this.PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
	}

	private protected class SomeDisposableService : SomeNonDisposableService, ISomeServiceDisposable
	{
		internal int DisposalCount { get; set; }

		internal AsyncAutoResetEvent Disposed { get; } = new AsyncAutoResetEvent();

		public void Dispose()
		{
			this.DisposalCount++;
			this.Disposed.Set();
		}
	}
}
