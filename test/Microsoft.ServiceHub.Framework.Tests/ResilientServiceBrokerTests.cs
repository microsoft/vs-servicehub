// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using StreamJsonRpc;

public class ResilientServiceBrokerTests : TestBase
{
	private static readonly ServiceJsonRpcDescriptor Descriptor = new(
		new ServiceMoniker("ResilientService"),
		ServiceJsonRpcDescriptor.Formatters.UTF8,
		ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders);

	public ResilientServiceBrokerTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	[Fact]
	public void Resilient_NullBroker()
	{
		Assert.Throws<ArgumentNullException>(() => ServiceBrokerAggregator.Resilient(null!));
	}

	[Fact]
	public async Task GetProxyAsync_InitiallyUnavailableReturnsNull()
	{
		var innerBroker = new ResilientTestBroker();
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);

		Assert.Null(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
	}

	[Fact]
	public async Task GetProxyAsync_ActivationFailureIsNotMaskedByCleanupFailure()
	{
		var innerBroker = new ResilientTestBroker
		{
			GetProxyCallback = cancellationToken =>
			{
				cancellationToken.Register(() => throw new InvalidOperationException("Cleanup failed."));
				throw new ServiceCompositionException("Activation failed.");
			},
		};
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);

		ServiceCompositionException exception = await Assert.ThrowsAsync<ServiceCompositionException>(
			() => broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken).AsTask());
		Assert.Equal("Activation failed.", exception.Message);
	}

	[Fact]
	public async Task Proxy_RefreshesAndRaisesInvalidated()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
