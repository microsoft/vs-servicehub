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
	private static readonly System.Threading.AsyncLocal<LifecyclePublicationContext?> ActiveLifecyclePublication = new();

	private readonly object syncObject = new();
	private readonly IServiceBroker serviceBroker;
	private readonly ServiceRpcDescriptor serviceDescriptor;
	private readonly ServiceActivationOptions options;
	private readonly CancellationTokenSource disposalCancellationSource = new();
	private readonly CancellationToken disposalCancellationToken;
	private readonly List<IResilientAttachment> resilientAttachments = [];
	private readonly HashSet<Task> activeRefreshTasks = [];
	private readonly Dictionary<Task<T?>, Task> refreshCleanupTasks = [];
	private readonly ConditionalWeakTable<T, Generation.ProxyLifetime> proxyLifetimes = new();

	private CancellationTokenSource? refreshCancellationSource;
	private Task<T?>? refreshTask;
	private Task refreshPublicationGate = Task.CompletedTask;
	private Task lifecyclePublication = Task.CompletedTask;
	private Generation? currentGeneration;
	private Task notificationTail = Task.CompletedTask;
	private TaskCompletionSource<object?> availabilityChanged = CreateTaskCompletionSource();
	private long? pendingPreviousGeneration;
	private Generation? pendingPreviousGenerationOwner;
	private long nextGeneration;
	private long refreshVersion;
	private bool currentGenerationPublicationStarted;
	private bool currentGenerationPublished;
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
		/// <param name="refreshVersion">The refresh version that must still own publication, or <see langword="null"/> for an installed generation.</param>
		void Attach(Generation generation, long? refreshVersion = null);

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
				return this.currentGeneration is not null && (this.currentGenerationPublished || this.IsActiveLifecyclePublisher());
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
		if (!this.TryBeginDispose(out Generation? generation, out Generation? pendingGeneration, out IResilientAttachment[] attachments, out TaskCompletionSource<object?> availabilityChanged, out Task lifecyclePublication, out _))
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
#pragma warning disable VSTHRD002 // Synchronous disposal must wait for an event handler running on another thread.
		lifecyclePublication.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
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

			if (pendingGeneration is not null)
			{
				try
				{
					pendingGeneration.Release();
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
		if (!this.TryBeginDispose(out Generation? generation, out Generation? pendingGeneration, out IResilientAttachment[] attachments, out TaskCompletionSource<object?> availabilityChanged, out Task lifecyclePublication, out Task[] refreshTasks))
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
		await lifecyclePublication.ConfigureAwait(false);
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

#pragma warning disable VSTHRD003 // These tasks represent proxy disposal initiated below.
		Task[] proxyDisposals = new[] { generation, pendingGeneration }
			.Where(candidate => candidate is not null)
			.Select(candidate => candidate!.ProxyDisposal)
			.Distinct()
			.ToArray();
#pragma warning restore VSTHRD003
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

			if (pendingGeneration is not null)
			{
				try
				{
					await pendingGeneration.ReleaseAsync().ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					cleanupException ??= ex;
				}
			}

			try
			{
				await Task.WhenAll(proxyDisposals).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				cleanupException ??= ex;
			}

			Exception? attachmentException = this.DisposeAttachments(attachments);
			cleanupException ??= attachmentException;
			await this.notificationTail.NoThrowAwaitable(captureContext: false);
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
				if (generation is not null && (this.currentGenerationPublished || this.IsActiveLifecyclePublisher()) && generation.TryAddReference())
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
	protected ResilientEvent<THandler> CreateEvent<THandler>(
		THandler handler,
		Action<T, THandler> addHandler,
		Action<T, THandler> removeHandler)
		where THandler : Delegate
	{
		var resilientEvent = new ResilientEvent<THandler>(this, handler, addHandler, removeHandler);
		lock (this.syncObject)
		{
			this.resilientAttachments.Add(resilientEvent);
		}

		return resilientEvent;
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

	private Generation CreateGeneration(T proxy)
	{
		if (!this.proxyLifetimes.TryGetValue(proxy, out Generation.ProxyLifetime? proxyLifetime))
		{
			proxyLifetime = new Generation.ProxyLifetime(this, proxy);
			this.proxyLifetimes.Add(proxy, proxyLifetime);
		}
		else
		{
			Verify.Operation(proxyLifetime.TryAddReference(), "A registered proxy lifetime must still be active.");
		}

		return new Generation(this, proxy, Interlocked.Increment(ref this.nextGeneration), proxyLifetime);
	}

	private void RemoveProxyLifetime(T proxy, Generation.ProxyLifetime proxyLifetime)
	{
		if (this.proxyLifetimes.TryGetValue(proxy, out Generation.ProxyLifetime? registeredLifetime)
			&& ReferenceEquals(proxyLifetime, registeredLifetime))
		{
			this.proxyLifetimes.Remove(proxy);
		}
	}

	private Task<T?> GetOrStartRefreshTaskAsync()
	{
		TaskCompletionSource<T?>? refreshSource = null;
		Task<T?> refresh;
		Task publicationGate;
		CancellationTokenSource refreshCancellationSource;
		Generation.ProxyLifetime? retainedProxyLifetime;
		TaskCompletionSource<object?> refreshCleanupSource;
		long version;
		lock (this.syncObject)
		{
			Verify.NotDisposed(!this.disposed, this);
			if (this.currentGeneration is not null && this.currentGenerationPublished)
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
			refreshCleanupSource = CreateTaskCompletionSource();
			refreshCleanupSource.Task.Forget();
			this.activeRefreshTasks.Add(refreshCleanupSource.Task);
			this.refreshCleanupTasks.Add(refresh, refreshCleanupSource.Task);
			publicationGate = this.refreshPublicationGate;
			retainedProxyLifetime = this.pendingPreviousGenerationOwner?.RetainProxyLifetime();
			version = this.refreshVersion;
		}

		this.RefreshAsync(refreshSource, version, publicationGate, refreshCancellationSource, retainedProxyLifetime, refreshCleanupSource).Forget();
		refresh.Forget();
		return refresh;
	}

	private async Task RefreshAsync(
		TaskCompletionSource<T?> refreshSource,
		long version,
		Task publicationGate,
		CancellationTokenSource refreshCancellationSource,
		Generation.ProxyLifetime? retainedProxyLifetime,
		TaskCompletionSource<object?> refreshCleanupSource)
	{
		Exception? cleanupException = null;
		try
		{
#pragma warning disable VSTHRD003 // RefreshCoreAsync awaits only work initiated by this proxy and the publication gate it created.
			await this.RefreshCoreAsync(refreshSource, version, publicationGate, refreshCancellationSource.Token).ConfigureAwait(false);
#pragma warning restore VSTHRD003
		}
		catch (Exception ex)
		{
			cleanupException = ex;
			throw;
		}
		finally
		{
			if (retainedProxyLifetime is not null)
			{
				try
				{
#pragma warning disable VSTHRD003 // This task represents disposal initiated by the retained proxy lifetime.
					await retainedProxyLifetime.ReleaseReferenceAsync(preferAsyncDisposal: false).ConfigureAwait(false);
#pragma warning restore VSTHRD003
				}
				catch (Exception ex)
				{
					cleanupException ??= ex;
				}
			}

			lock (this.syncObject)
			{
				this.activeRefreshTasks.Remove(refreshCleanupSource.Task);
				this.refreshCleanupTasks.Remove(refreshSource.Task);
				if (ReferenceEquals(this.refreshCancellationSource, refreshCancellationSource))
				{
					this.refreshCancellationSource = null;
				}
			}

			refreshCancellationSource.Dispose();
			if (cleanupException is null)
			{
				refreshCleanupSource.TrySetResult(null);
			}
			else
			{
				refreshCleanupSource.TrySetException(cleanupException);
			}
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
#pragma warning disable VSTHRD003 // The gate is created by this proxy to order publication after detaching the prior generation.
			await publicationGate.ConfigureAwait(false);
#pragma warning restore VSTHRD003
			ResilientServiceProxyChangedEventArgs? change = null;
			bool refreshSuperseded;
			lock (this.syncObject)
			{
				if (ReferenceEquals(this.refreshTask, refreshSource.Task))
				{
					this.refreshTask = null;
				}

				refreshSuperseded = !this.disposed && version != this.refreshVersion;
				if (!this.disposed && !refreshSuperseded && this.pendingPreviousGeneration is long previousGeneration)
				{
					change = new(this.serviceDescriptor.Moniker, previousGeneration, currentGeneration: null);
				}
			}

			if (change is not null)
			{
				this.PublishBackingServiceChanged(change, version, refreshSource.Task);
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

		Generation? candidateGeneration;
		lock (this.syncObject)
		{
			candidateGeneration = proxy is null ? null : this.CreateGeneration(proxy);
		}

#pragma warning disable VSTHRD003 // The gate is created by this proxy to order publication after detaching the prior generation.
		await publicationGate.ConfigureAwait(false);
#pragma warning restore VSTHRD003

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
		TaskCompletionSource<object?>? lifecyclePublicationSource = null;
		ResilientServiceProxyChangedEventArgs? backingServiceChange = null;
		Generation? previousGenerationOwner = null;
		var synchronizedSubscriptions = new HashSet<ResilientSubscription>();
		while (true)
		{
			bool candidateValid;
			lock (this.syncObject)
			{
				candidateValid = !this.disposed
					&& version == this.refreshVersion
					&& this.currentGeneration is null
					&& candidateGeneration is not null
					&& !candidateGeneration.IsDisconnected;
			}

			if (candidateValid)
			{
				await Task.WhenAll(attachments.OfType<ResilientSubscription>()
					.Where(synchronizedSubscriptions.Add)
					.Select(subscription => subscription.AttachAsync(candidateGeneration!, reportFailure: true, refreshCancellationToken, version))).ConfigureAwait(false);
			}

			bool retryWithNewSubscriptions = false;
			lock (this.syncObject)
			{
				candidateValid = !this.disposed
					&& version == this.refreshVersion
					&& this.currentGeneration is null
					&& candidateGeneration is not null
					&& !candidateGeneration.IsDisconnected;
				IResilientAttachment[] latestAttachments = [.. this.resilientAttachments];
				retryWithNewSubscriptions = candidateValid
					&& latestAttachments.OfType<ResilientSubscription>().Any(subscription => !synchronizedSubscriptions.Contains(subscription));
				attachments = latestAttachments;
			}

			if (retryWithNewSubscriptions)
			{
				continue;
			}

			if (candidateValid)
			{
				foreach (IResilientAttachment attachment in attachments)
				{
					if (attachment is not ResilientSubscription)
					{
						attachment.Attach(candidateGeneration!, version);
					}
				}
			}

			lock (this.syncObject)
			{
				candidateValid = !this.disposed
					&& version == this.refreshVersion
					&& this.currentGeneration is null
					&& candidateGeneration is not null
					&& !candidateGeneration.IsDisconnected;
				IResilientAttachment[] latestAttachments = [.. this.resilientAttachments];
				retryWithNewSubscriptions = candidateValid
					&& latestAttachments.OfType<ResilientSubscription>().Any(subscription => !synchronizedSubscriptions.Contains(subscription));
				attachments = latestAttachments;
				if (!retryWithNewSubscriptions)
				{
					superseded = !this.disposed
						&& (version != this.refreshVersion
							|| this.currentGeneration is not null
							|| (candidateGeneration is not null && candidateGeneration.IsDisconnected));
					if (!this.disposed && !superseded && candidateGeneration is not null && !candidateGeneration.IsDisconnected)
					{
						this.currentGeneration = candidateGeneration;
						this.currentGenerationPublicationStarted = false;
						this.currentGenerationPublished = false;
						availabilityChangedToSignal = this.availabilityChanged;
						lifecyclePublicationSource = CreateTaskCompletionSource();
						this.lifecyclePublication = lifecyclePublicationSource.Task;
						this.refreshPublicationGate = lifecyclePublicationSource.Task;
						backingServiceChange = new(
							this.serviceDescriptor.Moniker,
							this.pendingPreviousGeneration,
							candidateGeneration.Number);
						installed = true;
					}
					else
					{
						if (ReferenceEquals(this.refreshTask, refreshSource.Task))
						{
							this.refreshTask = null;
						}

						if (!this.disposed && !superseded && this.pendingPreviousGeneration is long previousGeneration)
						{
							backingServiceChange = new(this.serviceDescriptor.Moniker, previousGeneration, currentGeneration: null);
						}
					}
				}
			}

			if (retryWithNewSubscriptions)
			{
				continue;
			}

			break;
		}

		if (installed)
		{
			bool completedWithCandidate = false;
			try
			{
				foreach (IResilientAttachment attachment in attachments)
				{
					if (attachment is not ResilientSubscription)
					{
						attachment.Attach(candidateGeneration!);
					}
				}

				bool publishChange;
				lock (this.syncObject)
				{
					publishChange = !this.disposed
						&& version == this.refreshVersion
						&& ReferenceEquals(this.currentGeneration, candidateGeneration)
						&& !candidateGeneration!.IsDisconnected;
					if (publishChange)
					{
						this.currentGenerationPublicationStarted = true;
						this.pendingPreviousGeneration = null;
						previousGenerationOwner = this.pendingPreviousGenerationOwner;
						this.pendingPreviousGenerationOwner = null;
					}
				}

				if (publishChange)
				{
					if (previousGenerationOwner is not null)
					{
						this.ReleasePreviousGeneration(previousGenerationOwner);
					}

					this.PublishBackingServiceChanged(backingServiceChange!, refreshSource.Task);
				}

				lock (this.syncObject)
				{
					completedWithCandidate = ReferenceEquals(this.currentGeneration, candidateGeneration)
						&& version == this.refreshVersion
						&& !this.disposed
						&& !candidateGeneration!.IsDisconnected
						&& !this.currentGenerationPublished;
					if (completedWithCandidate)
					{
						this.currentGenerationPublicationStarted = false;
						this.currentGenerationPublished = true;
					}

					if (ReferenceEquals(this.refreshTask, refreshSource.Task))
					{
						this.refreshTask = null;
					}
				}
			}
			finally
			{
				lifecyclePublicationSource!.TrySetResult(null);
			}

			if (completedWithCandidate)
			{
				availabilityChangedToSignal!.TrySetResult(null);
				refreshSource.TrySetResult(candidateGeneration!.Proxy);
			}
			else
			{
				await this.CompleteFromCurrentRefreshAsync(refreshSource).ConfigureAwait(false);
			}

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
#pragma warning disable VSTHRD103 // A superseded duplicate must not request asynchronous disposal for the surviving generation.
					candidateGeneration.Release();
#pragma warning restore VSTHRD103
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
			if (backingServiceChange is not null)
			{
				this.PublishBackingServiceChanged(backingServiceChange, version, refreshSource.Task);
			}

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

			currentProxy = this.currentGeneration is { } generation && (this.currentGenerationPublished || this.IsActiveLifecyclePublisher())
				? generation.Proxy
				: null;
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
		cancellationToken.ThrowIfCancellationRequested();
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

	private bool IsActiveLifecyclePublisher()
		=> ActiveLifecyclePublication.Value is { IsActive: true, Owner: { } owner } && ReferenceEquals(owner, this);

	private void PublishBackingServiceChanged(ResilientServiceProxyChangedEventArgs args, Task<T?> refreshTask)
	{
		LifecyclePublicationContext? priorPublication = ActiveLifecyclePublication.Value;
		var publication = new LifecyclePublicationContext(this, refreshTask);
		ActiveLifecyclePublication.Value = publication;
		try
		{
			this.OnBackingServiceChanged(args);
		}
		finally
		{
			publication.Deactivate();
			ActiveLifecyclePublication.Value = priorPublication;
		}
	}

	private void PublishBackingServiceChanged(ResilientServiceProxyChangedEventArgs args, long version, Task<T?> refreshTask)
	{
		TaskCompletionSource<object?> publicationSource = CreateTaskCompletionSource();
		Generation? previousGenerationOwner;
		lock (this.syncObject)
		{
			if (this.disposed || version != this.refreshVersion)
			{
				return;
			}

			this.pendingPreviousGeneration = null;
			previousGenerationOwner = this.pendingPreviousGenerationOwner;
			this.lifecyclePublication = publicationSource.Task;
			this.refreshPublicationGate = publicationSource.Task;
		}

		try
		{
			this.PublishBackingServiceChanged(args, refreshTask);
		}
		finally
		{
			publicationSource.TrySetResult(null);
			if (previousGenerationOwner is not null)
			{
				Task<T?>? followupRefresh;
				Generation? generationToRelease = null;
				lock (this.syncObject)
				{
					followupRefresh = !ReferenceEquals(this.refreshTask, refreshTask) ? this.refreshTask : null;
					if (followupRefresh is null && ReferenceEquals(this.pendingPreviousGenerationOwner, previousGenerationOwner))
					{
						generationToRelease = this.pendingPreviousGenerationOwner;
						this.pendingPreviousGenerationOwner = null;
					}
				}

				if (followupRefresh is null)
				{
					if (generationToRelease is not null)
					{
						this.ReleasePreviousGeneration(generationToRelease);
					}
				}
				else
				{
					this.ReleasePreviousGenerationAfterRefreshAsync(previousGenerationOwner, followupRefresh).Forget();
				}
			}
		}
	}

	private async Task ReleasePreviousGenerationAfterRefreshAsync(Generation previousGeneration, Task<T?> refresh)
	{
		await refresh.NoThrowAwaitable(captureContext: false);
		Generation? generationToRelease = null;
		lock (this.syncObject)
		{
			if (ReferenceEquals(this.pendingPreviousGenerationOwner, previousGeneration))
			{
				generationToRelease = this.pendingPreviousGenerationOwner;
				this.pendingPreviousGenerationOwner = null;
			}
		}

		if (generationToRelease is not null)
		{
			this.ReleasePreviousGeneration(generationToRelease);
		}
	}

	private void ReleasePreviousGeneration(Generation previous)
	{
		try
		{
			previous.Release();
		}
		catch (Exception ex)
		{
			this.TraceEventHandlerFailure(ex);
		}
	}

	private void Invalidate(Generation? expectedGeneration)
	{
		Generation? generation = null;
		IResilientAttachment[] attachments = [];
		CancellationTokenSource? refreshCancellationSource = null;
		TaskCompletionSource<object?>? oldAvailabilityChanged = null;
		TaskCompletionSource<object?>? publicationGate = null;
		bool releaseGeneration = true;
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
				bool publicationInProgress = !this.currentGenerationPublished && !this.lifecyclePublication.IsCompleted;
				bool publicationStarted = this.currentGenerationPublicationStarted;
				bool wasPublished = this.currentGenerationPublished;
				this.currentGeneration = null;
				this.currentGenerationPublicationStarted = false;
				this.currentGenerationPublished = false;
				if (wasPublished || publicationStarted)
				{
					this.pendingPreviousGeneration = generation.Number;
					this.pendingPreviousGenerationOwner = generation;
					releaseGeneration = false;
				}

				if (publicationInProgress)
				{
					this.refreshPublicationGate = this.lifecyclePublication;
				}
				else
				{
					publicationGate = CreateTaskCompletionSource();
					this.refreshPublicationGate = publicationGate.Task;
				}
			}

			this.refreshVersion++;
			this.refreshTask = null;
			refreshCancellationSource = this.refreshCancellationSource;
			this.refreshCancellationSource = null;
			oldAvailabilityChanged = this.availabilityChanged;
			this.availabilityChanged = CreateTaskCompletionSource();
			attachments = [.. this.resilientAttachments];
		}

		if (generation is not null && this.DetachGeneration(generation, attachments) is Exception detachException)
		{
			this.TraceEventHandlerFailure(detachException);
		}

		publicationGate?.TrySetResult(null);
		oldAvailabilityChanged.TrySetResult(null);

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
			if (releaseGeneration)
			{
				generation?.Release();
			}
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

	private bool TryBeginDispose(
		out Generation? generation,
		out Generation? pendingGeneration,
		out IResilientAttachment[] attachments,
		out TaskCompletionSource<object?> availabilityChanged,
		out Task lifecyclePublication,
		out Task[] refreshTasks)
	{
		lock (this.syncObject)
		{
			if (this.disposed)
			{
				generation = null;
				pendingGeneration = null;
				attachments = [];
				availabilityChanged = this.availabilityChanged;
				lifecyclePublication = Task.CompletedTask;
				refreshTasks = [];
				return false;
			}

			this.disposed = true;
			this.OnDisposing();
			this.refreshVersion++;
			this.refreshTask = null;
			generation = this.currentGeneration;
			this.currentGeneration = null;
			this.currentGenerationPublicationStarted = false;
			this.currentGenerationPublished = false;
			pendingGeneration = this.pendingPreviousGenerationOwner;
			this.pendingPreviousGenerationOwner = null;
			this.pendingPreviousGeneration = null;
			attachments = [.. this.resilientAttachments];
			availabilityChanged = this.availabilityChanged;
			LifecyclePublicationContext? publication = ActiveLifecyclePublication.Value;
			bool disposingFromLifecycleHandler = publication is { IsActive: true, Owner: { } owner } && ReferenceEquals(owner, this);
			lifecyclePublication = disposingFromLifecycleHandler ? Task.CompletedTask : this.lifecyclePublication;
			Task? publicationRefreshCleanup = null;
			if (publication is not null)
			{
				this.refreshCleanupTasks.TryGetValue(publication.RefreshTask, out publicationRefreshCleanup);
			}

			refreshTasks = disposingFromLifecycleHandler
				? [.. this.activeRefreshTasks.Where(task => !ReferenceEquals(task, publicationRefreshCleanup))]
				: [.. this.activeRefreshTasks];
			return true;
		}
	}

	private Exception? DetachGeneration(Generation generation, IResilientAttachment[] attachments)
	{
		Exception? firstException = null;
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

		private THandler? handlers;
		private Generation? attachedGeneration;
		private Generation? desiredGeneration;
		private bool reconciliationInProgress;
		private bool disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="ResilientEvent{THandler}"/> class.
		/// </summary>
		/// <param name="owner">The resilient proxy.</param>
		/// <param name="handler">The forwarding handler.</param>
		/// <param name="addHandler">Attaches the forwarding handler.</param>
		/// <param name="removeHandler">Detaches the forwarding handler.</param>
		internal ResilientEvent(
			ResilientProxyBase<T> owner,
			THandler handler,
			Action<T, THandler> addHandler,
			Action<T, THandler> removeHandler)
		{
			this.owner = owner;
			this.handler = handler;
			this.addHandler = addHandler;
			this.removeHandler = removeHandler;
		}

		/// <summary>
		/// Gets the contract event handlers to invoke.
		/// </summary>
		public THandler? Handlers
		{
			get
			{
				lock (this.syncObject)
				{
					return this.handlers;
				}
			}
		}

		/// <summary>
		/// Adds or removes a contract event handler and reconciles the inner event registration.
		/// </summary>
		/// <param name="value">The handler to add or remove.</param>
		/// <param name="add"><see langword="true"/> to add the handler; otherwise, remove it.</param>
		public void UpdateHandlers(THandler? value, bool add)
		{
			if (this.owner.IsDisposed)
			{
				((IResilientAttachment)this).Dispose();
				if (add)
				{
					throw new ObjectDisposedException(this.owner.GetType().FullName);
				}

				return;
			}

			ProxyRental rental = default;
			bool hasRental = this.owner.TryRentCurrentProxy(expectedProxy: null, out rental);

			try
			{
				lock (this.syncObject)
				{
					if (this.disposed)
					{
						if (add)
						{
							throw new ObjectDisposedException(this.owner.GetType().FullName);
						}

						return;
					}

					this.handlers = (THandler?)(add ? Delegate.Combine(this.handlers, value) : Delegate.Remove(this.handlers, value));
					this.desiredGeneration = this.handlers is not null && hasRental ? rental.Generation : null;
				}

				this.Reconcile();
			}
			finally
			{
				if (hasRental)
				{
					rental.Dispose();
				}
			}
		}

		/// <inheritdoc />
		void IResilientAttachment.Attach(Generation generation, long? refreshVersion) => this.Attach(generation, refreshVersion);

		/// <inheritdoc />
		void IResilientAttachment.Detach(Generation generation) => this.Detach(generation);

		/// <inheritdoc />
		void IResilientAttachment.Dispose()
		{
			lock (this.syncObject)
			{
				this.disposed = true;
				this.handlers = null;
				this.desiredGeneration = null;
			}

			this.Reconcile();
		}

		private void Attach(Generation generation, long? refreshVersion)
		{
			if (!generation.TryAddReference())
			{
				return;
			}

			try
			{
				lock (this.owner.syncObject)
				{
					if (this.owner.disposed
						|| generation.IsDisconnected
						|| (refreshVersion is long requiredVersion
							? requiredVersion != this.owner.refreshVersion || this.owner.currentGeneration is not null
							: !ReferenceEquals(this.owner.currentGeneration, generation)))
					{
						return;
					}

					lock (this.syncObject)
					{
						this.desiredGeneration = !this.disposed && this.handlers is not null ? generation : null;
					}
				}

				this.Reconcile();
			}
			finally
			{
				generation.Release();
			}
		}

		private void Detach(Generation generation)
		{
			lock (this.syncObject)
			{
				if (ReferenceEquals(this.desiredGeneration, generation))
				{
					this.desiredGeneration = null;
				}
			}

			this.Reconcile();
		}

		private void Reconcile()
		{
			lock (this.syncObject)
			{
				if (this.reconciliationInProgress)
				{
					return;
				}

				this.reconciliationInProgress = true;
			}

			while (true)
			{
				Generation? attached;
				Generation? desired;
				lock (this.syncObject)
				{
					attached = this.attachedGeneration;
					desired = this.desiredGeneration;
					if (ReferenceEquals(attached, desired))
					{
						this.reconciliationInProgress = false;
						return;
					}
				}

				if (attached is not null)
				{
					try
					{
						this.removeHandler(attached.Proxy, this.handler);
					}
					catch (Exception ex)
					{
						this.owner.TraceEventHandlerFailure(ex);
					}

					lock (this.syncObject)
					{
						if (ReferenceEquals(this.attachedGeneration, attached))
						{
							this.attachedGeneration = null;
						}
					}

					continue;
				}

				try
				{
					this.addHandler(desired!.Proxy, this.handler);
				}
				catch (Exception ex)
				{
					bool retry;
					lock (this.syncObject)
					{
						retry = !ReferenceEquals(this.desiredGeneration, desired);
						if (!retry)
						{
							this.desiredGeneration = null;
							this.reconciliationInProgress = false;
						}
					}

					this.owner.TraceEventHandlerFailure(ex);
					if (!retry)
					{
						return;
					}

					continue;
				}

				bool removeAddedHandler;
				lock (this.syncObject)
				{
					removeAddedHandler = !ReferenceEquals(this.desiredGeneration, desired)
						|| this.attachedGeneration is not null;
					if (!removeAddedHandler)
					{
						this.attachedGeneration = desired;
					}
				}

				if (removeAddedHandler)
				{
					try
					{
						this.removeHandler(desired!.Proxy, this.handler);
					}
					catch (Exception ex)
					{
						this.owner.TraceEventHandlerFailure(ex);
					}
				}
			}
		}
	}

	private sealed class LifecyclePublicationContext
	{
		private int active = 1;

		/// <summary>
		/// Initializes a new instance of the <see cref="LifecyclePublicationContext"/> class.
		/// </summary>
		/// <param name="owner">The proxy publishing the lifecycle event.</param>
		/// <param name="refreshTask">The refresh task publishing the lifecycle event.</param>
		internal LifecyclePublicationContext(ResilientProxyBase<T> owner, Task<T?> refreshTask)
		{
			this.Owner = owner;
			this.RefreshTask = refreshTask;
		}

		/// <summary>
		/// Gets a value indicating whether the lifecycle callback is still executing.
		/// </summary>
		internal bool IsActive => Volatile.Read(ref this.active) != 0;

		/// <summary>
		/// Gets the proxy publishing the lifecycle event.
		/// </summary>
		internal ResilientProxyBase<T> Owner { get; }

		/// <summary>
		/// Gets the refresh task publishing the lifecycle event.
		/// </summary>
		internal Task<T?> RefreshTask { get; }

		/// <summary>
		/// Marks the lifecycle callback as complete in all captured execution contexts.
		/// </summary>
		internal void Deactivate() => Volatile.Write(ref this.active, 0);
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

		internal Task AttachAsync(Generation generation, bool reportFailure, CancellationToken cancellationToken = default, long? refreshVersion = null)
			=> this.AttachCoreAsync(generation, reportFailure, cancellationToken, refreshVersion);

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

		void IResilientAttachment.Attach(Generation generation, long? refreshVersion)
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

		private async Task AttachCoreAsync(Generation generation, bool reportFailure, CancellationToken cancellationToken, long? refreshVersion)
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
				lock (this.owner.syncObject)
				{
					if (refreshVersion is long requiredVersion
						&& (this.owner.disposed
							|| requiredVersion != this.owner.refreshVersion
							|| this.owner.currentGeneration is not null
							|| generation.IsDisconnected))
					{
						return;
					}

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
		private readonly ProxyLifetime proxyLifetime;
		private readonly TaskCompletionSource<object?> releaseCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private EventHandler<JsonRpcDisconnectedEventArgs>? disconnectedHandler;
		private int disconnected;
		private int preferAsyncDisposal;
		private int referenceCount = 1;

		/// <summary>
		/// Initializes a new instance of the <see cref="Generation"/> class.
		/// </summary>
		/// <param name="owner">The resilient proxy.</param>
		/// <param name="proxy">The inner proxy.</param>
		/// <param name="number">The generation number.</param>
		/// <param name="proxyLifetime">The lifetime shared by all generations that use the same proxy instance.</param>
		internal Generation(ResilientProxyBase<T> owner, T proxy, long number, ProxyLifetime proxyLifetime)
		{
			this.owner = owner;
			this.Proxy = proxy;
			this.Number = number;
			this.proxyLifetime = proxyLifetime;
			this.releaseCompletion.Task.Forget();
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
		/// Gets a task that completes when the shared proxy instance is disposed.
		/// </summary>
		internal Task ProxyDisposal => this.proxyLifetime.Disposal;

		/// <summary>
		/// Retains the shared proxy lifetime while a refresh acquisition is outstanding.
		/// </summary>
		/// <returns>The retained proxy lifetime.</returns>
		internal ProxyLifetime RetainProxyLifetime()
		{
			Verify.Operation(this.proxyLifetime.TryAddReference(), "An active generation must have an active proxy lifetime.");
			return this.proxyLifetime;
		}

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
			this.ReleaseCore(preferAsyncDisposal: false);
		}

		/// <summary>
		/// Releases one lease and requests asynchronous proxy disposal.
		/// </summary>
		/// <returns>A task that completes when the proxy is disposed.</returns>
		internal Task ReleaseAsync()
		{
			this.ReleaseCore(preferAsyncDisposal: true);
#pragma warning disable VSTHRD003 // The completion source represents release of all references to this generation.
			return this.releaseCompletion.Task;
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
					this.proxyLifetime.MarkDisconnected();
					this.owner.Invalidate(this);
				};
				jsonRpcProxy.JsonRpc.Disconnected += this.disconnectedHandler;
				jsonRpcProxy.JsonRpc.Completion.ContinueWith(
					(_, state) =>
					{
						var generation = (Generation)state!;
						Volatile.Write(ref generation.disconnected, 1);
						generation.proxyLifetime.MarkDisconnected();
						generation.owner.Invalidate(generation);
					},
					this,
					CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously,
					TaskScheduler.Default).Forget();
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

		/// <summary>
		/// Coordinates disposal across generations that use the same proxy instance.
		/// </summary>
		internal sealed class ProxyLifetime
		{
			private readonly ResilientProxyBase<T> owner;
			private readonly T proxy;
			private readonly TaskCompletionSource<object?> disposalCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
			private int disconnected;
			private int preferAsyncDisposal;
			private int referenceCount = 1;

			/// <summary>
			/// Initializes a new instance of the <see cref="ProxyLifetime"/> class.
			/// </summary>
			/// <param name="owner">The resilient proxy.</param>
			/// <param name="proxy">The shared inner proxy.</param>
			internal ProxyLifetime(ResilientProxyBase<T> owner, T proxy)
			{
				this.owner = owner;
				this.proxy = proxy;
				this.disposalCompletion.Task.Forget();
			}

			/// <summary>
			/// Gets a task that completes when the proxy is disposed.
			/// </summary>
			internal Task Disposal
			{
				get
				{
#pragma warning disable VSTHRD003 // The completion source represents disposal initiated by this lifetime.
					return this.disposalCompletion.Task;
#pragma warning restore VSTHRD003
				}
			}

			/// <summary>
			/// Attempts to retain the shared proxy.
			/// </summary>
			/// <returns><see langword="true"/> if the shared proxy was retained.</returns>
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
			/// Records that the shared RPC proxy disconnected.
			/// </summary>
			internal void MarkDisconnected() => Volatile.Write(ref this.disconnected, 1);

			/// <summary>
			/// Releases one shared proxy reference.
			/// </summary>
			/// <param name="preferAsyncDisposal">A value indicating whether final disposal should be asynchronous.</param>
			/// <returns>A task that completes with final disposal, or immediately when other references remain.</returns>
			internal Task ReleaseReferenceAsync(bool preferAsyncDisposal)
			{
				if (preferAsyncDisposal)
				{
					Volatile.Write(ref this.preferAsyncDisposal, 1);
				}

				lock (this.owner.syncObject)
				{
					int remainingReferences = --this.referenceCount;
					Verify.Operation(remainingReferences >= 0, "A proxy lifetime reference cannot be released more than once.");
					if (remainingReferences != 0)
					{
						return Task.CompletedTask;
					}

					this.owner.RemoveProxyLifetime(this.proxy, this);
				}

				// A disconnected RPC proxy cannot dispatch its remote asynchronous disposal, so close its local proxy synchronously instead.
				if (Volatile.Read(ref this.preferAsyncDisposal) != 0 && this.proxy is System.IAsyncDisposable && Volatile.Read(ref this.disconnected) == 0)
				{
					this.DisposeProxyAsync(reportFailure: true).Forget();
#pragma warning disable VSTHRD003 // The completion source represents asynchronous disposal initiated above.
					return this.disposalCompletion.Task;
#pragma warning restore VSTHRD003
				}

				try
				{
					if (this.proxy is IDisposable disposable)
					{
						disposable.Dispose();
						this.disposalCompletion.TrySetResult(null);
					}
					else if (this.proxy is System.IAsyncDisposable)
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

#pragma warning disable VSTHRD003 // The completion source represents disposal initiated above.
				return this.disposalCompletion.Task;
#pragma warning restore VSTHRD003
			}

			private async Task DisposeProxyAsync(bool reportFailure)
			{
				try
				{
					await ((System.IAsyncDisposable)this.proxy).DisposeAsync().ConfigureAwait(false);
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

#pragma warning disable SA1201 // Helper methods follow the nested lifetime type they coordinate with.
		private void ReleaseCore(bool preferAsyncDisposal)
		{
			if (preferAsyncDisposal)
			{
				Volatile.Write(ref this.preferAsyncDisposal, 1);
			}

			int remainingReferences = Interlocked.Decrement(ref this.referenceCount);
			Verify.Operation(remainingReferences >= 0, "A generation reference cannot be released more than once.");
			if (remainingReferences != 0)
			{
				return;
			}

			try
			{
				Task lifetimeRelease = this.proxyLifetime.ReleaseReferenceAsync(Volatile.Read(ref this.preferAsyncDisposal) != 0);
				if (lifetimeRelease.Status == TaskStatus.RanToCompletion)
				{
					this.releaseCompletion.TrySetResult(null);
				}
				else
				{
					this.CompleteReleaseAsync(lifetimeRelease).Forget();
				}
			}
			catch (Exception ex)
			{
				this.releaseCompletion.TrySetException(ex);
				throw;
			}
		}

		private async Task CompleteReleaseAsync(Task lifetimeRelease)
		{
			try
			{
#pragma warning disable VSTHRD003 // This task represents disposal initiated by the proxy lifetime.
				await lifetimeRelease.ConfigureAwait(false);
#pragma warning restore VSTHRD003
				this.releaseCompletion.TrySetResult(null);
			}
			catch (Exception ex)
			{
				this.releaseCompletion.TrySetException(ex);
			}
		}
#pragma warning restore SA1201
	}
}
