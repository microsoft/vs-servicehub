// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
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
}
