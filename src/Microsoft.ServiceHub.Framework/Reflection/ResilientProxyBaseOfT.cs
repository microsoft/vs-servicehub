// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Microsoft.VisualStudio.Threading;
using StreamJsonRpc;

#pragma warning disable SA1649 // The generic and non-generic base classes cannot both match their file names.

namespace Microsoft.ServiceHub.Framework.Reflection;

/// <summary>
/// Base class for a source-generated resilient proxy with a strongly typed inner proxy.
/// </summary>
/// <typeparam name="T">The primary service interface.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class ResilientProxyBase<T> : ResilientProxyBase
	where T : class
{
	private readonly object syncObject = new();
	private readonly object eventTransitionSyncObject = new();
	private readonly IServiceBroker serviceBroker;
	private readonly ServiceRpcDescriptor serviceDescriptor;
	private readonly ServiceActivationOptions options;
	private readonly CancellationTokenSource disposalCancellationSource = new();
	private readonly CancellationToken disposalCancellationToken;
	private readonly List<IResilientEvent> resilientEvents = [];
	private readonly HashSet<Task<T?>> activeRefreshTasks = [];

	private Task<T?>? refreshTask;
	private Task refreshPublicationGate = Task.CompletedTask;
	private Generation? currentGeneration;
	private Task notificationTail = Task.CompletedTask;
	private TaskCompletionSource<object?> availabilityChanged = CreateTaskCompletionSource();
	private long nextGeneration;
	private long refreshVersion;
	private bool disposed;

	/// <summary>
	/// Initializes a new instance of the <see cref="ResilientProxyBase{T}"/> class.
	/// </summary>
	/// <param name="serviceBroker">The broker used to acquire inner proxies.</param>
	/// <param name="serviceDescriptor">The service descriptor.</param>
	/// <param name="options">Service activation options.</param>
	protected ResilientProxyBase(IServiceBroker serviceBroker, ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options)
	{
		this.serviceBroker = Requires.NotNull(serviceBroker);
		this.serviceDescriptor = IncludeRequiredAdditionalInterfaces(Requires.NotNull(serviceDescriptor), this.GetType());
		this.options = options;
		this.disposalCancellationToken = this.disposalCancellationSource.Token;
		this.serviceBroker.AvailabilityChanged += this.ServiceBroker_AvailabilityChanged;
	}

	/// <summary>
	/// Attaches and detaches one generated contract event.
	/// </summary>
	private interface IResilientEvent
	{
		/// <summary>
		/// Attaches the event to an inner proxy.
		/// </summary>
		/// <param name="generation">The inner proxy generation.</param>
		void Attach(Generation generation);

		/// <summary>
		/// Detaches the event from an inner proxy.
		/// </summary>
		/// <param name="proxy">The inner proxy.</param>
		void Detach(T proxy);
	}

	/// <inheritdoc />
	public override bool IsDisposed
	{
		get
		{
			lock (this.syncObject)
			{
				return this.disposed;
			}
		}
	}

	/// <inheritdoc />
	public override void Dispose()
	{
		if (!this.TryBeginDispose(out Generation? generation, out IResilientEvent[] events, out TaskCompletionSource<object?> availabilityChanged, out _))
		{
			return;
		}

		this.serviceBroker.AvailabilityChanged -= this.ServiceBroker_AvailabilityChanged;
		this.disposalCancellationSource.Cancel();
		availabilityChanged.TrySetResult(null);
		try
		{
			if (generation is not null)
			{
				Exception? detachException = this.DetachGeneration(generation, events);
				generation.Release();
				if (detachException is not null)
				{
					ExceptionDispatchInfo.Capture(detachException).Throw();
				}
			}
		}
		finally
		{
			this.disposalCancellationSource.Dispose();
			this.OnDisposed();
		}
	}

	/// <inheritdoc />
	public override async ValueTask DisposeAsync()
	{
		if (!this.TryBeginDispose(out Generation? generation, out IResilientEvent[] events, out TaskCompletionSource<object?> availabilityChanged, out Task<T?>[] refreshTasks))
		{
			return;
		}

		this.serviceBroker.AvailabilityChanged -= this.ServiceBroker_AvailabilityChanged;
#if NET
		await this.disposalCancellationSource.CancelAsync().ConfigureAwait(false);
#else
#pragma warning disable VSTHRD103 // CancellationTokenSource.CancelAsync is unavailable on these targets.
		this.disposalCancellationSource.Cancel();
#pragma warning restore VSTHRD103
#endif
		availabilityChanged.TrySetResult(null);
		Task generationDisposal = Task.CompletedTask;
		try
		{
			Exception? detachException = null;
			if (generation is not null)
			{
				detachException = this.DetachGeneration(generation, events);
				generationDisposal = generation.ReleaseAsync();
			}

			await this.notificationTail.NoThrowAwaitable(captureContext: false);
			try
			{
				await Task.WhenAll(refreshTasks).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (this.disposalCancellationToken.IsCancellationRequested)
			{
			}

			await generationDisposal.ConfigureAwait(false);
			if (detachException is not null)
			{
				ExceptionDispatchInfo.Capture(detachException).Throw();
			}
		}
		finally
		{
			this.disposalCancellationSource.Dispose();
			this.OnDisposed();
		}
	}

	/// <inheritdoc />
	protected override async ValueTask<bool> InitializeAsync(CancellationToken cancellationToken)
	{
		return await this.GetOrStartRefreshTaskAsync().WithCancellation(cancellationToken).ConfigureAwait(false) is not null;
	}

	/// <summary>
	/// Acquires a lease on the current inner proxy.
	/// </summary>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>A lease that keeps the inner proxy alive for the invocation.</returns>
	/// <exception cref="ServiceUnavailableException">Thrown when the service is temporarily unavailable.</exception>
	protected async ValueTask<ProxyRental> RentProxyAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			Generation? generation;
			lock (this.syncObject)
			{
				Verify.NotDisposed(!this.disposed, this);
				generation = this.currentGeneration;
				if (generation is not null && generation.TryAddReference())
				{
					return new ProxyRental(generation);
				}
			}

			if (await this.GetOrStartRefreshTaskAsync().WithCancellation(cancellationToken).ConfigureAwait(false) is null)
			{
				throw new ServiceUnavailableException($"Brokered service '{this.serviceDescriptor.Moniker}' is temporarily unavailable.");
			}
		}
	}

	/// <summary>
	/// Invokes an asynchronous stream while retaining its inner proxy.
	/// </summary>
	/// <typeparam name="TResult">The element type.</typeparam>
	/// <param name="invocation">The operation to invoke.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The asynchronous sequence.</returns>
	protected async IAsyncEnumerable<TResult> InvokeAsyncEnumerableAsync<TResult>(
		Func<T, IAsyncEnumerable<TResult>> invocation,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		Requires.NotNull(invocation);
		using ProxyRental rental = await this.RentProxyAsync(cancellationToken).ConfigureAwait(false);
		await foreach (TResult item in invocation(rental.Proxy).WithCancellation(cancellationToken).ConfigureAwait(false))
		{
			yield return item;
		}
	}

	/// <summary>
	/// Queues a notification for ordered dispatch on an available inner proxy.
	/// </summary>
	/// <param name="invocation">The notification to invoke.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	protected void InvokeNotification(Action<T> invocation, CancellationToken cancellationToken)
	{
		Requires.NotNull(invocation);
		cancellationToken.ThrowIfCancellationRequested();

		TaskCompletionSource<object?> dispatchGate = CreateTaskCompletionSource();
		lock (this.syncObject)
		{
			Verify.NotDisposed(!this.disposed, this);
			this.notificationTail = this.InvokeNotificationAsync(dispatchGate.Task, this.notificationTail, invocation, cancellationToken);
		}

		dispatchGate.TrySetResult(null);
	}

	/// <summary>
	/// Creates a contract event registration that follows replacement proxies.
	/// </summary>
	/// <typeparam name="THandler">The event handler type.</typeparam>
	/// <param name="handler">The forwarding handler to apply to each inner proxy.</param>
	/// <param name="addHandler">Adds the forwarding handler to an inner proxy.</param>
	/// <param name="removeHandler">Removes the forwarding handler from an inner proxy.</param>
	/// <returns>The event registration.</returns>
	protected ResilientEvent<THandler> CreateEvent<THandler>(THandler handler, Action<T, THandler> addHandler, Action<T, THandler> removeHandler)
		where THandler : Delegate
	{
		var resilientEvent = new ResilientEvent<THandler>(this, handler, addHandler, removeHandler);
		lock (this.syncObject)
		{
			this.resilientEvents.Add(resilientEvent);
		}

		return resilientEvent;
	}

	/// <summary>
	/// Atomically updates an event handler field.
	/// </summary>
	/// <typeparam name="THandler">The event handler type.</typeparam>
	/// <param name="handlers">The field to update.</param>
	/// <param name="value">The handler to add or remove.</param>
	/// <param name="add"><see langword="true"/> to add the handler; otherwise, remove it.</param>
	/// <returns><see langword="true"/> when at least one handler remains.</returns>
