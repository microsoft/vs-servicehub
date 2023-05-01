// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A wrapper around <see cref="IServiceBroker"/> that caches and shares client proxies.
/// </summary>
public class ServiceBrokerClient : IDisposableObservable
{
	private readonly object syncObject = new object();

	/// <summary>
	/// The <see cref="JoinableTaskFactory"/> that can be used to mitigate deadlocks. May be null.
	/// </summary>
	private readonly JoinableTaskFactory? joinableTaskFactory;

	/// <summary>
	/// A cache of current (non-stale) proxies.
	/// </summary>
	private readonly Dictionary<(ServiceMoniker Moniker, Type ClientType), AsyncLazy<object?>> clientCache = new Dictionary<(ServiceMoniker Moniker, Type ClientType), AsyncLazy<object?>>();

	/// <summary>
	/// A map of any currently rented proxies with a count of open rentals.
	/// </summary>
	private readonly Dictionary<AsyncLazy<object?>, int> rentedProxies = new Dictionary<AsyncLazy<object?>, int>();

	/// <summary>
	/// A flag indicating whether the <see cref="ServiceBroker_AvailabilityChanged(object?, BrokeredServicesChangedEventArgs)"/>
	/// handler has been wired up to the <see cref="IServiceBroker.AvailabilityChanged"/> event on
	/// <see cref="serviceBroker"/> already.
	/// </summary>
	private bool availabilityChangedHookedUp;

	/// <summary>
	/// The source for a <see cref="CancellationToken"/> which was handed to the last raising of the <see cref="Invalidated"/> event.
	/// </summary>
	private CancellationTokenSource? lastInvalidationEventCancellationSource;

	/// <summary>
	/// The inner <see cref="IServiceBroker"/> from which client proxies are obtained.
	/// </summary>
	private IServiceBroker serviceBroker;

	/// <summary>
	/// The set of proxies that are stale but still being rented.
	/// </summary>
	private ImmutableHashSet<AsyncLazy<object?>> staleRentedProxies = ImmutableHashSet<AsyncLazy<object?>>.Empty;

	/// <summary>
	/// Initializes a new instance of the <see cref="ServiceBrokerClient"/> class.
	/// </summary>
	/// <param name="serviceBroker">The underlying service broker.</param>
	/// <param name="joinableTaskFactory">A means to avoid deadlocks if the authorization service requires the main thread. May be null.</param>
	public ServiceBrokerClient(IServiceBroker serviceBroker, JoinableTaskFactory? joinableTaskFactory = null)
	{
		Requires.NotNull(serviceBroker, nameof(serviceBroker));

		this.serviceBroker = serviceBroker;
		this.joinableTaskFactory = joinableTaskFactory;
		this.InvalidationSemaphore = ReentrantSemaphore.Create(joinableTaskContext: joinableTaskFactory?.Context, mode: ReentrantSemaphore.ReentrancyMode.NotAllowed);
	}

	/// <summary>
	/// The delegate for a handler of the <see cref="Invalidated"/> event.
	/// </summary>
	/// <param name="sender">The <see cref="ServiceBrokerClient"/> instance that is raising the event.</param>
	/// <param name="args">Details regarding which services actually changed that led to this event being raised.</param>
	/// <param name="cancellationToken">A token that is canceled when the new set of services begun with this event is itself invalidated.</param>
	/// <returns>A task whose completion will allow a subsequent invocation of this event handler.</returns>
	public delegate Task ClientProxiesInvalidatedEventHandler(ServiceBrokerClient sender, BrokeredServicesChangedEventArgs args, CancellationToken cancellationToken);

	/// <summary>
	/// Occurs when previously acquired proxies have gone stale.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Handlers should release any outstanding rentals at their earliest convenience and use <see cref="GetProxyAsync{T}(ServiceRpcDescriptor, CancellationToken)"/> to get new proxies.
	/// Exceptions thrown or faulted tasks returned by the handler are ignored.
	/// </para>
	/// <para>
	/// Handlers return a <see cref="Task"/> to they can carry out asynchronous operations such as acquiring and initializing new services without fear that another invocation of their handler will happen concurrently.
	/// Any further invalidation event will await for handlers of the prior event to complete before raising the next one. The <see cref="CancellationToken"/> provided to the earlier invocation signals that a follow-up event is waiting to be raised to reset the services again.
	/// Note however that even if the event handler has not yet completed, all calls to <see cref="GetProxyAsync{T}(ServiceRpcDescriptor, CancellationToken)"/> will always return a proxy to the most current service available.
	/// </para>
	/// </remarks>
	public event ClientProxiesInvalidatedEventHandler? Invalidated;

