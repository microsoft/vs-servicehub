// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;

public class ServiceBrokerClientTests : TestBase
{
	private static readonly ServiceMoniker SomeMoniker = new ServiceMoniker("SomeName");
	private static readonly ServiceRpcDescriptor SomeRpcDescriptor = new ServiceJsonRpcDescriptor(SomeMoniker, clientInterface: null, ServiceJsonRpcDescriptor.Formatters.UTF8, ServiceJsonRpcDescriptor.MessageDelimiters.HttpLikeHeaders, multiplexingStreamOptions: null);

	private ServiceBrokerClient client;

	private TestServiceBroker serviceBroker;

	public ServiceBrokerClientTests(ITestOutputHelper logger)
		: base(logger)
	{
		this.serviceBroker = new TestServiceBroker();
		this.client = new ServiceBrokerClient(this.serviceBroker);
	}

	[Fact]
	public void Ctor_NullServiceBroker()
	{
		Assert.Throws<ArgumentNullException>(() => new ServiceBrokerClient(null!));
	}

	[Fact]
	public void IsDisposed()
	{
		Assert.False(this.client.IsDisposed);
		this.client.Dispose();
		Assert.True(this.client.IsDisposed);
	}

	[Fact]
	public void Dispose_Twice()
	{
		this.client.Dispose();
		this.client.Dispose();
	}

	[Fact]
	public async Task Dispose_ServiceFactoryThrows()
	{
		await Assert.ThrowsAsync<ServiceActivationFailedException>(
			async () => await this.client.GetProxyAsync<ICalculator>(TestServices.Throws, this.TimeoutToken));

		this.client.Dispose();
	}

	[Fact]
	public async Task GetProxyAsync_CachesAndInvalidates()
	{
		ICalculator lastCalc;
		using (ServiceBrokerClient.Rental<ICalculator> calc = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			Assumes.NotNull(calc.Proxy);
			lastCalc = calc.Proxy;
		}

		using (ServiceBrokerClient.Rental<ICalculator> calc = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			Assert.Same(lastCalc, calc.Proxy);
		}

		this.InvalidateAllServices();

		using (ServiceBrokerClient.Rental<ICalculator> calc = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			Assert.NotSame(lastCalc, calc.Proxy);
		}
	}

	[Fact]
	public void Proxy_UninitializedRentalThrowsInvalidOperationException()
	{
		var rental = default(ServiceBrokerClient.Rental<ICalculator>);
		Assert.False(rental.IsInitialized);
		Assert.Throws<InvalidOperationException>(() => rental.Proxy);
	}

