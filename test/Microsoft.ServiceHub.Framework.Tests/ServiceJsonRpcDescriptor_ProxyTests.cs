// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using StreamJsonRpc;

public class ServiceJsonRpcDescriptor_ProxyTests : ServiceRpcDescriptor_ProxyTestBase
{
	public ServiceJsonRpcDescriptor_ProxyTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	internal interface IServerWithVoidMethod : ISomeService
	{
		void NoReturnValue();
	}

	protected override ServiceRpcDescriptor SomeDescriptor { get; } = new ServiceJsonRpcDescriptor(new ServiceMoniker("SomeMoniker"), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);

	[Fact]
	public void CreatePassThroughProxy_ForwardsMethodsWithVoidReturn()
	{
		var target = new SomeService();
		IServerWithVoidMethod proxy = this.CreateProxy<IServerWithVoidMethod>(target);
		Assumes.NotNull(proxy);

		proxy.NoReturnValue();
		Assert.True(target.NoReturnValue_Invoked);
	}

	[Fact]
	public void NullTarget()
	{
		Assert.Null(this.CreateProxy<ISomeService>(null));
	}

	[Fact]
	public async Task TargetIsDisposable()
	{
		var target = new SomeDisposableService();
		ISomeService? proxy = this.CreateProxy<ISomeService>(target);
		Assumes.NotNull(proxy);
		Assert.IsNotType<SomeNonDisposableService>(proxy);
		IDisposable disposableProxy = Assert.IsAssignableFrom<IDisposable>(proxy);
		disposableProxy.Dispose();
		await target.Disposed.WaitAsync(this.TimeoutToken);

		// Verify that after disposal, the proxy acts like an RPC proxy
		await Assert.ThrowsAsync<ObjectDisposedException>(() => proxy.AddAsync(1, 2));
	}

	[Fact]
	public void DisposeOnlyForwardsToTargetOnce()
	{
		var target = new SomeDisposableService();
		ISomeServiceDisposable proxy = this.CreateProxy<ISomeServiceDisposable>(target);
		Assert.Equal(0, target.DisposalCount);
		proxy.Dispose();
		Assert.Equal(1, target.DisposalCount);
		proxy.Dispose();
		Assert.Equal(1, target.DisposalCount);
	}

	[Fact]
	public void INotifyDisposable_HandlerInvokedOnce()
	{
		ISomeServiceDisposable target = new SomeDisposableService();
		ISomeServiceDisposable proxy = this.CreateProxy(target);
		var notifyDisposable = (INotifyDisposable)proxy;

		int disposed = 0;
		EventHandler disposalHandler = (s, e) =>
		{
			Assert.Same(s, notifyDisposable);
			Assert.NotNull(e);
			disposed++;
		};

		// Adding the handler should *not* invoke it before disposal.
		notifyDisposable.Disposed += disposalHandler;
		Assert.Equal(0, disposed);

		// Dispose and assert the handler is invoked exactly once.
		notifyDisposable.Dispose();
		Assert.Equal(1, disposed);

		// Assert that another dispose call does *not* invoke the delegate.
		notifyDisposable.Dispose();
		Assert.Equal(1, disposed);
	}

	[Fact]
	public void INotifyDisposable_HandlerAddedAfterDisposal()
	{
		ISomeServiceDisposable target = new SomeDisposableService();
		ISomeServiceDisposable proxy = this.CreateProxy(target);

		// Run most asserts inside a non-inlineable helper method so we can do a GC test at the end.
		WeakReference delegateWeakRef = Helper((INotifyDisposable)proxy);

		// Assert that the delegate is no longer referenced by the proxy.
		GC.Collect();
		Assert.False(delegateWeakRef.IsAlive);

		[MethodImpl(MethodImplOptions.NoInlining)]
		static WeakReference Helper(INotifyDisposable notifyDisposable)
		{
			int disposed = 0;
			EventHandler disposalHandler = (s, e) =>
			{
				Assert.Same(s, notifyDisposable);
				Assert.NotNull(e);
				disposed++;
			};

			notifyDisposable.Dispose();

			// Assert that a subsequent handler being added gets immediately invoked.
			notifyDisposable.Disposed += disposalHandler;
			Assert.Equal(1, disposed);

			// At this point, no strong references to the handler should exist (aside from our local variable).
			// But .NET rules don't allow us to do a reliable check until this frame is popped off the stack.
			return new WeakReference(disposalHandler);
		}
	}