	/// <summary>
	/// Gets the semaphore that is entered to raise the <see cref="Invalidated"/> event.
	/// </summary>
	/// <remarks>
	/// This can be used to enter the same semaphore during initialization in order to ensure that an <see cref="Invalidated"/> event does not disrupt initialization.
	/// </remarks>
	public ReentrantSemaphore InvalidationSemaphore { get; }

	/// <inheritdoc/>
	public bool IsDisposed { get; private set; }

	/// <summary>
	/// Requests access to some service through a client proxy.
	/// The same client proxy is returned for a given service and proxy type until it is invalidated.
	/// </summary>
	/// <typeparam name="T">The type of client proxy to create.</typeparam>
	/// <param name="serviceRpcDescriptor">An descriptor of the service.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>
	/// A rental around the client proxy that may be used to communicate with the service; or <see langword="null"/> if no matching service could be found.
	/// Proxies are kept alive while "rented", and may be kept alive beyond a rental until they are invalidated.
	/// The rental struct should be disposed as soon as the caller is done using it (such that the next use will call <see cref="GetProxyAsync{T}(ServiceRpcDescriptor, CancellationToken)"/> again and could tolerate getting a client proxy to a different service instance.)
	/// The client proxy itself within the rental struct should NOT be disposed directly since it can be shared across invocations of this method.
	/// </returns>
	/// <exception cref="ServiceCompositionException">Thrown when a service discovery or activation error occurs.</exception>
	public ValueTask<Rental<T>> GetProxyAsync<T>(ServiceRpcDescriptor serviceRpcDescriptor, CancellationToken cancellationToken)
		where T : class
	{
		return this.GetProxyAsync<T>(serviceRpcDescriptor, options: default, cancellationToken);
	}

	/// <summary>
	/// Requests access to some service through a client proxy.
	/// The same client proxy is returned for a given service and proxy type until it is invalidated.
	/// </summary>
	/// <typeparam name="T">The type of client proxy to create.</typeparam>
	/// <param name="serviceRpcDescriptor">An descriptor of the service.</param>
	/// <param name="options">Additional options that alter how the service may be activated or provide additional data to the service constructor. Only used if the service has not already been cached.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>
	/// A rental around the client proxy that may be used to communicate with the service; or <see langword="null"/> if no matching service could be found.
	/// Proxies are kept alive while "rented", and may be kept alive beyond a rental until they are invalidated.
	/// The rental struct should be disposed as soon as the caller is done using it (such that the next use will call <see cref="GetProxyAsync{T}(ServiceRpcDescriptor, CancellationToken)"/> again and could tolerate getting a client proxy to a different service instance.)
	/// The client proxy itself within the rental struct should NOT be disposed directly since it can be shared across invocations of this method.
	/// </returns>
	/// <exception cref="ServiceCompositionException">Thrown when a service discovery or activation error occurs.</exception>
	public async ValueTask<Rental<T>> GetProxyAsync<T>(ServiceRpcDescriptor serviceRpcDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		where T : class
	{
		Requires.NotNull(serviceRpcDescriptor);
		Verify.NotDisposed(this);

		AsyncLazy<object?>? clientLazy;
		lock (this.syncObject)
		{
			Verify.NotDisposed(this);
			this.EnsureAvailabilityChangedIsHookedUp();

			(ServiceMoniker Moniker, Type) key = (serviceRpcDescriptor.Moniker, typeof(T));
			if (!this.clientCache.TryGetValue(key, out clientLazy))
			{
				clientLazy = new AsyncLazy<object?>(
					async () =>
					{
						Verify.NotDisposed(this);
						GC.KeepAlive(typeof(ValueTask<T>)); // workaround CLR bug https://dev.azure.com/devdiv/DevDiv/_workitems/edit/1358442
						return await this.serviceBroker.GetProxyAsync<T>(serviceRpcDescriptor, options).ConfigureAwait(false);
					},
					this.joinableTaskFactory);
				this.clientCache.Add(key, clientLazy);
			}

			this.rentedProxies.TryGetValue(clientLazy, out int oldCount);
			this.rentedProxies[clientLazy] = oldCount + 1;
		}

		try
		{
			var value = (T?)await clientLazy.GetValueAsync(cancellationToken).ConfigureAwait(false);
			return new Rental<T>(this, clientLazy, value);
		}
		catch
		{
			this.ReleaseRental(clientLazy, proxy: null);
			throw;
		}
	}

	/// <summary>
	/// Invalidates all previously produced client proxies and disposes this object.
	/// Any client proxies currently rented will be disposed of when they are all returned.
	/// </summary>
	public void Dispose()
	{
		this.Dispose(true);
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// Disposes managed and unmanaged resources held by this instance.
	/// </summary>
	/// <param name="disposing"><see langword="true"/> to dispose managed and native resources; <see langword="false"/> to dispose of only native resources.</param>
	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			List<IDisposable>? disposableProxies;
			lock (this.syncObject)
			{
				if (this.IsDisposed)
				{
					return;
				}

				this.IsDisposed = true;

				// Invalidate all proxies.
				disposableProxies = this.InvalidateProxies(new BrokeredServicesChangedEventArgs(ImmutableHashSet<ServiceMoniker>.Empty, otherServicesImpacted: true));
			}

			this.serviceBroker.AvailabilityChanged -= this.ServiceBroker_AvailabilityChanged;
			(this.serviceBroker as IDisposable)?.Dispose();
			this.lastInvalidationEventCancellationSource?.Dispose();

			// Dispose the proxies outside of our own private lock.
			this.DisposeOldProxies(disposableProxies);
		}
	}

