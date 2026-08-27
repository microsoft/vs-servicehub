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
	private readonly List<IResilientAttachment> resilientAttachments = [];
	private readonly HashSet<Task<T?>> activeRefreshTasks = [];

	private CancellationTokenSource? refreshCancellationSource;
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
	private interface IResilientAttachment
	{
		/// <summary>
		/// Attaches the event to an inner proxy.
		/// </summary>
		/// <param name="generation">The inner proxy generation.</param>
		void Attach(Generation generation);

		/// <summary>
		/// Detaches the event from an inner proxy.
		/// </summary>
		/// <param name="generation">The inner proxy generation.</param>
		void Detach(Generation generation);

		/// <summary>
		/// Releases state retained by the attachment when its owner is disposed.
		/// </summary>
		void Dispose();
	}

	/// <inheritdoc />
	public override bool IsAvailable
	{
		get
		{
			lock (this.syncObject)
			{
				return this.currentGeneration is not null;
			}
		}
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
		if (!this.TryBeginDispose(out Generation? generation, out IResilientAttachment[] attachments, out TaskCompletionSource<object?> availabilityChanged, out _))
		{
			return;
		}

		this.serviceBroker.AvailabilityChanged -= this.ServiceBroker_AvailabilityChanged;
		Exception? cleanupException = null;
		try
		{
			this.disposalCancellationSource.Cancel();
		}
		catch (Exception ex)
		{
			cleanupException = ex;
		}

		availabilityChanged.TrySetResult(null);
		try
		{
			if (generation is not null)
			{
				Exception? detachException = this.DetachGeneration(generation, attachments);
				cleanupException ??= detachException;
				try
				{
					generation.Release();
				}
				catch (Exception ex)
				{
					cleanupException ??= ex;
				}
			}

			Exception? attachmentException = this.DisposeAttachments(attachments);
			cleanupException ??= attachmentException;
		}
		finally
		{
			this.disposalCancellationSource.Dispose();
			this.OnDisposed();
		}

		if (cleanupException is not null)
		{
			ExceptionDispatchInfo.Capture(cleanupException).Throw();
		}
	}

	/// <inheritdoc />
	public override async ValueTask DisposeAsync()
	{
		if (!this.TryBeginDispose(out Generation? generation, out IResilientAttachment[] attachments, out TaskCompletionSource<object?> availabilityChanged, out Task<T?>[] refreshTasks))
		{
			return;
		}

		this.serviceBroker.AvailabilityChanged -= this.ServiceBroker_AvailabilityChanged;
		Exception? cleanupException = null;
		try
		{
#if NET
			await this.disposalCancellationSource.CancelAsync().ConfigureAwait(false);
#else
#pragma warning disable VSTHRD103 // CancellationTokenSource.CancelAsync is unavailable on these targets.
			this.disposalCancellationSource.Cancel();
#pragma warning restore VSTHRD103
#endif
		}
		catch (Exception ex)
		{
			cleanupException = ex;
		}

		availabilityChanged.TrySetResult(null);
		Task generationDisposal = Task.CompletedTask;
		try
		{
			if (generation is not null)
			{
				Exception? detachException = this.DetachGeneration(generation, attachments);
				cleanupException ??= detachException;
				try
				{
					generationDisposal = generation.ReleaseAsync();
				}
				catch (Exception ex)
				{
					cleanupException ??= ex;
				}
			}

			Exception? attachmentException = this.DisposeAttachments(attachments);
			cleanupException ??= attachmentException;
			await this.notificationTail.NoThrowAwaitable(captureContext: false);
			try
			{
				await Task.WhenAll(refreshTasks).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (this.disposalCancellationToken.IsCancellationRequested)
			{
			}
			catch (Exception ex)
			{
				cleanupException ??= ex;
			}

			try
			{
				await generationDisposal.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				cleanupException ??= ex;
			}
		}
		finally
		{
			this.disposalCancellationSource.Dispose();
			this.OnDisposed();
		}

		if (cleanupException is not null)
		{
			ExceptionDispatchInfo.Capture(cleanupException).Throw();
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
	/// Creates a task-based disposable subscription that follows replacement proxies.
	/// </summary>
	/// <param name="initialInvocation">Creates the initial physical subscription.</param>
	/// <param name="reattachInvocation">Creates physical subscriptions on replacement proxies.</param>
	/// <param name="cancellationToken">A cancellation token for the initial subscription.</param>
	/// <returns>A task whose result cancels the current and all future physical subscriptions when disposed.</returns>
	protected Task<IDisposable> InvokeTaskSubscriptionAsync(
		Func<T, Task<IDisposable>> initialInvocation,
		Func<T, CancellationToken, Task<IDisposable>> reattachInvocation,
		CancellationToken cancellationToken)
	{
		Requires.NotNull(initialInvocation);
		Requires.NotNull(reattachInvocation);
		return this.InvokeSubscriptionAsync(
			proxy => new ValueTask<IDisposable>(initialInvocation(proxy)),
			(proxy, reattachCancellationToken) => new ValueTask<IDisposable>(reattachInvocation(proxy, reattachCancellationToken)),
			cancellationToken).AsTask();
	}

	/// <summary>
	/// Creates a value-task-based disposable subscription that follows replacement proxies.
	/// </summary>
	/// <param name="initialInvocation">Creates the initial physical subscription.</param>
	/// <param name="reattachInvocation">Creates physical subscriptions on replacement proxies.</param>
	/// <param name="cancellationToken">A cancellation token for the initial subscription.</param>
	/// <returns>A task whose result cancels the current and all future physical subscriptions when disposed.</returns>
	protected ValueTask<IDisposable> InvokeValueTaskSubscriptionAsync(
		Func<T, ValueTask<IDisposable>> initialInvocation,
		Func<T, CancellationToken, ValueTask<IDisposable>> reattachInvocation,
		CancellationToken cancellationToken)
	{
		Requires.NotNull(initialInvocation);
		Requires.NotNull(reattachInvocation);
		return this.InvokeSubscriptionAsync(initialInvocation, reattachInvocation, cancellationToken);
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
			this.resilientAttachments.Add(resilientEvent);
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
		CancellationTokenSource refreshCancellationSource;
		long version;
		lock (this.syncObject)
		{
			Verify.NotDisposed(!this.disposed, this);
			if (this.currentGeneration is not null)
			{
				return Task.FromResult<T?>(this.currentGeneration.Proxy);
			}

			if (this.refreshTask is not null)
			{
				return this.refreshTask;
			}

			refreshSource = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
			refresh = this.refreshTask = refreshSource.Task;
			refreshCancellationSource = this.refreshCancellationSource = new CancellationTokenSource();
			this.activeRefreshTasks.Add(refresh);
			publicationGate = this.refreshPublicationGate;
			version = this.refreshVersion;
		}

		this.RefreshAsync(refreshSource, version, publicationGate, refreshCancellationSource).Forget();
		return refresh;
	}

	private async Task RefreshAsync(
		TaskCompletionSource<T?> refreshSource,
		long version,
		Task publicationGate,
		CancellationTokenSource refreshCancellationSource)
	{
		try
		{
#pragma warning disable VSTHRD003 // RefreshCoreAsync awaits only work initiated by this proxy and the publication gate it created.
			await this.RefreshCoreAsync(refreshSource, version, publicationGate, refreshCancellationSource.Token).ConfigureAwait(false);
#pragma warning restore VSTHRD003
		}
		finally
		{
			lock (this.syncObject)
			{
				this.activeRefreshTasks.Remove(refreshSource.Task);
				if (ReferenceEquals(this.refreshCancellationSource, refreshCancellationSource))
				{
					this.refreshCancellationSource = null;
				}
			}

			refreshCancellationSource.Dispose();
		}
	}

	private async Task RefreshCoreAsync(
		TaskCompletionSource<T?> refreshSource,
		long version,
		Task publicationGate,
		CancellationToken refreshCancellationToken)
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
		IResilientAttachment[] attachments = [];
		bool superseded;
		lock (this.syncObject)
		{
			superseded = !this.disposed && version != this.refreshVersion;
			if (!this.disposed && !superseded && candidateGeneration is not null && !candidateGeneration.IsDisconnected)
			{
				attachments = [.. this.resilientAttachments];
			}
		}

		bool installed = false;
		TaskCompletionSource<object?>? availabilityChangedToSignal = null;
		var synchronizedSubscriptions = new HashSet<ResilientSubscription>();
		while (true)
		{
			if (candidateGeneration is not null)
			{
				await Task.WhenAll(attachments.OfType<ResilientSubscription>()
					.Where(synchronizedSubscriptions.Add)
					.Select(subscription => subscription.AttachAsync(candidateGeneration, reportFailure: true, refreshCancellationToken))).ConfigureAwait(false);
			}

			bool retryWithNewSubscriptions = false;
			lock (this.eventTransitionSyncObject)
			{
				lock (this.syncObject)
				{
					IResilientAttachment[] latestAttachments = [.. this.resilientAttachments];
					retryWithNewSubscriptions = candidateGeneration is not null
						&& latestAttachments.OfType<ResilientSubscription>().Any(subscription => !synchronizedSubscriptions.Contains(subscription));
					attachments = latestAttachments;
				}

				if (!retryWithNewSubscriptions)
				{
					if (candidateGeneration is not null)
					{
						foreach (IResilientAttachment attachment in attachments)
						{
							if (attachment is not ResilientSubscription)
							{
								attachment.Attach(candidateGeneration);
							}
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

					if (installed)
					{
						this.OnAvailabilityChanged();
					}
				}
			}

			if (!retryWithNewSubscriptions)
			{
				break;
			}
		}

		if (installed)
		{
			availabilityChangedToSignal!.TrySetResult(null);
			refreshSource.TrySetResult(candidateGeneration!.Proxy);
			return;
		}

		T? replacementProxy = null;
		ExceptionDispatchInfo? replacementFailure = null;
		bool replacementCanceled = false;
		if (superseded)
		{
			try
			{
				replacementProxy = await this.GetCurrentOrStartRefreshAsync().ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (this.IsDisposed)
			{
				replacementCanceled = true;
			}
			catch (Exception ex)
			{
				replacementFailure = ExceptionDispatchInfo.Capture(ex);
			}
		}

		if (candidateGeneration is not null)
		{
			bool replacementSharesProxy = ReferenceEquals(candidateGeneration.Proxy, replacementProxy);
			Exception? detachException = null;
			if (replacementSharesProxy)
			{
				try
				{
					candidateGeneration.UnsubscribeFromDisconnection();
				}
				catch (Exception ex)
				{
					detachException = ex;
				}
			}
			else
			{
				detachException = this.DetachGeneration(candidateGeneration, attachments);
			}

			if (detachException is not null)
			{
				this.TraceEventHandlerFailure(detachException);
			}

			try
			{
				if (replacementSharesProxy)
				{
					candidateGeneration.ReleaseWithoutDisposal();
				}
				else
				{
					await candidateGeneration.ReleaseAsync().ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				refreshSource.TrySetException(ex);
				return;
			}
		}

		if (superseded)
		{
			if (replacementCanceled)
			{
				refreshSource.TrySetCanceled(this.disposalCancellationToken);
			}
			else if (replacementFailure is not null)
			{
				refreshSource.TrySetException(replacementFailure.SourceException);
			}
			else
			{
				refreshSource.TrySetResult(replacementProxy);
			}
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
			refreshSource.TrySetResult(await this.GetCurrentOrStartRefreshAsync().ConfigureAwait(false));
		}
		catch (OperationCanceledException) when (this.IsDisposed)
		{
			refreshSource.TrySetCanceled(this.disposalCancellationToken);
		}
		catch (Exception ex)
		{
			refreshSource.TrySetException(ex);
		}
	}

	private async Task<T?> GetCurrentOrStartRefreshAsync()
	{
		T? currentProxy;
		lock (this.syncObject)
		{
			if (this.disposed)
			{
				throw new OperationCanceledException(this.disposalCancellationToken);
			}

			currentProxy = this.currentGeneration?.Proxy;
		}

		try
		{
			return currentProxy ?? await this.GetOrStartRefreshTaskAsync().ConfigureAwait(false);
		}
		catch (ObjectDisposedException) when (this.IsDisposed)
		{
			throw new OperationCanceledException(this.disposalCancellationToken);
		}
	}

	private async ValueTask<IDisposable> InvokeSubscriptionAsync(
		Func<T, ValueTask<IDisposable>> initialInvocation,
		Func<T, CancellationToken, ValueTask<IDisposable>> reattachInvocation,
		CancellationToken cancellationToken)
	{
		using ProxyRental rental = await this.RentProxyAsync(cancellationToken).ConfigureAwait(false);
		IDisposable innerSubscription = await initialInvocation(rental.Proxy).ConfigureAwait(false)
			?? throw new InvalidOperationException("A resilient RPC subscription method returned null.");
		var subscription = new ResilientSubscription(this, reattachInvocation);
		subscription.SetInitial(rental.Generation, innerSubscription);
		Task? reattachTask = null;
		bool ownerDisposed;
		bool needsRefreshReconciliation = false;
		try
		{
			lock (this.eventTransitionSyncObject)
			{
				Generation? currentGeneration;
				lock (this.syncObject)
				{
					ownerDisposed = this.disposed;
					currentGeneration = this.currentGeneration;
					if (!ownerDisposed)
					{
						this.resilientAttachments.Add(subscription);
					}
				}

				if (!ownerDisposed && !ReferenceEquals(currentGeneration, rental.Generation))
				{
					((IResilientAttachment)subscription).Detach(rental.Generation);
					if (currentGeneration is not null)
					{
						reattachTask = subscription.AttachAsync(currentGeneration, reportFailure: true, CancellationToken.None);
					}
					else
					{
						needsRefreshReconciliation = true;
					}
				}
			}
		}
		catch
		{
			((IDisposable)subscription).Dispose();
			throw;
		}

		if (ownerDisposed)
		{
			((IDisposable)subscription).Dispose();
			throw new ObjectDisposedException(this.GetType().FullName);
		}

		try
		{
			if (reattachTask is not null)
			{
				await reattachTask.ConfigureAwait(false);
			}

			if (needsRefreshReconciliation
				&& await this.GetOrStartRefreshTaskAsync().WithCancellation(cancellationToken).ConfigureAwait(false) is null)
			{
				throw new ServiceUnavailableException($"Brokered service '{this.serviceDescriptor.Moniker}' is temporarily unavailable.");
			}

			Generation? currentGeneration;
			lock (this.syncObject)
			{
				Verify.NotDisposed(!this.disposed, this);
				currentGeneration = this.currentGeneration;
			}

			if (currentGeneration is null)
			{
				throw new ServiceUnavailableException($"Brokered service '{this.serviceDescriptor.Moniker}' is temporarily unavailable.");
			}

			subscription.ThrowIfNotAttached(currentGeneration);
		}
		catch
		{
			((IDisposable)subscription).Dispose();
			throw;
		}

		return subscription;
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
		Generation? generation = null;
		IResilientAttachment[] attachments = [];
		CancellationTokenSource? refreshCancellationSource = null;
		TaskCompletionSource<object?>? oldAvailabilityChanged = null;
		TaskCompletionSource<object?>? publicationGate = null;
		lock (this.eventTransitionSyncObject)
		{
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
				refreshCancellationSource = this.refreshCancellationSource;
				this.refreshCancellationSource = null;
				oldAvailabilityChanged = this.availabilityChanged;
				this.availabilityChanged = CreateTaskCompletionSource();
				attachments = [.. this.resilientAttachments];
			}

			if (generation is not null)
			{
				if (this.DetachGeneration(generation, attachments) is Exception detachException)
				{
					this.TraceEventHandlerFailure(detachException);
				}

				this.OnAvailabilityChanged();
				this.OnInvalidated(new ResilientServiceProxyInvalidatedEventArgs(this.serviceDescriptor.Moniker, generation.Number));
				publicationGate!.TrySetResult(null);
			}

			publicationGate?.TrySetResult(null);
			oldAvailabilityChanged.TrySetResult(null);
		}

		try
		{
			refreshCancellationSource?.Cancel();
		}
		catch (Exception ex)
		{
			this.TraceEventHandlerFailure(ex);
		}

		try
		{
			generation?.Release();
		}
		finally
		{
			try
			{
				_ = this.GetOrStartRefreshTaskAsync();
			}
			catch (ObjectDisposedException) when (this.IsDisposed)
			{
			}
		}
	}

	private bool TryBeginDispose(out Generation? generation, out IResilientAttachment[] attachments, out TaskCompletionSource<object?> availabilityChanged, out Task<T?>[] refreshTasks)
	{
		lock (this.syncObject)
		{
			if (this.disposed)
			{
				generation = null;
				attachments = [];
				availabilityChanged = this.availabilityChanged;
				refreshTasks = [];
				return false;
			}

			this.disposed = true;
			this.refreshVersion++;
			this.refreshTask = null;
			generation = this.currentGeneration;
			this.currentGeneration = null;
			attachments = [.. this.resilientAttachments];
			availabilityChanged = this.availabilityChanged;
			refreshTasks = [.. this.activeRefreshTasks];
			return true;
		}
	}

	private Exception? DetachGeneration(Generation generation, IResilientAttachment[] attachments)
	{
		Exception? firstException = null;
		lock (this.eventTransitionSyncObject)
		{
			foreach (IResilientAttachment attachment in attachments)
			{
				try
				{
					attachment.Detach(generation);
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

	private Exception? DisposeAttachments(IResilientAttachment[] attachments)
	{
		Exception? firstException = null;
		foreach (IResilientAttachment attachment in attachments)
		{
			try
			{
				attachment.Dispose();
			}
			catch (Exception ex)
			{
				firstException ??= ex;
			}
		}

		return firstException;
	}

	private void RemoveAttachment(IResilientAttachment attachment)
	{
		lock (this.syncObject)
		{
			this.resilientAttachments.Remove(attachment);
		}
	}

	private bool IsCurrent(Generation generation)
	{
		lock (this.syncObject)
		{
			return ReferenceEquals(this.currentGeneration, generation);
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
		/// Gets the leased generation.
		/// </summary>
		internal Generation Generation => this.generation;

		/// <summary>
		/// Releases this lease.
		/// </summary>
		public void Dispose() => this.generation.Release();
	}

	/// <summary>
	/// Tracks a contract event across inner proxy generations.
	/// </summary>
	/// <typeparam name="THandler">The event handler type.</typeparam>
	protected sealed class ResilientEvent<THandler> : IResilientAttachment
		where THandler : Delegate
	{
		private readonly object syncObject = new();
		private readonly ResilientProxyBase<T> owner;
		private readonly THandler handler;
		private readonly Action<T, THandler> addHandler;
		private readonly Action<T, THandler> removeHandler;

		private bool active;
		private Generation? attachedGeneration;

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
					if (value && this.attachedGeneration is null)
					{
						if (this.owner.TryRentCurrentProxy(expectedProxy: null, out ProxyRental rental))
						{
							try
							{
								this.addHandler(rental.Proxy, this.handler);
								this.attachedGeneration = rental.Generation;
								if (!this.owner.IsCurrent(rental.Generation))
								{
									this.removeHandler(rental.Proxy, this.handler);
									this.attachedGeneration = null;
								}
							}
							catch (Exception ex)
							{
								this.attachedGeneration = null;
								this.owner.TraceEventHandlerFailure(ex);
							}
							finally
							{
								rental.Dispose();
							}
						}
					}
					else if (!value && this.attachedGeneration is not null)
					{
						try
						{
							this.removeHandler(this.attachedGeneration.Proxy, this.handler);
						}
						catch (Exception ex)
						{
							this.owner.TraceEventHandlerFailure(ex);
						}

						this.attachedGeneration = null;
					}
				}
			}
		}

		/// <inheritdoc />
		void IResilientAttachment.Attach(Generation generation) => this.Attach(generation);

		/// <inheritdoc />
		void IResilientAttachment.Detach(Generation generation) => this.Detach(generation);

		/// <inheritdoc />
		void IResilientAttachment.Dispose()
		{
		}

		private void Attach(Generation generation)
		{
			lock (this.syncObject)
			{
				if (!this.active || ReferenceEquals(this.attachedGeneration, generation))
				{
					return;
				}

				if (!generation.TryAddReference())
				{
					return;
				}

				try
				{
					if (this.attachedGeneration is not null)
					{
						this.removeHandler(this.attachedGeneration.Proxy, this.handler);
						this.attachedGeneration = null;
					}

					this.addHandler(generation.Proxy, this.handler);
					this.attachedGeneration = generation;
				}
				catch (Exception ex)
				{
					this.attachedGeneration = null;
					this.owner.TraceEventHandlerFailure(ex);
				}
				finally
				{
					generation.Release();
				}
			}
		}

		private void Detach(Generation generation)
		{
			lock (this.syncObject)
			{
				if (!ReferenceEquals(this.attachedGeneration, generation))
				{
					return;
				}

				try
				{
					this.removeHandler(generation.Proxy, this.handler);
				}
				catch (Exception ex)
				{
					this.owner.TraceEventHandlerFailure(ex);
				}

				this.attachedGeneration = null;
			}
		}
	}

	/// <summary>
	/// Tracks a disposable RPC subscription across inner proxy generations.
	/// </summary>
#pragma warning disable SA1202 // Interface implementations follow the helpers used by the containing resilient proxy.
	private sealed class ResilientSubscription : IResilientAttachment, IDisposable
	{
		private readonly object syncObject = new();
		private readonly ResilientProxyBase<T> owner;
		private CancellationTokenSource? attachmentCancellationSource;
		private ExceptionDispatchInfo? attachmentFailure;
		private Func<T, CancellationToken, ValueTask<IDisposable>>? invocation;
		private IDisposable? innerSubscription;
		private Generation? attachedGeneration;
		private Generation? attachingGeneration;
		private long attachmentVersion;
		private bool disposed;

		internal ResilientSubscription(ResilientProxyBase<T> owner, Func<T, CancellationToken, ValueTask<IDisposable>> invocation)
		{
			this.owner = owner;
			this.invocation = invocation;
		}

#pragma warning disable SA1204 // This helper follows the constructor but precedes instance methods.
		private static Exception? CancelAndDispose(CancellationTokenSource? cancellationSource)
		{
			if (cancellationSource is null)
			{
				return null;
			}

			Exception? exception = null;
			try
			{
				cancellationSource.Cancel();
			}
			catch (Exception ex)
			{
				exception = ex;
			}
			finally
			{
				cancellationSource.Dispose();
			}

			return exception;
		}
#pragma warning restore SA1204

		internal Task AttachAsync(Generation generation, bool reportFailure, CancellationToken cancellationToken = default)
			=> this.AttachCoreAsync(generation, reportFailure, cancellationToken);

		internal void SetInitial(Generation generation, IDisposable subscription)
		{
			lock (this.syncObject)
			{
				this.attachmentVersion++;
				this.attachedGeneration = generation;
				this.innerSubscription = subscription;
			}
		}

		internal void ThrowIfNotAttached(Generation generation)
		{
			ExceptionDispatchInfo? failure;
			lock (this.syncObject)
			{
				if (ReferenceEquals(this.attachedGeneration, generation))
				{
					return;
				}

				failure = this.attachmentFailure;
			}

			if (failure is not null)
			{
				failure.Throw();
			}

			throw new ServiceUnavailableException("The observer subscription could not be attached to the current service proxy.");
		}

		void IResilientAttachment.Attach(Generation generation)
		{
			throw new NotSupportedException();
		}

		/// <inheritdoc />
		void IResilientAttachment.Dispose() => this.Dispose();

		void IResilientAttachment.Detach(Generation generation)
		{
			CancellationTokenSource? cancellationSource;
			IDisposable? subscription;
			lock (this.syncObject)
			{
				if (!ReferenceEquals(this.attachedGeneration, generation) && !ReferenceEquals(this.attachingGeneration, generation))
				{
					return;
				}

				this.attachmentVersion++;
				this.attachmentFailure = null;
				this.attachedGeneration = null;
				this.attachingGeneration = null;
				cancellationSource = this.attachmentCancellationSource;
				this.attachmentCancellationSource = null;
				subscription = this.innerSubscription;
				this.innerSubscription = null;
			}

			Exception? cleanupException = CancelAndDispose(cancellationSource);
			try
			{
				subscription?.Dispose();
			}
			catch (Exception ex)
			{
				cleanupException ??= ex;
			}

			if (cleanupException is not null)
			{
				ExceptionDispatchInfo.Capture(cleanupException).Throw();
			}
		}

		/// <inheritdoc />
		void IDisposable.Dispose() => this.Dispose();

		private void Dispose()
		{
			CancellationTokenSource? cancellationSource;
			IDisposable? subscription;
			lock (this.owner.eventTransitionSyncObject)
			{
				lock (this.syncObject)
				{
					if (this.disposed)
					{
						return;
					}

					this.disposed = true;
					this.attachmentVersion++;
					this.attachmentFailure = null;
					this.invocation = null;
					this.attachedGeneration = null;
					this.attachingGeneration = null;
					cancellationSource = this.attachmentCancellationSource;
					this.attachmentCancellationSource = null;
					subscription = this.innerSubscription;
					this.innerSubscription = null;
				}

				this.owner.RemoveAttachment(this);
			}

			Exception? cleanupException = CancelAndDispose(cancellationSource);
			try
			{
				subscription?.Dispose();
			}
			catch (Exception ex)
			{
				cleanupException ??= ex;
			}

			if (cleanupException is not null)
			{
				ExceptionDispatchInfo.Capture(cleanupException).Throw();
			}
		}

		private async Task AttachCoreAsync(Generation generation, bool reportFailure, CancellationToken cancellationToken)
		{
			if (!generation.TryAddReference())
			{
				if (!reportFailure)
				{
					throw new ServiceUnavailableException("The service proxy was invalidated before the subscription could be created.");
				}

				return;
			}

			try
			{
				CancellationTokenSource attachmentCancellationSource;
				Func<T, CancellationToken, ValueTask<IDisposable>>? subscriptionFactory;
				CancellationTokenSource? previousCancellationSource;
				IDisposable? previousSubscription;
				long version;
				lock (this.syncObject)
				{
					if (this.disposed
						|| ReferenceEquals(this.attachedGeneration, generation)
						|| ReferenceEquals(this.attachingGeneration, generation))
					{
						return;
					}

					subscriptionFactory = this.invocation;
					version = ++this.attachmentVersion;
					this.attachmentFailure = null;
					this.attachedGeneration = null;
					this.attachingGeneration = generation;
					previousCancellationSource = this.attachmentCancellationSource;
					attachmentCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(this.owner.disposalCancellationToken, cancellationToken);
					this.attachmentCancellationSource = attachmentCancellationSource;
					previousSubscription = this.innerSubscription;
					this.innerSubscription = null;
				}

				if (CancelAndDispose(previousCancellationSource) is Exception cancellationException)
				{
					throw cancellationException;
				}

				previousSubscription?.Dispose();
				IDisposable subscription = await subscriptionFactory!(generation.Proxy, attachmentCancellationSource.Token).ConfigureAwait(false)
					?? throw new InvalidOperationException("A resilient RPC subscription method returned null.");
				bool disposeSubscription;
				lock (this.syncObject)
				{
					disposeSubscription = this.disposed || version != this.attachmentVersion;
					if (!disposeSubscription)
					{
						this.attachmentCancellationSource = null;
						this.attachingGeneration = null;
						this.attachedGeneration = generation;
						this.innerSubscription = subscription;
					}
				}

				attachmentCancellationSource.Dispose();
				if (disposeSubscription)
				{
					subscription.Dispose();
				}
			}
			catch (Exception ex) when (reportFailure)
			{
				CancellationTokenSource? cancellationSource = null;
				bool failureIsCurrent;
				lock (this.syncObject)
				{
					failureIsCurrent = ReferenceEquals(this.attachingGeneration, generation);
					if (failureIsCurrent)
					{
						this.attachmentFailure = ExceptionDispatchInfo.Capture(ex);
						this.attachingGeneration = null;
						cancellationSource = this.attachmentCancellationSource;
						this.attachmentCancellationSource = null;
					}
				}

				CancelAndDispose(cancellationSource);
				if (failureIsCurrent)
				{
					this.owner.TraceEventHandlerFailure(ex);
				}
			}
			finally
			{
#pragma warning disable VSTHRD103 // This releases a temporary lease; awaiting disposal would wait for the generation owner.
				generation.Release();
#pragma warning restore VSTHRD103
			}
		}
	}
#pragma warning restore SA1202

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
		private int suppressDisposal;

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

			if (Volatile.Read(ref this.suppressDisposal) != 0)
			{
				this.disposalCompletion.TrySetResult(null);
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
		/// Releases this generation without disposing a proxy instance shared by its replacement.
		/// </summary>
		internal void ReleaseWithoutDisposal()
		{
			Volatile.Write(ref this.suppressDisposal, 1);
			this.Release();
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