	[Fact]
	public void INotifyDisposable_DisposalReleasesRef()
	{
		ISomeServiceDisposable target = new SomeDisposableService();
		ISomeServiceDisposable proxy = this.CreateProxy(target);

		// Run most asserts inside a non-inlineable helper method so we can do a GC test at the end.
		WeakReference delegateWeakRef = Helper((INotifyDisposable)proxy);

		// Assert that the delegate is no longer referenced by the proxy.
		GC.Collect();
		Assert.False(delegateWeakRef.IsAlive);

		[MethodImpl(MethodImplOptions.NoInlining)]
		static WeakReference Helper(INotifyDisposable notifyDisposable)
		{
			bool forceUniqueDelegateInstance = true; // force closure so C# doesn't cache the delegate in a static field
			EventHandler disposalHandler = (s, e) =>
			{
				Assert.True(forceUniqueDelegateInstance);
			};

			// Adding the handler should *not* invoke it before disposal.
			notifyDisposable.Disposed += disposalHandler;
			notifyDisposable.Dispose();

			// At this point, no strong references to the handler should exist (aside from our local variable).
			// But .NET rules don't allow us to do a reliable check until this frame is popped off the stack.
			return new WeakReference(disposalHandler);
		}
	}

	[Fact]
	public void INotifyDisposable_RemoveHandler_ReleasesRef()
	{
		ISomeServiceDisposable target = new SomeDisposableService();
		ISomeServiceDisposable proxy = this.CreateProxy(target);

		// Run most asserts inside a non-inlineable helper method so we can do a GC test at the end.
		WeakReference delegateWeakRef = Helper((INotifyDisposable)proxy);

		// Assert that the delegate is no longer referenced by the proxy.
		GC.Collect();
		Assert.False(delegateWeakRef.IsAlive);

		[MethodImpl(MethodImplOptions.NoInlining)]
		static WeakReference Helper(INotifyDisposable notifyDisposable)
		{
			bool forceUniqueDelegateInstance = true; // force closure so C# doesn't cache the delegate in a static field
			EventHandler disposalHandler = (s, e) =>
			{
				Assert.True(forceUniqueDelegateInstance);
				Assumes.NotReachable();
			};

			notifyDisposable.Disposed += disposalHandler;
			notifyDisposable.Disposed -= disposalHandler;

			// At this point, no strong references to the handler should exist (aside from our local variable).
			// But .NET rules don't allow us to do a reliable check until this frame is popped off the stack.
			return new WeakReference(disposalHandler);
		}
	}

	[Fact]
	public async Task TargetImplementsIJsonRpcLocalProxy()
	{
		var target = new SomeNonDisposableService();
		ISomeService? proxy = this.CreateProxy<ISomeService>(target);
		Assumes.NotNull(proxy);
		Assert.IsNotType<SomeNonDisposableService>(proxy);

		var jsonRpcLocalProxy = proxy as IJsonRpcLocalProxy;
		Assert.NotNull(jsonRpcLocalProxy);

		ISomeService2? newProxy = jsonRpcLocalProxy!.ConstructLocalProxy<ISomeService2>();
		Assert.NotNull(newProxy);
		Assert.Equal(await proxy.GetIdentifier(), await newProxy!.GetIdentifier());

		ISomeServiceNotImplemented? nullProxy = jsonRpcLocalProxy!.ConstructLocalProxy<ISomeServiceNotImplemented>();
		Assert.Null(nullProxy);
	}

	/// <summary>
	/// Verifies that local proxies cannot be created over an object that does not implement the required interfaces.
	/// </summary>
	[Fact]
	public void WithAdditionalServiceInterfaces_NonExistingOnTarget()
	{
		ServiceCompositionException ex = Assert.Throws<ServiceCompositionException>(() => this.CreateProxy<ISomeService>(
			new SomeNonDisposableService(),
			this.DescriptorWithAdditionalServiceInterfaces(this.SomeDescriptor, [typeof(IDisposable)])));
		Assert.IsType<InvalidCastException>(ex.InnerException);
		this.Logger.WriteLine(ex.ToString());
	}

	/// <summary>
	/// Verifies that <see cref="ServiceJsonRpcDescriptor"/> uses the source-generated proxy
	/// (a <see cref="Microsoft.ServiceHub.Framework.Reflection.ProxyBase"/>-derived class) when one is registered
	/// for the contract via <see cref="JsonRpcContractAttribute"/>, instead of emitting a dynamic proxy.
	/// </summary>
	[Fact]
	public void UsesSourceGeneratedProxyWhenAvailable()
	{
		ISomeService2? proxy = this.CreateProxy<ISomeService2>(new SomeNonDisposableService());
		Assumes.NotNull(proxy);
		Assert.IsAssignableFrom<Microsoft.ServiceHub.Framework.Reflection.ProxyBase>(proxy);
		this.Logger.WriteLine($"Proxy type: {proxy.GetType().FullName}");
	}