	/// <summary>
	/// Raises the <see cref="Invalidated"/> event and swallows any exceptions thrown by handlers.
	/// </summary>
	private void OnInvalidated(BrokeredServicesChangedEventArgs args)
	{
		Requires.NotNull(args, nameof(args));

		// Take a snapshot of the handlers we have when the event is originally meant to be raised.
		// This is the set we owe notifications to.
		ClientProxiesInvalidatedEventHandler? invalidated = this.Invalidated;
		if (invalidated is null)
		{
			return;
		}

		CancellationTokenSource? oldInvalidationCancellationSource;
		lock (this.syncObject)
		{
			oldInvalidationCancellationSource = this.lastInvalidationEventCancellationSource;
			using (this.lastInvalidationEventCancellationSource = new CancellationTokenSource())
			{
				CancellationToken currentInvalidationEventCancellation = this.lastInvalidationEventCancellationSource.Token;
				_ = this.InvalidationSemaphore.ExecuteAsync(
					async delegate
					{
						// We never want to invoke the handlers on the main thread.
						// We also don't want to inline any of the handlers within our private lock.
						await TaskScheduler.Default.SwitchTo(alwaysYield: true);

						// Because our event handlers are async, we have to take very special care to invoke and await the tasks from *all* the returned handlers.
						Delegate[] invocationList = invalidated.GetInvocationList();
						var handlerTasks = new Task[invocationList.Length];
						for (int i = 0; i < invocationList.Length; i++)
						{
							var handler = (ClientProxiesInvalidatedEventHandler)invocationList[i];
							try
							{
								handlerTasks[i] = handler(this, args, currentInvalidationEventCancellation);
							}
							catch (Exception ex)
							{
								handlerTasks[i] = Task.FromException(ex);
							}
						}

						// We don't care what the handlers throw.
						await Task.WhenAll(handlerTasks).NoThrowAwaitable();
					},
					currentInvalidationEventCancellation);
			}
		}

		// Whatever event handlers were being raised in a prior session, let them know it's out of date so they can bail out early and let the next round in sooner.
		try
		{
			oldInvalidationCancellationSource?.Cancel();
		}
		catch (ObjectDisposedException)
		{
			// .NET Framework can throw this.
		}
	}