#pragma warning disable ISB001 // Disposed by the test.
		IResilientTestService proxy = Assert.IsAssignableFrom<IResilientTestService>(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		IResilientServiceProxy resilientProxy = Assert.IsAssignableFrom<IResilientServiceProxy>(proxy);
		Assert.IsAssignableFrom<System.IAsyncDisposable>(proxy);
		Assert.True(resilientProxy.IsAvailable);
		Assert.Equal(1, await proxy.GetGenerationAsync(this.TimeoutToken));

		ResilientServiceProxyInvalidatedEventArgs? invalidatedArgs = null;
		resilientProxy.Invalidated += (sender, args) =>
		{
			Assert.Same(proxy, sender);
			invalidatedArgs = args;
		};

		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();

		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.NotNull(invalidatedArgs);
		Assert.Equal(Descriptor.Moniker, invalidatedArgs.ServiceMoniker);
		Assert.Equal(1, invalidatedArgs.Generation);
		Assert.True(resilientProxy.IsAvailable);
		Assert.True(first.IsDisposed);

		await resilientProxy.DisposeAsync();
		Assert.True(resilientProxy.IsDisposed);
		Assert.True(second.IsDisposed);
	}

	[Fact]
	public async Task AvailabilityChanged_ReportsLossAndRestoration()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var delayedRefresh = new TaskCompletionSource<IResilientTestService?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		List<bool> availabilityStates = [];
		proxyLifetime.AvailabilityChanged += (sender, args) =>
		{
			Assert.Same(proxyLifetime, sender);
			availabilityStates.Add(proxyLifetime.IsAvailable);
		};

		innerBroker.GetProxyCallback = cancellationToken => new(delayedRefresh.Task.WithCancellation(cancellationToken));
		innerBroker.RaiseAvailabilityChanged();
		Assert.False(proxyLifetime.IsAvailable);
		Assert.Equal([false], availabilityStates);

		delayedRefresh.SetResult(second);
		Assert.Equal(2, await ((IResilientTestService)proxyLifetime).GetGenerationAsync(this.TimeoutToken));
		Assert.True(proxyLifetime.IsAvailable);
		Assert.Equal([false, true], availabilityStates);
	}

	[Fact]
	public async Task FailedCall_IsNotReplayed()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		first.GetGenerationCallback = () =>
		{
			innerBroker.CurrentService = second;
			innerBroker.RaiseAvailabilityChanged();
			throw new InvalidOperationException("The original call failed.");
		};

		await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.Equal(1, first.InvocationCount);
		Assert.Equal(0, second.InvocationCount);

		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.Equal(1, first.InvocationCount);
		Assert.Equal(1, second.InvocationCount);
	}

	[Fact]
	public async Task ContractEvents_FollowReplacementProxy()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		int eventCount = 0;
		object? lastSender = null;
		proxy.Changed += (sender, args) =>
		{
			eventCount++;
			lastSender = sender;
		};

		first.RaiseChanged();
		Assert.Equal(1, eventCount);
		Assert.Same(proxy, lastSender);

		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
		first.RaiseChanged();
		second.RaiseChanged();

		Assert.Equal(2, eventCount);
		Assert.Same(proxy, lastSender);
	}

	[Fact]
	public async Task AvailabilityChanged_HandlerCanWaitForContractEventRemoval()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		EventHandler handler = (sender, args) => { };
		proxy.Changed += handler;
		proxyLifetime.AvailabilityChanged += (sender, args) =>
		{
			if (!proxyLifetime.IsAvailable)
			{
				Task.Run(() => proxy.Changed -= handler).GetAwaiter().GetResult();
			}
		};

		innerBroker.CurrentService = second;
		await Task.Run(innerBroker.RaiseAvailabilityChanged, TestContext.Current.CancellationToken).WithCancellation(this.TimeoutToken);

		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
	}

	[Fact]
	public async Task ContractEventAddedWhileReplacementIsPublishingIsAttached()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var changedAddStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var completeChangedAdd = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		proxy.Changed += (sender, args) => { };
		second.AddChangedCallback = () =>
		{
			changedAddStarted.TrySetResult(null);
			completeChangedAdd.Task.GetAwaiter().GetResult();
		};

		innerBroker.CurrentService = second;
		Task refresh = Task.Run(innerBroker.RaiseAvailabilityChanged, TestContext.Current.CancellationToken);
		await changedAddStarted.Task.WithCancellation(this.TimeoutToken);
		int eventCount = 0;
		proxy.EarlierChanged += (sender, args) => eventCount++;
		completeChangedAdd.TrySetResult(null);
		await refresh.WithCancellation(this.TimeoutToken);
		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));

		second.RaiseEarlierChanged();
		Assert.Equal(1, eventCount);
	}

	[Fact]
	public async Task ObserverSubscription_FollowsReplacementUntilDisposed()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var third = new ResilientTestService(3);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		var observer = new TestObserver<int>();
		using var subscriptionCancellationSource = new CancellationTokenSource();
		IDisposable subscription = await proxy.ObserveAsync(observer, subscriptionCancellationSource.Token);
		Assert.Equal(1, first.SubscriptionCount);

		first.Publish(10);
		subscriptionCancellationSource.Cancel();
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.Equal(0, first.SubscriptionCount);
		Assert.Equal(1, second.SubscriptionCount);
		second.Publish(20);

		subscription.Dispose();
		Assert.Equal(0, second.SubscriptionCount);
		innerBroker.CurrentService = third;
		innerBroker.RaiseAvailabilityChanged();
		Assert.Equal(3, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.Equal(0, third.SubscriptionCount);
		Assert.Equal([10, 20], observer.Values);
	}

	[Fact]
	public async Task ObserverSubscription_ProxyDisposalCancelsPhysicalSubscription()
	{
		var service = new ResilientTestService(1);
		var innerBroker = new ResilientTestBroker { CurrentService = service };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
#pragma warning disable ISB001 // Disposed by the test.
		IResilientTestService proxy = Assert.IsAssignableFrom<IResilientTestService>(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		IDisposable subscription = await proxy.ObserveAsync(new TestObserver<int>(), this.TimeoutToken);
		Assert.Equal(1, service.SubscriptionCount);

		await ((IResilientServiceProxy)proxy).DisposeAsync();

		Assert.Equal(0, service.SubscriptionCount);
		subscription.Dispose();
	}

	[Fact]
	public async Task ObserverSubscription_QueuesDuringRefreshAndThrowsWhenUnavailable()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var delayedRefresh = new TaskCompletionSource<IResilientTestService?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;

		innerBroker.GetProxyCallback = cancellationToken => new(delayedRefresh.Task.WithCancellation(cancellationToken));
		innerBroker.RaiseAvailabilityChanged();
		Task<IDisposable> pendingSubscription = proxy.ObserveAsync(new TestObserver<int>(), this.TimeoutToken);
		Assert.False(pendingSubscription.IsCompleted);
		Assert.Equal(0, first.SubscriptionCount);

		delayedRefresh.SetResult(second);
		using IDisposable subscription = await pendingSubscription;
		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.Equal(1, second.SubscriptionCount);

		innerBroker.GetProxyCallback = cancellationToken => default;
		innerBroker.RaiseAvailabilityChanged();
		await Assert.ThrowsAsync<ServiceUnavailableException>(() => proxy.GetGenerationAsync(this.TimeoutToken));
		await Assert.ThrowsAsync<ServiceUnavailableException>(() => proxy.ObserveAsync(new TestObserver<int>(), this.TimeoutToken));
	}

	[Fact]
	public async Task ObserverSubscription_CompletingDuringRefreshAttachesToCandidate()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var existingObserver = new TestObserver<int>();
		var lateObserver = new TestObserver<int>();
		var lateInitialStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var completeLateInitial = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var existingReattachStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var completeExistingReattach = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		using IDisposable existingSubscription = await proxy.ObserveAsync(existingObserver, this.TimeoutToken);
		first.ObserveCallback = async (observer, cancellationToken) =>
		{
			lateInitialStarted.TrySetResult(null);
			await completeLateInitial.Task.WithCancellation(cancellationToken);
			return first.CreateSubscription(observer);
		};
		second.ObserveCallback = async (observer, cancellationToken) =>
		{
			if (ReferenceEquals(observer, existingObserver))
			{
				existingReattachStarted.TrySetResult(null);
				await completeExistingReattach.Task.WithCancellation(cancellationToken);
			}

			return second.CreateSubscription(observer);
		};

		Task<IDisposable> lateSubscriptionTask = proxy.ObserveAsync(lateObserver, this.TimeoutToken);
		await lateInitialStarted.Task.WithCancellation(this.TimeoutToken);
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		await existingReattachStarted.Task.WithCancellation(this.TimeoutToken);
		completeLateInitial.TrySetResult(null);
		completeExistingReattach.TrySetResult(null);
		using IDisposable lateSubscription = await lateSubscriptionTask;

		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.Equal(2, second.SubscriptionCount);
	}

	[Fact]
	public async Task ObserverSubscription_FailedReconciliationDoesNotReattach()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var third = new ResilientTestService(3);
		var invocationStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var completeInvocation = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		first.ObserveCallback = async (observer, cancellationToken) =>
		{
			invocationStarted.TrySetResult(null);
			await completeInvocation.Task.WithCancellation(cancellationToken);
			return first.CreateSubscription(observer);
		};
		second.ObserveCallback = (observer, cancellationToken) => Task.FromException<IDisposable>(new InvalidOperationException("Reattachment failed."));
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;

		Task<IDisposable> subscriptionTask = proxy.ObserveAsync(new TestObserver<int>(), this.TimeoutToken);
		await invocationStarted.Task.WithCancellation(this.TimeoutToken);
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
		completeInvocation.TrySetResult(null);
		await Assert.ThrowsAsync<InvalidOperationException>(() => subscriptionTask);

		innerBroker.CurrentService = third;
		innerBroker.RaiseAvailabilityChanged();
		Assert.Equal(3, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.Equal(0, third.SubscriptionCount);
	}

	[Fact]
	public async Task DisposeAsync_CleansUpWhenCancellationCallbackThrows()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var reattachStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		second.ObserveCallback = async (observer, cancellationToken) =>
		{
			CancellationTokenRegistration registration = cancellationToken.Register(() => throw new InvalidOperationException("Cancellation failed."));
			try
			{
				reattachStarted.TrySetResult(null);
				await Task.Delay(Timeout.Infinite, cancellationToken);
				throw new InvalidOperationException();
			}
			finally
			{
				registration.Dispose();
			}
		};
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
#pragma warning disable ISB001 // Disposed by the test.
		IResilientTestService proxy = Assert.IsAssignableFrom<IResilientTestService>(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		using IDisposable subscription = await proxy.ObserveAsync(new TestObserver<int>(), this.TimeoutToken);
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		await reattachStarted.Task.WithCancellation(this.TimeoutToken);

		_ = await Record.ExceptionAsync(() => ((IResilientServiceProxy)proxy).DisposeAsync().AsTask());

		Assert.True(((IResilientServiceProxy)proxy).IsDisposed);
		Assert.True(second.IsDisposed);
		Assert.Equal(0, second.SubscriptionCount);
	}

	[Fact]
	public async Task DisposeAsync_CancelsPendingObserverReattachment()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var reattachStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		second.ObserveCallback = async (observer, cancellationToken) =>
		{
			reattachStarted.TrySetResult(null);
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new InvalidOperationException();
		};
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
#pragma warning disable ISB001 // Disposed by the test.
		IResilientTestService proxy = Assert.IsAssignableFrom<IResilientTestService>(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		using IDisposable subscription = await proxy.ObserveAsync(new TestObserver<int>(), this.TimeoutToken);
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		await reattachStarted.Task.WithCancellation(this.TimeoutToken);

		await ((IResilientServiceProxy)proxy).DisposeAsync().AsTask().WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task SupersededRefresh_CancelsPendingObserverReattachment()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var third = new ResilientTestService(3);
		var reattachStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var thirdReattachCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		second.ObserveCallback = async (observer, cancellationToken) =>
		{
			reattachStarted.TrySetResult(null);
			await Task.Delay(Timeout.Infinite, cancellationToken);
			throw new InvalidOperationException();
		};
		third.ObserveCallback = (observer, cancellationToken) =>
		{
			thirdReattachCancellation.TrySetResult(cancellationToken.IsCancellationRequested);
			return Task.FromResult(third.CreateSubscription(observer));
		};
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		using IDisposable subscription = await proxy.ObserveAsync(new TestObserver<int>(), this.TimeoutToken);

		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		await reattachStarted.Task.WithCancellation(this.TimeoutToken);
		innerBroker.CurrentService = third;
		innerBroker.RaiseAvailabilityChanged();

		Assert.Equal(3, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.False(await thirdReattachCancellation.Task.WithCancellation(this.TimeoutToken));
		Assert.Equal(1, third.SubscriptionCount);
		await second.Disposal.WithCancellation(this.TimeoutToken);
		Assert.True(second.IsDisposed);
		Assert.Equal(1, third.SubscriptionCount);
	}

	[Fact]
	public async Task SupersededRefresh_SharedProxyPreservesLatestSubscription()
	{
		var first = new ResilientTestService(1);
		var replacement = new ResilientTestService(2);
		var firstReattachStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		int reattachCount = 0;
		replacement.ObserveCallback = async (observer, cancellationToken) =>
		{
			if (Interlocked.Increment(ref reattachCount) == 1)
			{
				firstReattachStarted.TrySetResult(null);
				await Task.Delay(Timeout.Infinite, cancellationToken);
				throw new InvalidOperationException();
			}

			return replacement.CreateSubscription(observer);
		};
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		using IDisposable subscription = await proxy.ObserveAsync(new TestObserver<int>(), this.TimeoutToken);

		innerBroker.CurrentService = replacement;
		innerBroker.RaiseAvailabilityChanged();
		Task<int> pendingCall = proxy.GetGenerationAsync(this.TimeoutToken);
		await firstReattachStarted.Task.WithCancellation(this.TimeoutToken);
		innerBroker.RaiseAvailabilityChanged();

		Assert.Equal(2, await pendingCall);
		Assert.Equal(2, reattachCount);
		Assert.False(replacement.IsDisposed);
		Assert.Equal(1, replacement.SubscriptionCount);
	}

	[Fact]
	public async Task Notification_WaitsForReplacementProxy()
	{
		var first = new ResilientTestService(1);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;

		innerBroker.CurrentService = null;
		innerBroker.RaiseAvailabilityChanged();
		proxy.Notify(42);
		Assert.False(first.Notification.Task.IsCompleted);

		var second = new ResilientTestService(2);
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();

		Assert.Equal(42, await second.Notification.Task.WithCancellation(this.TimeoutToken));
		Assert.False(first.Notification.Task.IsCompleted);
	}

	[Fact]
	public async Task SuccessfulCall_WakesNotificationWaitingForReplacement()
	{
		var first = new ResilientTestService(1);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;

		innerBroker.CurrentService = null;
		innerBroker.RaiseAvailabilityChanged();
		proxy.Notify(42);
		await Assert.ThrowsAsync<ServiceUnavailableException>(() => proxy.GetGenerationAsync(this.TimeoutToken));

		var second = new ResilientTestService(2);
		innerBroker.CurrentService = second;
		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
		Assert.Equal(42, await second.Notification.Task.WithCancellation(this.TimeoutToken));
	}

	[Fact]
	public async Task Call_ThrowsWhileTemporarilyUnavailableAndThenRecovers()
	{
		var first = new ResilientTestService(1);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;

		innerBroker.CurrentService = null;
		innerBroker.RaiseAvailabilityChanged();
		await Assert.ThrowsAsync<ServiceUnavailableException>(() => proxy.GetGenerationAsync(this.TimeoutToken));

		innerBroker.CurrentService = new ResilientTestService(2);
		innerBroker.RaiseAvailabilityChanged();
		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
	}

	[Fact]
	public async Task AsyncEnumeration_HoldsGenerationLease()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;
		await using IAsyncEnumerator<int> enumerator = proxy.StreamAsync(this.TimeoutToken).GetAsyncEnumerator(this.TimeoutToken);

		Assert.True(await enumerator.MoveNextAsync());
		Assert.Equal(1, enumerator.Current);
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		Assert.False(first.IsDisposed);

		await enumerator.DisposeAsync();
		Assert.True(first.IsDisposed);
		Assert.Equal(2, await proxy.GetGenerationAsync(this.TimeoutToken));
	}

	[Fact]
	public async Task AdditionalInterfaceAndActivationOptions_SurviveRefresh()
	{
		var first = new ResilientTestService(1);
		var second = new ResilientTestService(2);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		ServiceActivationOptions options = new() { ActivationArguments = new Dictionary<string, string> { ["key"] = "value" } };
		ServiceJsonRpcDescriptor descriptor = Descriptor.WithAcceptProxyWithExtraInterfaces(true);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestServiceWithMetadata>(descriptor, options, this.TimeoutToken));