	/// <summary>
	/// Verifies that a grouped source-generated proxy is not used when the target lacks one of the grouped interfaces,
	/// since the source-generated proxy type would otherwise expose interfaces that fail when invoked.
	/// </summary>
	[Fact]
	public void GroupedSourceGeneratedProxyIsRejectedWhenTargetLacksGroupedInterface()
	{
		ISomeService? proxy = this.CreateProxy<ISomeService>(new SomePrimaryOnlyService());
		Assumes.NotNull(proxy);
		Assert.IsNotAssignableFrom<Microsoft.ServiceHub.Framework.Reflection.ProxyBase>(proxy);
		Assert.False(proxy is ISomeService2);
		this.Logger.WriteLine($"Proxy type: {proxy.GetType().FullName}");
	}

	[Fact]
	public void GroupedSourceGeneratedProxyCanBeOptedIntoWhenTargetLacksGroupedInterface()
	{
		ServiceJsonRpcDescriptor descriptor = new ServiceJsonRpcDescriptor(new ServiceMoniker("SomeMoniker"), clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null)
			.WithAcceptProxyWithExtraInterfaces(true);

		ISomeService? proxy = this.CreateProxy<ISomeService>(new SomePrimaryOnlyService(), descriptor);
		Assumes.NotNull(proxy);
		Assert.IsAssignableFrom<Microsoft.ServiceHub.Framework.Reflection.ProxyBase>(proxy);
		Assert.True(proxy is ISomeService2);
		Assert.False(((IClientProxy)proxy).Is(typeof(ISomeService2)));
		this.Logger.WriteLine($"Proxy type: {proxy.GetType().FullName}");
	}

	/// <summary>
	/// Verifies that contracts without <see cref="JsonRpcContractAttribute"/> still get a dynamically-emitted proxy,
	/// since no source-generated proxy class is registered for them.
	/// </summary>
	[Fact]
	public void FallsBackToDynamicProxyForUnannotatedContract()
	{
		IServerWithVoidMethod? proxy = this.CreateProxy<IServerWithVoidMethod>(new SomeService());
		Assumes.NotNull(proxy);
		Assert.IsNotAssignableFrom<Microsoft.ServiceHub.Framework.Reflection.ProxyBase>(proxy);
		this.Logger.WriteLine($"Proxy type: {proxy.GetType().FullName}");
	}

	protected override T? CreateProxy<T>(T? target, ServiceRpcDescriptor descriptor)
		where T : class
	{
		return descriptor.ConstructLocalProxy(target);
	}

	protected override ServiceRpcDescriptor DescriptorWithAdditionalServiceInterfaces(ServiceRpcDescriptor descriptor, ImmutableArray<Type>? additionalServiceInterfaces)
		=> ((ServiceJsonRpcDescriptor)descriptor).WithAdditionalServiceInterfaces(additionalServiceInterfaces);

	protected override ServiceRpcDescriptor DescriptorWithExceptionStrategy(ServiceRpcDescriptor descriptor, ExceptionProcessing strategy)
		=> ((ServiceJsonRpcDescriptor)descriptor).WithExceptionStrategy(strategy);

	protected override string GetDisplayName(ServiceRpcDescriptor descriptor) => throw new NotSupportedException();

	private protected class SomeService : SomeNonDisposableService, IServerWithVoidMethod
	{
		public void NoReturnValue() => this.NoReturnValue_Invoked = true;
	}

	private sealed class SomePrimaryOnlyService : ISomeService
	{
		private readonly Guid identifier = Guid.NewGuid();

		public event EventHandler<PropertyChangedEventArgs>? PropertyChanged;

		public Task<int> AddAsync(int a, int b) => Task.FromResult(a + b);

		public ValueTask<int> AddValueAsync(int a, int b) => new(a + b);

		public Task Throws() => Task.FromException(new InvalidOperationException());

		public Task NoOp(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task GetFaultedTask(bool yieldFirst, CancellationToken cancellationToken) => Task.FromException(new InvalidOperationException());

		public Task<int> GetFaultedTaskOfT(bool yieldFirst, CancellationToken cancellationToken) => Task.FromException<int>(new InvalidOperationException());

		public ValueTask GetFaultedValueTask(bool yieldFirst, CancellationToken cancellationToken) => new(Task.FromException(new InvalidOperationException()));

		public ValueTask<int> GetFaultedValueTaskOfT(bool yieldFirst, CancellationToken cancellationToken) => new(Task.FromException<int>(new InvalidOperationException()));

		public Task<ExternalTestAssembly.InternalType> MethodReturnsInternalTypeFromOtherAssemblyAsync(CancellationToken cancellationToken)
			=> Task.FromException<ExternalTestAssembly.InternalType>(new InvalidOperationException());

		public Task FaultWithErrorCodeAsync(string topLevelMessage, int errorCode, string secondaryMessage, int hresult)
			=> Task.FromException(new InvalidOperationException());

		public Task YieldThenThrow() => Task.FromException(new InvalidOperationException());

		public Task<Guid> GetIdentifier() => Task.FromResult(this.identifier);

		internal void OnPropertyChanged(string propertyName) => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