	/// <summary>
	/// Invalidates proxies of changed services and raises the <see cref="Invalidated"/> event.
	/// </summary>
	private void ServiceBroker_AvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs e)
	{
		Requires.NotNull(e, nameof(e));
		if (this.IsDisposed)
		{
			return;
		}

		try
		{
			this.DisposeOldProxies(this.InvalidateProxies(e));
		}
		finally
		{
			this.OnInvalidated(e);
		}
	}

	/// <summary>
	/// Releases a rental and disposes of the client proxy if appropriate.
	/// </summary>
	/// <param name="clientProxy">The shared client proxy with its lazy wrapper.</param>
	/// <param name="proxy">The value from the <see cref="AsyncLazy{T}"/>, if it could be obtained; otherwise <see langword="null"/>.</param>
	private void ReleaseRental(AsyncLazy<object?> clientProxy, object? proxy)
	{
		Requires.NotNull(clientProxy, nameof(clientProxy));

		bool disposeProxy = false;
		lock (this.syncObject)
		{
			if (this.rentedProxies.TryGetValue(clientProxy, out int oldCount))
			{
				if (oldCount <= 1)
				{
					this.rentedProxies.Remove(clientProxy);

					// We are the last active rental for this proxy. If it is a stale proxy, dispose of it now.
					if (this.staleRentedProxies.Contains(clientProxy))
					{
						disposeProxy = true;
						this.staleRentedProxies = this.staleRentedProxies.Remove(clientProxy);
					}
				}
				else
				{
					this.rentedProxies[clientProxy] = oldCount - 1;
				}
			}
		}

		if (disposeProxy)
		{
			(proxy as IDisposable)?.Dispose();
		}
	}

	/// <summary>
	/// Invalidates all current proxies.
	/// </summary>
	/// <returns>A list of proxies that are stale and not rented and thus should be disposed of. May be null if no proxies need to be disposed of.</returns>
	private List<IDisposable>? InvalidateProxies(BrokeredServicesChangedEventArgs args)
	{
		Requires.NotNull(args, nameof(args));

		List<IDisposable>? unusedStaleProxies = null;
		lock (this.syncObject)
		{
			var staleRentedProxies = this.staleRentedProxies.ToBuilder();
			var proxiesToRemoveFromCache = new List<(ServiceMoniker, Type)>();
			foreach (KeyValuePair<(ServiceMoniker Moniker, Type ClientType), AsyncLazy<object?>> monikerAndClientProxy in this.clientCache)
			{
				if (!args.OtherServicesImpacted && !args.ImpactedServices.Contains(monikerAndClientProxy.Key.Moniker))
				{
					// This cached client is not impacted by the change, so skip it.
					continue;
				}

				proxiesToRemoveFromCache.Add(monikerAndClientProxy.Key);
				AsyncLazy<object?> clientProxy = monikerAndClientProxy.Value;
				if (this.rentedProxies.TryGetValue(clientProxy, out int userCount) && userCount > 0)
				{
					// This stale proxy is currently being rented, so we will dispose it when the rentals are all returned.
					staleRentedProxies.Add(clientProxy);
				}
				else if (clientProxy.IsValueFactoryCompleted)
				{
					Task valueFactoryTask = clientProxy.GetValueAsync();

					// Only dispose the client proxy if the factory task completed without faulting or cancellation.
					if ((valueFactoryTask.Status == TaskStatus.RanToCompletion) &&
						(clientProxy.GetValue() is IDisposable disposableClientProxy))
					{
						// We can dispose these now.
						if (unusedStaleProxies == null)
						{
							unusedStaleProxies = new List<IDisposable>();
						}

						unusedStaleProxies.Add(disposableClientProxy);
					}
				}

				this.staleRentedProxies = staleRentedProxies.ToImmutable();
			}

			foreach ((ServiceMoniker, Type) toRemove in proxiesToRemoveFromCache)
			{
				this.clientCache.Remove(toRemove);
			}
		}

		return unusedStaleProxies;
	}

	/// <summary>
	/// Disposes old proxies.
	/// </summary>
	/// <param name="disposableProxies">The list of proxies to dispose. May be <see langword="null"/>.</param>
	/// <exception cref="AggregateException">Thrown with all exceptions thrown by proxy <see cref="IDisposable.Dispose"/> methods.</exception>
	private void DisposeOldProxies(List<IDisposable>? disposableProxies)
	{
		if (disposableProxies == null)
		{
			return;
		}

		ImmutableList<Exception> exceptions = ImmutableList<Exception>.Empty;
		foreach (IDisposable proxy in disposableProxies)
		{
			try
			{
				proxy.Dispose();
			}
			catch (Exception ex)
			{
				// one failure shouldn't prevent us from disposing the rest.
				exceptions = exceptions.Add(ex);
			}
		}

		if (!exceptions.IsEmpty)
		{
			throw new AggregateException(exceptions);
		}
	}

	/// <summary>
	/// Adds the <see cref="IServiceBroker.AvailabilityChanged"/> handler.
	/// </summary>
	/// <remarks>
	/// The caller should have already entered the <see cref="syncObject"/> lock
	/// and confirmed that <see cref="IsDisposed"/> is <see langword="false"/>.
	/// </remarks>
	private void EnsureAvailabilityChangedIsHookedUp()
	{
		Assumes.True(Monitor.IsEntered(this.syncObject));
		Assumes.False(this.IsDisposed);

		if (!this.availabilityChangedHookedUp)
		{
			this.serviceBroker.AvailabilityChanged += this.ServiceBroker_AvailabilityChanged;
			this.availabilityChangedHookedUp = true;
		}
	}

	/// <summary>
	/// Provides access to a client proxy as a rental that should be disposed of to signify no active use, allowing it to be disposed of when invalidated.
	/// </summary>
	/// <typeparam name="T">The type of the client proxy.</typeparam>
	public struct Rental<T> : IDisposable
		where T : class
	{
		private readonly ServiceBrokerClient client;
		private AsyncLazy<object?>? rentedProxy;
		private T? proxy;

		/// <summary>
		/// Initializes a new instance of the <see cref="Rental{T}"/> struct.
		/// </summary>
		/// <param name="client">The owner.</param>
		/// <param name="proxy">The (already evaluated) lazy that we use to track rentals.</param>
		/// <param name="value">The client proxy itself.</param>
		public Rental(ServiceBrokerClient client, AsyncLazy<object?> proxy, T? value)
		{
			this.client = client ?? throw new ArgumentNullException(nameof(client));
			this.rentedProxy = proxy ?? throw new ArgumentNullException(nameof(proxy));
			this.proxy = value;
		}

		/// <summary>
		/// Gets the client proxy.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if this instance has not been initialized.</exception>
		/// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed (after being initialized).</exception>
		/// <remarks>
		/// This value should NOT be disposed directly since it can be shared across invocations of the <see cref="GetProxyAsync{T}(ServiceRpcDescriptor, CancellationToken)"/> method.
		/// </remarks>
		public T? Proxy
		{
			get
			{
				if (this.IsDisposed)
				{
					throw new ObjectDisposedException(this.GetType().FullName);
				}

				if (!this.IsInitialized)
				{
					throw new InvalidOperationException(Strings.NotInitialized);
				}

				return this.proxy;
			}
		}

		/// <summary>
		/// Gets a value indicating whether this rental has been initialized (and not yet disposed).
		/// </summary>
		/// <remarks>
		/// This value can be useful to discern between a default <see cref="Rental{T}"/> instance (where no proxy was yet requested)
		/// and one which is initialized but with a null proxy because no matching service was found.
		/// </remarks>
		public bool IsInitialized => this.client != null;

		/// <summary>
		/// Gets a value indicating whether this rental has already been disposed.
		/// </summary>
		private bool IsDisposed => this.client != null && this.rentedProxy == null;

		/// <summary>
		/// Returns the rental of the client proxy, allowing it to be subject to disposal upon invalidation if all rentals have been similarly disposed.
		/// </summary>
		public void Dispose()
		{
			// Some protection against double-disposal leading to over-returning of rentals.
			AsyncLazy<object?>? rentedProxy = this.rentedProxy;
			this.rentedProxy = null;

			if (rentedProxy != null)
			{
				this.client.ReleaseRental(rentedProxy, this.proxy);
			}

			this.proxy = null;
		}
	}
}