#pragma warning disable SA1204 // Generated proxies call this helper alongside the instance event helper above.
	protected static bool UpdateEventHandlers<THandler>(ref THandler? handlers, THandler? value, bool add)
#pragma warning restore SA1204
		where THandler : Delegate
	{
		THandler? oldValue;
		THandler? newValue;
		do
		{
			oldValue = handlers;
			newValue = (THandler?)(add ? Delegate.Combine(oldValue, value) : Delegate.Remove(oldValue, value));
		}
		while (Interlocked.CompareExchange(ref handlers, newValue, oldValue) != oldValue);

		return newValue is not null;
	}

	/// <inheritdoc />
	protected override void TraceEventHandlerFailure(Exception exception)
	{
		this.serviceDescriptor.TraceSource?.TraceEvent(TraceEventType.Error, 0, "A resilient proxy event handler failed: {0}", exception);
	}

	private static TaskCompletionSource<object?> CreateTaskCompletionSource()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static ServiceRpcDescriptor IncludeRequiredAdditionalInterfaces(
		ServiceRpcDescriptor serviceDescriptor,
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type generatedProxyType)
	{
		IEnumerable<Type> generatedAdditionalInterfaces = generatedProxyType.GetInterfaces().Where(
			iface => !iface.IsAssignableFrom(typeof(T)) && !iface.IsAssignableFrom(typeof(IResilientServiceProxy)));
		return serviceDescriptor switch
		{
			ServiceJsonRpcDescriptor descriptor => descriptor.WithAdditionalServiceInterfaces(
				CombineAdditionalInterfaces(descriptor.AdditionalServiceInterfaces, generatedAdditionalInterfaces)),
			ServiceJsonRpcPolyTypeDescriptor descriptor => descriptor.WithAdditionalServiceInterfaces(
				CombineAdditionalInterfaces(descriptor.AdditionalServiceInterfaces, generatedAdditionalInterfaces)),
			_ => serviceDescriptor,
		};

		static ImmutableArray<Type> CombineAdditionalInterfaces(ImmutableArray<Type>? existing, IEnumerable<Type> generated)
			=> [.. (existing ?? []).Concat(generated).Distinct()];
	}

	private Task<T?> GetOrStartRefreshTaskAsync()
	{
		TaskCompletionSource<T?>? refreshSource = null;
		Task<T?> refresh;
		Task publicationGate;
		long version;
		lock (this.syncObject)
		{
			Verify.NotDisposed(!this.disposed, this);
			if (this.refreshTask is not null)
			{
				return this.refreshTask;
			}

			refreshSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
			refresh = this.refreshTask = refreshSource.Task;
			this.activeRefreshTasks.Add(refresh);
			publicationGate = this.refreshPublicationGate;
			version = this.refreshVersion;
		}

		this.RefreshAsync(refreshSource, version, publicationGate).Forget();
		return refresh;
	}

	private async Task RefreshAsync(TaskCompletionSource<T?> refreshSource, long version, Task publicationGate)
	{
		try
		{
#pragma warning disable VSTHRD003 // RefreshCoreAsync awaits only work initiated by this proxy and the publication gate it created.
			await this.RefreshCoreAsync(refreshSource, version, publicationGate).ConfigureAwait(false);
#pragma warning restore VSTHRD003
		}
		finally
		{
			lock (this.syncObject)
			{
				this.activeRefreshTasks.Remove(refreshSource.Task);
			}
		}
	}

	private async Task RefreshCoreAsync(TaskCompletionSource<T?> refreshSource, long version, Task publicationGate)
	{
		T? proxy;
		try
		{
#pragma warning disable ISB001 // The generation owns and disposes this proxy.
			proxy = await this.serviceBroker.GetProxyAsync<T>(this.serviceDescriptor, this.options, this.disposalCancellationToken).ConfigureAwait(false);
#pragma warning restore ISB001
		}
		catch (Exception ex)
		{
			bool refreshSuperseded;
			lock (this.syncObject)
			{
				if (ReferenceEquals(this.refreshTask, refreshSource.Task))
				{
					this.refreshTask = null;
				}

				refreshSuperseded = !this.disposed && version != this.refreshVersion;
			}

			if (refreshSuperseded)
			{
				await this.CompleteFromCurrentRefreshAsync(refreshSource).ConfigureAwait(false);
			}
			else
			{
				refreshSource.TrySetException(ex);
			}

			return;
		}

#pragma warning disable VSTHRD003 // The gate is created by this proxy to order publication after its lifecycle event.
		await publicationGate.ConfigureAwait(false);
#pragma warning restore VSTHRD003

		Generation? candidateGeneration = proxy is null ? null : new Generation(this, proxy, Interlocked.Increment(ref this.nextGeneration));
		candidateGeneration?.SubscribeToDisconnection();
		IResilientEvent[] events = [];
		bool superseded;
		lock (this.syncObject)
		{
			superseded = !this.disposed && version != this.refreshVersion;
			if (!this.disposed && !superseded && candidateGeneration is not null && !candidateGeneration.IsDisconnected)
			{
				events = [.. this.resilientEvents];
			}
		}

		bool installed = false;
		TaskCompletionSource<object?>? availabilityChangedToSignal = null;
		lock (this.eventTransitionSyncObject)
		{
			if (candidateGeneration is not null)
			{
				foreach (IResilientEvent resilientEvent in events)
				{
					resilientEvent.Attach(candidateGeneration);
				}
			}

			lock (this.syncObject)
			{
				if (ReferenceEquals(this.refreshTask, refreshSource.Task))
				{
					this.refreshTask = null;
				}

				superseded = !this.disposed && (version != this.refreshVersion || this.currentGeneration is not null);
				if (!this.disposed && !superseded && candidateGeneration is not null && !candidateGeneration.IsDisconnected)
				{
					this.currentGeneration = candidateGeneration;
					availabilityChangedToSignal = this.availabilityChanged;
					installed = true;
				}
			}
		}

		if (installed)
		{
			availabilityChangedToSignal!.TrySetResult(null);
			refreshSource.TrySetResult(candidateGeneration!.Proxy);
			return;
		}

		if (candidateGeneration is not null)
		{
			if (this.DetachGeneration(candidateGeneration, events) is Exception detachException)
			{
				this.TraceEventHandlerFailure(detachException);
			}

			try
			{
				await candidateGeneration.ReleaseAsync().ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				refreshSource.TrySetException(ex);
				return;
			}
		}

		if (superseded)
		{
			await this.CompleteFromCurrentRefreshAsync(refreshSource).ConfigureAwait(false);
		}
		else
		{
			refreshSource.TrySetResult(null);
		}
	}

	private async Task CompleteFromCurrentRefreshAsync(TaskCompletionSource<T?> refreshSource)
	{
		try
		{
			T? currentProxy;
			lock (this.syncObject)
			{
				currentProxy = this.currentGeneration?.Proxy;
			}

			currentProxy ??= await this.GetOrStartRefreshTaskAsync().ConfigureAwait(false);
			refreshSource.TrySetResult(currentProxy);
		}
		catch (Exception ex)
		{
			refreshSource.TrySetException(ex);
		}
	}

	private async Task InvokeNotificationAsync(Task dispatchGate, Task previous, Action<T> invocation, CancellationToken cancellationToken)
	{
#pragma warning disable VSTHRD003 // The gate is created by this proxy to defer dispatch until its coordinator lock is released.
		await dispatchGate.ConfigureAwait(false);
#pragma warning restore VSTHRD003
		await previous.NoThrowAwaitable(captureContext: false);
		using CancellationTokenSource linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.disposalCancellationToken);
		try
		{
			while (true)
			{
				ProxyRental rental;
				try
				{
					rental = await this.RentProxyAsync(linkedCancellationSource.Token).ConfigureAwait(false);
				}
				catch (ServiceUnavailableException)
				{
					Task availabilityTask;
					lock (this.syncObject)
					{
						if (this.currentGeneration is not null || this.refreshTask is not null)
						{
							continue;
						}

						availabilityTask = this.availabilityChanged.Task;
					}

					await availabilityTask.WithCancellation(linkedCancellationSource.Token).ConfigureAwait(false);
					continue;
				}

				using (rental)
				{
					invocation(rental.Proxy);
				}

				return;
			}
		}
		catch (OperationCanceledException) when (linkedCancellationSource.IsCancellationRequested)
		{
		}
		catch (ObjectDisposedException) when (this.IsDisposed)
		{
		}
		catch (Exception ex)
		{
			this.serviceDescriptor.TraceSource?.TraceEvent(TraceEventType.Error, 0, "A queued resilient proxy notification failed: {0}", ex);
		}
	}

	private void ServiceBroker_AvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs e)
	{
		if (!e.OtherServicesImpacted && !e.ImpactedServices.Contains(this.serviceDescriptor.Moniker))
		{
			return;
		}

		this.Invalidate(expectedGeneration: null);
	}

	private void Invalidate(Generation? expectedGeneration)
	{
		Generation? generation;
		IResilientEvent[] events;
		TaskCompletionSource<object?> oldAvailabilityChanged;
		TaskCompletionSource<object?>? publicationGate = null;
		lock (this.syncObject)
		{
			if (this.disposed)
			{
				return;
			}

			generation = this.currentGeneration;
			if (expectedGeneration is not null && !ReferenceEquals(generation, expectedGeneration))
			{
				return;
			}

			if (generation is not null)
			{
				this.currentGeneration = null;
				publicationGate = CreateTaskCompletionSource();
				this.refreshPublicationGate = publicationGate.Task;
			}

			this.refreshVersion++;
			this.refreshTask = null;
			oldAvailabilityChanged = this.availabilityChanged;
			this.availabilityChanged = CreateTaskCompletionSource();
			events = [.. this.resilientEvents];
		}

		try
		{
			if (generation is not null)
			{
				if (this.DetachGeneration(generation, events) is Exception detachException)
				{
					this.TraceEventHandlerFailure(detachException);
				}

				this.OnInvalidated(new ResilientServiceProxyInvalidatedEventArgs(this.serviceDescriptor.Moniker, generation.Number));
				publicationGate!.TrySetResult(null);
				generation.Release();
			}
		}
		finally
		{
			publicationGate?.TrySetResult(null);
			oldAvailabilityChanged.TrySetResult(null);
			try
			{
				_ = this.GetOrStartRefreshTaskAsync();
			}
			catch (ObjectDisposedException) when (this.IsDisposed)
			{
			}
		}
	}

	private bool TryBeginDispose(out Generation? generation, out IResilientEvent[] events, out TaskCompletionSource<object?> availabilityChanged, out Task<T?>[] refreshTasks)
	{
		lock (this.syncObject)
		{
			if (this.disposed)
			{
				generation = null;
				events = [];
				availabilityChanged = this.availabilityChanged;
				refreshTasks = [];
				return false;
			}

			this.disposed = true;
			this.refreshVersion++;
			this.refreshTask = null;
			generation = this.currentGeneration;
			this.currentGeneration = null;
			events = [.. this.resilientEvents];
			availabilityChanged = this.availabilityChanged;
			refreshTasks = [.. this.activeRefreshTasks];
			return true;
		}
	}

	private Exception? DetachGeneration(Generation generation, IResilientEvent[] events)
	{
		Exception? firstException = null;
		lock (this.eventTransitionSyncObject)
		{
			foreach (IResilientEvent resilientEvent in events)
			{
				try
				{
					resilientEvent.Detach(generation.Proxy);
				}
				catch (Exception ex)
				{
					firstException ??= ex;
				}
			}
		}

		try
		{
			generation.UnsubscribeFromDisconnection();
		}
		catch (Exception ex)
		{
			firstException ??= ex;
		}

		return firstException;
	}

	private bool IsCurrent(T proxy)
	{
		lock (this.syncObject)
		{
			return ReferenceEquals(this.currentGeneration?.Proxy, proxy);
		}
	}

	private bool TryRentCurrentProxy(T? expectedProxy, out ProxyRental rental)
	{
		lock (this.syncObject)
		{
			Generation? generation = this.currentGeneration;
			if (generation is not null
				&& (expectedProxy is null || ReferenceEquals(generation.Proxy, expectedProxy))
				&& generation.TryAddReference())
			{
				rental = new ProxyRental(generation);
				return true;
			}
		}

		rental = default;
		return false;
	}

	/// <summary>
	/// A lease on an inner proxy generation.
	/// </summary>
	protected readonly struct ProxyRental : IDisposable
	{
		private readonly Generation generation;

		/// <summary>
		/// Initializes a new instance of the <see cref="ProxyRental"/> struct.
		/// </summary>
		/// <param name="generation">The leased generation.</param>
		internal ProxyRental(Generation generation)
		{
			this.generation = generation;
		}

		/// <summary>
		/// Gets the inner proxy.
		/// </summary>
		public T Proxy => this.generation.Proxy;

		/// <summary>
		/// Releases this lease.
		/// </summary>
		public void Dispose() => this.generation.Release();
	}

	/// <summary>
	/// Tracks a contract event across inner proxy generations.
	/// </summary>
	/// <typeparam name="THandler">The event handler type.</typeparam>
	protected sealed class ResilientEvent<THandler> : IResilientEvent
		where THandler : Delegate
	{
		private readonly object syncObject = new();
		private readonly ResilientProxyBase<T> owner;
		private readonly THandler handler;
		private readonly Action<T, THandler> addHandler;
		private readonly Action<T, THandler> removeHandler;

		private bool active;
		private T? attachedProxy;

		/// <summary>
		/// Initializes a new instance of the <see cref="ResilientEvent{THandler}"/> class.
		/// </summary>
		/// <param name="owner">The resilient proxy.</param>
		/// <param name="handler">The forwarding handler.</param>
		/// <param name="addHandler">Attaches the forwarding handler.</param>
		/// <param name="removeHandler">Detaches the forwarding handler.</param>
		internal ResilientEvent(ResilientProxyBase<T> owner, THandler handler, Action<T, THandler> addHandler, Action<T, THandler> removeHandler)
		{
			this.owner = owner;
			this.handler = handler;
			this.addHandler = addHandler;
			this.removeHandler = removeHandler;
		}

		/// <summary>
		/// Sets whether the forwarding handler should be attached.
		/// </summary>
		/// <param name="value"><see langword="true"/> when the contract event has subscribers.</param>
		public void SetActive(bool value)
		{
			lock (this.owner.eventTransitionSyncObject)
			{
				lock (this.syncObject)
				{
					this.active = value;
					if (value && this.attachedProxy is null)
					{
						if (this.owner.TryRentCurrentProxy(expectedProxy: null, out ProxyRental rental))
						{
							try
							{
								this.addHandler(rental.Proxy, this.handler);
								this.attachedProxy = rental.Proxy;
								if (!this.owner.IsCurrent(rental.Proxy))
								{
									this.removeHandler(rental.Proxy, this.handler);
									this.attachedProxy = null;
								}
							}
							catch (Exception ex)
							{
								this.attachedProxy = null;
								this.owner.TraceEventHandlerFailure(ex);
							}
							finally
							{
								rental.Dispose();
							}
						}
					}
					else if (!value && this.attachedProxy is not null)
					{
						try
						{
							this.removeHandler(this.attachedProxy, this.handler);
						}
						catch (Exception ex)
						{
							this.owner.TraceEventHandlerFailure(ex);
						}

						this.attachedProxy = null;
					}
				}
			}
		}

		/// <inheritdoc />
		void IResilientEvent.Attach(Generation generation) => this.Attach(generation);

		/// <inheritdoc />
		void IResilientEvent.Detach(T proxy) => this.Detach(proxy);

		private void Attach(Generation generation)
		{
			lock (this.syncObject)
			{
				if (!this.active || ReferenceEquals(this.attachedProxy, generation.Proxy))
				{
					return;
				}

				if (!generation.TryAddReference())
				{
					return;
				}

				try
				{
					if (this.attachedProxy is not null)
					{
						this.removeHandler(this.attachedProxy, this.handler);
						this.attachedProxy = null;
					}

					this.addHandler(generation.Proxy, this.handler);
					this.attachedProxy = generation.Proxy;
				}
				catch (Exception ex)
				{
					this.attachedProxy = null;
					this.owner.TraceEventHandlerFailure(ex);
				}
				finally
				{
					generation.Release();
				}
			}
		}

		private void Detach(T proxy)
		{
			lock (this.syncObject)
			{
				if (!ReferenceEquals(this.attachedProxy, proxy))
				{
					return;
				}

				try
				{
					this.removeHandler(proxy, this.handler);
				}
				catch (Exception ex)
				{
					this.owner.TraceEventHandlerFailure(ex);
				}

				this.attachedProxy = null;
			}
		}
	}

	/// <summary>
	/// Owns one inner proxy and tracks its active leases.
	/// </summary>