#pragma warning restore ISB001
		IResilientTestServiceMetadata metadata = Assert.IsAssignableFrom<IResilientTestServiceMetadata>(proxyLifetime);

		Assert.Equal("Service 1", await metadata.GetNameAsync(this.TimeoutToken));
		Assert.Same(options.ActivationArguments, innerBroker.LastOptions.ActivationArguments);
		Assert.Contains(typeof(IResilientTestServiceMetadata), Assert.IsType<ServiceJsonRpcDescriptor>(innerBroker.LastDescriptor).AdditionalServiceInterfaces ?? []);
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		Assert.Equal("Service 2", await metadata.GetNameAsync(this.TimeoutToken));
		Assert.Same(options.ActivationArguments, innerBroker.LastOptions.ActivationArguments);
	}

	[Fact]
	public async Task ContractAsyncDisposal_DisposesStableProxy()
	{
		var service = new ResilientTestService(1);
		var innerBroker = new ResilientTestBroker { CurrentService = service };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
#pragma warning disable ISB001 // Disposed by the test.
		IResilientTestService proxy = Assert.IsAssignableFrom<IResilientTestService>(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001

		await proxy.DisposeAsync();

		Assert.True(((IResilientServiceProxy)proxy).IsDisposed);
		Assert.Equal(1, service.AsynchronousDisposeCount);
	}

	[Fact]
	public async Task Notification_ServiceUnavailableFailure_IsNotRetried()
	{
		var service = new ResilientTestService(1)
		{
			NotificationCallback = value =>
			{
				if (value == 1)
				{
					throw new ServiceUnavailableException("The notification failed.");
				}
			},
		};
		var innerBroker = new ResilientTestBroker { CurrentService = service };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;

		proxy.Notify(1);
		proxy.Notify(2);

		Assert.Equal(2, await service.Notification.Task.WithCancellation(this.TimeoutToken));
		Assert.Equal(2, service.NotificationCount);
	}

	[Fact]
	public async Task DisposeAsync_CancelsNotificationsWaitingForReplacement()
	{
		var first = new ResilientTestService(1);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
#pragma warning disable ISB001 // Disposed by the test.
		IResilientTestService proxy = Assert.IsAssignableFrom<IResilientTestService>(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var lifetime = (IResilientServiceProxy)proxy;

		innerBroker.CurrentService = null;
		innerBroker.RaiseAvailabilityChanged();
		proxy.Notify(42);
		await lifetime.DisposeAsync();

		Assert.True(lifetime.IsDisposed);
		Assert.Equal(1, first.DisposeCount);
		var second = new ResilientTestService(2);
		innerBroker.CurrentService = second;
		innerBroker.RaiseAvailabilityChanged();
		Assert.False(second.Notification.Task.IsCompleted);
	}

	[Fact]
	public async Task DisposeAsync_PrefersAsyncInnerDisposal()
	{
		var service = new ResilientTestService(1);
		var innerBroker = new ResilientTestBroker { CurrentService = service };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
#pragma warning disable ISB001 // Disposed by the test.
		IResilientServiceProxy proxy = Assert.IsAssignableFrom<IResilientServiceProxy>(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001

		await proxy.DisposeAsync();

		Assert.Equal(0, service.SynchronousDisposeCount);
		Assert.Equal(1, service.AsynchronousDisposeCount);
	}

	[Fact]
	public async Task DisposeAsync_AwaitsAndCleansUpOutstandingRefresh()
	{
		var first = new ResilientTestService(1);
		var replacement = new ResilientTestService(2);
		var delayedRefresh = new TaskCompletionSource<IResilientTestService?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
#pragma warning disable ISB001 // Disposed by the test.
		IResilientServiceProxy proxy = Assert.IsAssignableFrom<IResilientServiceProxy>(await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001

		innerBroker.GetProxyCallback = cancellationToken => new(delayedRefresh.Task);
		innerBroker.RaiseAvailabilityChanged();
		await innerBroker.SecondRequestStarted.Task.WithCancellation(this.TimeoutToken);
		ValueTask disposal = proxy.DisposeAsync();
		Assert.False(disposal.IsCompleted);

		delayedRefresh.SetResult(replacement);
		await disposal;
		Assert.True(replacement.IsDisposed);
	}

	[Fact]
	public async Task SupersededRefresh_CannotInstallStaleProxy()
	{
		var first = new ResilientTestService(1);
		var stale = new ResilientTestService(2);
		var latest = new ResilientTestService(3);
		var delayedRefresh = new TaskCompletionSource<IResilientTestService?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var innerBroker = new ResilientTestBroker { CurrentService = first };
		IServiceBroker broker = ServiceBrokerAggregator.Resilient(innerBroker);
		using IResilientServiceProxy proxyLifetime = Assert.IsAssignableFrom<IResilientServiceProxy>(
#pragma warning disable ISB001 // Disposed by the using statement.
			await broker.GetProxyAsync<IResilientTestService>(Descriptor, this.TimeoutToken));
#pragma warning restore ISB001
		var proxy = (IResilientTestService)proxyLifetime;

		innerBroker.GetProxyCallback = cancellationToken => new(delayedRefresh.Task.WithCancellation(cancellationToken));
		innerBroker.RaiseAvailabilityChanged();
		Task<int> pendingCall = proxy.GetGenerationAsync(this.TimeoutToken);
		await innerBroker.SecondRequestStarted.Task.WithCancellation(this.TimeoutToken);

		innerBroker.GetProxyCallback = cancellationToken => new(latest);
		innerBroker.RaiseAvailabilityChanged();
		delayedRefresh.SetResult(stale);

		Assert.Equal(3, await pendingCall);
		Assert.True(stale.IsDisposed);
		Assert.Equal(3, await proxy.GetGenerationAsync(this.TimeoutToken));
	}

	private sealed class ResilientTestBroker : IServiceBroker
	{
		private int requestCount;

		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		internal IResilientTestService? CurrentService { get; set; }

		internal Func<CancellationToken, ValueTask<IResilientTestService?>>? GetProxyCallback { get; set; }

		internal ServiceActivationOptions LastOptions { get; private set; }

		internal ServiceRpcDescriptor? LastDescriptor { get; private set; }

		internal TaskCompletionSource<object?> SecondRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			=> default;

		public async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			this.LastOptions = options;
			this.LastDescriptor = serviceDescriptor;
			if (Interlocked.Increment(ref this.requestCount) >= 2)
			{
				this.SecondRequestStarted.TrySetResult(null);
			}

			IResilientTestService? service = this.GetProxyCallback is null
				? this.CurrentService
				: await this.GetProxyCallback(cancellationToken);
			return (T?)(object?)service;
		}

		internal void RaiseAvailabilityChanged()
			=> this.AvailabilityChanged?.Invoke(this, new BrokeredServicesChangedEventArgs(ImmutableHashSet.Create(Descriptor.Moniker)));
	}

	private sealed class ResilientTestService(int generation) : IResilientTestServiceWithMetadata, IResilientTestServiceMetadata, IDisposable, System.IAsyncDisposable
	{
		private readonly List<IObserver<int>> observers = [];
		private readonly TaskCompletionSource<object?> disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private EventHandler? changed;
		private EventHandler? earlierChanged;

		public event EventHandler? Changed
		{
			add
			{
				this.AddChangedCallback?.Invoke();
				this.changed += value;
			}

			remove => this.changed -= value;
		}

		public event EventHandler? EarlierChanged
		{
			add => this.earlierChanged += value;
			remove => this.earlierChanged -= value;
		}

		internal TaskCompletionSource<int> Notification { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Action? AddChangedCallback { get; set; }

		internal Func<int>? GetGenerationCallback { get; set; }

		internal Action<int>? NotificationCallback { get; set; }

		internal Func<IObserver<int>, CancellationToken, Task<IDisposable>>? ObserveCallback { get; set; }

		internal int InvocationCount { get; private set; }

		internal int NotificationCount { get; private set; }

		internal int SubscriptionCount
		{
			get
			{
				lock (this.observers)
				{
					return this.observers.Count;
				}
			}
		}

		internal bool IsDisposed => this.DisposeCount > 0;

		internal int DisposeCount => this.SynchronousDisposeCount + this.AsynchronousDisposeCount;

		internal Task Disposal => this.disposal.Task;

		internal int SynchronousDisposeCount { get; private set; }

		internal int AsynchronousDisposeCount { get; private set; }

		public Task<int> GetGenerationAsync(CancellationToken cancellationToken)
		{
			this.InvocationCount++;
			return Task.FromResult(this.GetGenerationCallback?.Invoke() ?? generation);
		}

		public Task<string> GetNameAsync(CancellationToken cancellationToken)
			=> Task.FromResult($"Service {generation}");

		public async IAsyncEnumerable<int> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.Yield();
			yield return generation;
			await Task.Delay(Timeout.Infinite, cancellationToken);
		}

		public void Notify(int value)
		{
			this.NotificationCount++;
			this.NotificationCallback?.Invoke(value);
			this.Notification.TrySetResult(value);
		}

		public Task<IDisposable> ObserveAsync(IObserver<int> observer, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return this.ObserveCallback?.Invoke(observer, cancellationToken) ?? Task.FromResult(this.CreateSubscription(observer));
		}

		public void Dispose()
		{
			this.SynchronousDisposeCount++;
			this.disposal.TrySetResult(null);
		}

		public ValueTask DisposeAsync()
		{
			this.AsynchronousDisposeCount++;
			this.disposal.TrySetResult(null);
			return default;
		}

		internal IDisposable CreateSubscription(IObserver<int> observer)
		{
			lock (this.observers)
			{
				this.observers.Add(observer);
			}

			return new ActionDisposable(() =>
			{
				lock (this.observers)
				{
					this.observers.Remove(observer);
				}
			});
		}

		internal void RaiseChanged() => this.changed?.Invoke(this, EventArgs.Empty);

		internal void RaiseEarlierChanged() => this.earlierChanged?.Invoke(this, EventArgs.Empty);

		internal void Publish(int value)
		{
			IObserver<int>[] snapshot;
			lock (this.observers)
			{
				snapshot = [.. this.observers];
			}

			foreach (IObserver<int> observer in snapshot)
			{
				observer.OnNext(value);
			}
		}
	}

	private sealed class ActionDisposable(Action action) : IDisposable
	{
		private Action? action = action;

		public void Dispose() => Interlocked.Exchange(ref this.action, null)?.Invoke();
	}

	private sealed class TestObserver<T> : IObserver<T>
	{
		internal List<T> Values { get; } = [];

		public void OnCompleted()
		{
		}

		public void OnError(Exception error)
		{
		}

		public void OnNext(T value) => this.Values.Add(value);
	}
}