	[Fact]
	public async Task Proxy_AfterReturnedThrowsObjectDisposedException()
	{
		ServiceBrokerClient.Rental<ICalculator> rental = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken);
		Assert.True(rental.IsInitialized);
		rental.Dispose();
		Assert.Throws<ObjectDisposedException>(() => rental.Proxy);
		Assert.True(rental.IsInitialized);
	}

	[Fact]
	public async Task Invalidate_SubsetOfProxies()
	{
		ICalculator lastCalc;
		using (ServiceBrokerClient.Rental<ICalculator> calc = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			Assumes.NotNull(calc.Proxy);
			lastCalc = calc.Proxy;
		}

		IEchoService lastEcho;
		using (ServiceBrokerClient.Rental<IEchoService> echo = await this.client.GetProxyAsync<IEchoService>(TestServices.Echo, this.TimeoutToken))
		{
			Assumes.NotNull(echo.Proxy);
			lastEcho = echo.Proxy;
		}

		this.InvalidateServices(TestServices.Echo.Moniker);

		// Assert that we get a new Echo service proxy but retain the same calculator proxy.
		using (ServiceBrokerClient.Rental<IEchoService> echo = await this.client.GetProxyAsync<IEchoService>(TestServices.Echo, this.TimeoutToken))
		{
			Assert.NotSame(lastEcho, echo.Proxy);
		}

		using (ServiceBrokerClient.Rental<ICalculator> calc = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			Assert.Same(lastCalc, calc.Proxy);
		}
	}

	[Fact]
	public async Task GetProxyAsync_WithOptions_CachesAndInvalidates()
	{
		IEchoService lastCalc;
		using (ServiceBrokerClient.Rental<IEchoService> calc = await this.client.GetProxyAsync<IEchoService>(TestServices.Echo, GetOptions("1"), this.TimeoutToken))
		{
			Assumes.NotNull(calc.Proxy);
			lastCalc = calc.Proxy;
			Assert.Equal("1", ((EchoService)calc.Proxy).Options.ActivationArguments!["a"]);
		}

		using (ServiceBrokerClient.Rental<IEchoService> calc = await this.client.GetProxyAsync<IEchoService>(TestServices.Echo, GetOptions("2"), this.TimeoutToken))
		{
			Assumes.NotNull(calc.Proxy);
			Assert.Same(lastCalc, calc.Proxy);
			Assert.Equal("1", ((EchoService)calc.Proxy).Options.ActivationArguments!["a"]); // options=2 should have been ignored since this is cached.
		}

		this.InvalidateAllServices();

		using (ServiceBrokerClient.Rental<IEchoService> calc = await this.client.GetProxyAsync<IEchoService>(TestServices.Echo, GetOptions("3"), this.TimeoutToken))
		{
			Assumes.NotNull(calc.Proxy);
			Assert.NotSame(lastCalc, calc.Proxy);
			Assert.Equal("3", ((EchoService)calc.Proxy).Options.ActivationArguments!["a"]);
		}

		ServiceActivationOptions GetOptions(string uniqueValue) => new ServiceActivationOptions { ActivationArguments = new Dictionary<string, string> { { "a", uniqueValue } } };
	}

	[Fact]
	public async Task GetProxyAsync_ServiceMissing()
	{
		using (ServiceBrokerClient.Rental<ICalculator> calc = await this.client.GetProxyAsync<ICalculator>(SomeRpcDescriptor, this.TimeoutToken))
		{
			Assert.True(calc.IsInitialized);
			Assert.Null(calc.Proxy);
		}
	}

	[Fact]
	public async Task Invalidate_RaisesEvent()
	{
		var raisedEvent = new TaskCompletionSource<object?>();
		this.client.Invalidated += (s, e, ct) =>
		{
			try
			{
				Assert.Same(this.client, s);
				raisedEvent.SetResult(null);
			}
			catch (Exception ex)
			{
				raisedEvent.SetException(ex);
			}

			return Task.CompletedTask;
		};

		// No event can be expected before we ask for any proxy.
		ServiceBrokerClient.Rental<IEchoService> proxy = await this.client.GetProxyAsync<IEchoService>(TestServices.Echo, this.TimeoutToken);
		(proxy as IDisposable)?.Dispose();

		this.InvalidateAllServices();
		await raisedEvent.Task.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task Invalidate_DisposesOldRentals()
	{
		ICalculator lastCalc;
		using (ServiceBrokerClient.Rental<ICalculator> calc = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			Assumes.NotNull(calc.Proxy);
			lastCalc = calc.Proxy;
		}

		this.InvalidateAllServices();
		await ((Calculator)lastCalc).WaitForDisposalAsync(this.TimeoutToken);
	}

	[Fact]
	public async Task Invalidated_CallsAllHandlersEvenWhenFirstThrows()
	{
		var finalHandlerInvoked = new TaskCompletionSource<object?>();
		this.client.Invalidated += (s, e, ct) => throw new InvalidOperationException();
		this.client.Invalidated += (s, e, ct) => Task.FromException(new InvalidOperationException());
		this.client.Invalidated += (s, e, ct) =>
		{
			finalHandlerInvoked.SetResult(null);
			return Task.CompletedTask;
		};

		// No event can be expected before we ask for any proxy.
		ServiceBrokerClient.Rental<IEchoService> proxy = await this.client.GetProxyAsync<IEchoService>(TestServices.Echo, this.TimeoutToken);
		(proxy as IDisposable)?.Dispose();

		this.InvalidateAllServices();
		await finalHandlerInvoked.Task.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task Invalidated_RaisedWithinSemaphore()
	{
		var raisedEvent = new TaskCompletionSource<object?>();
		this.client.Invalidated += (s, e, ct) =>
		{
			raisedEvent.SetResult(null);
			return Task.CompletedTask;
		};

		// Hold the semaphore and don't let go.
		var releaseSemaphore = new AsyncManualResetEvent();
		Task semaphoreHoldingTask = this.client.InvalidationSemaphore.ExecuteAsync(releaseSemaphore.WaitAsync, this.TimeoutToken);

		// No event can be expected before we ask for any proxy.
		ServiceBrokerClient.Rental<IEchoService> proxy = await this.client.GetProxyAsync<IEchoService>(TestServices.Echo, this.TimeoutToken);
		(proxy as IDisposable)?.Dispose();

		// Invalidate and assert that the event handler is not raised.
		this.InvalidateAllServices();
		await Assert.ThrowsAnyAsync<TimeoutException>(() => raisedEvent.Task.WithTimeout(AsyncDelay));

		// Now release the semaphore and verify that the event is raised.
		releaseSemaphore.Set();
		await raisedEvent.Task.WithCancellation(this.TimeoutToken);
	}

	[Fact]
	public async Task Invalidated_RaisedWhenProxyDies()
	{
		this.serviceBroker = new TestServiceBroker();
		this.client = new ServiceBrokerClient(ServiceBrokerAggregator.ForceMarshal(this.serviceBroker));

		TaskCompletionSource<BrokeredServicesChangedEventArgs> invalidated = new(TaskCreationOptions.RunContinuationsAsynchronously);
		this.client.Invalidated += (s, e, ct) =>
		{
			invalidated.TrySetResult(e);
			return Task.CompletedTask;
		};

		using (ServiceBrokerClient.Rental<ICalculator> rental = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			// Arrange for the connection to be lost (on the service side).
			this.serviceBroker.ForceKillLastServicePipe();

			BrokeredServicesChangedEventArgs args = await invalidated.Task.WithCancellation(this.TimeoutToken);
			Assert.False(args.OtherServicesImpacted);
			Assert.Equal(TestServices.Calculator.Moniker, Assert.Single(args.ImpactedServices));
		}
	}

	[Fact]
	public async Task Invalidate_WhileProxiesUsed()
	{
		Calculator underlyingService;
		using (ServiceBrokerClient.Rental<ICalculator> calc1 = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			Assumes.NotNull(calc1.Proxy);
			underlyingService = (Calculator)calc1.Proxy;

			using (ServiceBrokerClient.Rental<ICalculator> calc2 = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
			{
				Assert.Same(calc1.Proxy, calc2.Proxy);

				// Signal all proxies as invalidated.
				this.InvalidateAllServices();

				// Verify that the proxy we have a rental on did NOT get disposed.
				await Assert.ThrowsAnyAsync<OperationCanceledException>(() => underlyingService.WaitForDisposalAsync(ExpectedTimeoutToken));
			}

			// Verify that the proxy we have a rental on did NOT get disposed.
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => underlyingService.WaitForDisposalAsync(ExpectedTimeoutToken));
		}

		// With all rentals completed, verify that the service DID get disposed.
		await underlyingService.WaitForDisposalAsync(this.TimeoutToken);
	}

	[Fact]
	public async Task ReleaseRentalAfterDisposal()
	{
		Calculator calcService;
		using (ServiceBrokerClient.Rental<ICalculator> calc = await this.client.GetProxyAsync<ICalculator>(TestServices.Calculator, this.TimeoutToken))
		{
			Assumes.NotNull(calc.Proxy);
			calcService = (Calculator)calc.Proxy;

			this.client.Dispose();

			// Verify that our proxy is still alive.
			Assumes.NotNull(calc.Proxy);
			Assert.Equal(3, await calc.Proxy.AddAsync(1, 2));
		}

		// Verify that the service was released now that we've released our rental.
		await calcService.WaitForDisposalAsync(this.TimeoutToken);
	}

	[Fact]
	public void Ctor_DoesNotCreateStrongRefToItself()
	{
		WeakReference weakClient = Helper();
		GC.Collect();
		Assert.False(weakClient.IsAlive);

		[MethodImpl(MethodImplOptions.NoInlining)]
		WeakReference Helper()
		{
			return new WeakReference(new ServiceBrokerClient(this.serviceBroker));
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			this.client.Dispose();
		}

		base.Dispose(disposing);
	}

	private void InvalidateAllServices()
	{
		this.serviceBroker.OnAvailabilityChanged(new BrokeredServicesChangedEventArgs(ImmutableHashSet.Create<ServiceMoniker>(), otherServicesImpacted: true));
	}

	private void InvalidateServices(params ServiceMoniker[] monikers)
	{
		this.serviceBroker.OnAvailabilityChanged(new BrokeredServicesChangedEventArgs(monikers.ToImmutableHashSet(), otherServicesImpacted: false));
	}
}