#pragma warning disable SA1202 // This implementation type follows the protected generated-proxy helpers that expose its leases.
	internal sealed class Generation
#pragma warning restore SA1202
	{
		private readonly ResilientProxyBase<T> owner;
		private readonly TaskCompletionSource<object?> disposalCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private EventHandler<JsonRpcDisconnectedEventArgs>? disconnectedHandler;
		private int disconnected;
		private int referenceCount = 1;
		private int preferAsyncDisposal;

		/// <summary>
		/// Initializes a new instance of the <see cref="Generation"/> class.
		/// </summary>
		/// <param name="owner">The resilient proxy.</param>
		/// <param name="proxy">The inner proxy.</param>
		/// <param name="number">The generation number.</param>
		internal Generation(ResilientProxyBase<T> owner, T proxy, long number)
		{
			this.owner = owner;
			this.Proxy = proxy;
			this.Number = number;
		}

		/// <summary>
		/// Gets the inner proxy.
		/// </summary>
		internal T Proxy { get; }

		/// <summary>
		/// Gets the generation number.
		/// </summary>
		internal long Number { get; }

		/// <summary>
		/// Gets a value indicating whether the RPC connection has disconnected.
		/// </summary>
		internal bool IsDisconnected => Volatile.Read(ref this.disconnected) != 0;

		/// <summary>
		/// Attempts to acquire another lease.
		/// </summary>
		/// <returns><see langword="true"/> when the lease was acquired.</returns>
		internal bool TryAddReference()
		{
			int oldValue;
			do
			{
				oldValue = Volatile.Read(ref this.referenceCount);
				if (oldValue == 0)
				{
					return false;
				}
			}
			while (Interlocked.CompareExchange(ref this.referenceCount, oldValue + 1, oldValue) != oldValue);

			return true;
		}

		/// <summary>
		/// Releases one lease.
		/// </summary>
		internal void Release()
		{
			if (Interlocked.Decrement(ref this.referenceCount) != 0)
			{
				return;
			}

			if (Volatile.Read(ref this.preferAsyncDisposal) != 0 && this.Proxy is System.IAsyncDisposable)
			{
				this.DisposeProxyAsync(reportFailure: true).Forget();
				return;
			}

			try
			{
				if (this.Proxy is IDisposable disposable)
				{
					disposable.Dispose();
					this.disposalCompletion.TrySetResult(null);
				}
				else if (this.Proxy is System.IAsyncDisposable)
				{
					this.DisposeProxyAsync(reportFailure: false).Forget();
				}
				else
				{
					this.disposalCompletion.TrySetResult(null);
				}
			}
			catch (Exception ex)
			{
				this.disposalCompletion.TrySetException(ex);
				throw;
			}
		}

		/// <summary>
		/// Releases one lease and requests asynchronous proxy disposal.
		/// </summary>
		/// <returns>A task that completes when the proxy is disposed.</returns>
		internal Task ReleaseAsync()
		{
			Volatile.Write(ref this.preferAsyncDisposal, 1);
			this.Release();
#pragma warning disable VSTHRD003 // The completion source represents disposal initiated by Release above.
			return this.disposalCompletion.Task;
#pragma warning restore VSTHRD003
		}

		/// <summary>
		/// Subscribes to RPC disconnection.
		/// </summary>
		internal void SubscribeToDisconnection()
		{
			if (this.Proxy is IJsonRpcClientProxy jsonRpcProxy)
			{
				this.disconnectedHandler = (sender, args) =>
				{
					Volatile.Write(ref this.disconnected, 1);
					this.owner.Invalidate(this);
				};
				jsonRpcProxy.JsonRpc.Disconnected += this.disconnectedHandler;
			}
		}

		/// <summary>
		/// Unsubscribes from RPC disconnection.
		/// </summary>
		internal void UnsubscribeFromDisconnection()
		{
			if (this.Proxy is IJsonRpcClientProxy jsonRpcProxy && this.disconnectedHandler is not null)
			{
				jsonRpcProxy.JsonRpc.Disconnected -= this.disconnectedHandler;
				this.disconnectedHandler = null;
			}
		}

		private async Task DisposeProxyAsync(bool reportFailure)
		{
			try
			{
				await ((System.IAsyncDisposable)this.Proxy).DisposeAsync().ConfigureAwait(false);
				this.disposalCompletion.TrySetResult(null);
			}
			catch (Exception ex)
			{
				if (reportFailure)
				{
					this.disposalCompletion.TrySetException(ex);
				}
				else
				{
					this.owner.TraceEventHandlerFailure(ex);
					this.disposalCompletion.TrySetResult(null);
				}
			}
		}
	}
}
