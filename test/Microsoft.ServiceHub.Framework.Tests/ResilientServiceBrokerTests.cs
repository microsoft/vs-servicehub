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
		Assert.True(first.IsDisposed);

		await resilientProxy.DisposeAsync();
		Assert.True(resilientProxy.IsDisposed);
		Assert.True(second.IsDisposed);
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
		public event EventHandler? Changed;

		internal TaskCompletionSource<int> Notification { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		internal Func<int>? GetGenerationCallback { get; set; }

		internal Action<int>? NotificationCallback { get; set; }

		internal int InvocationCount { get; private set; }

		internal int NotificationCount { get; private set; }

		internal bool IsDisposed => this.DisposeCount > 0;

		internal int DisposeCount => this.SynchronousDisposeCount + this.AsynchronousDisposeCount;

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

		public void Dispose()
		{
			this.SynchronousDisposeCount++;
		}

		public ValueTask DisposeAsync()
		{
			this.AsynchronousDisposeCount++;
			return default;
		}

		internal void RaiseChanged() => this.Changed?.Invoke(this, EventArgs.Empty);
	}
}
