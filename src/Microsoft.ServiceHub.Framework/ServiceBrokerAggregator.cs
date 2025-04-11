// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A class that aggregates one or more <see cref="IServiceBroker"/> instances into one, with various policies applied.
/// </summary>
public static class ServiceBrokerAggregator
{
	/// <summary>
	/// Creates a new <see cref="IServiceBroker"/>.
	/// Service requests are forwarded to a list of other <see cref="IServiceBroker"/> instances one-at-a-time.
	/// The first broker to return a successful response is returned to the caller.
	/// </summary>
	/// <param name="serviceBrokers">A list of service brokers aggregated into the new one. This collection is stored; not copied. The collection should *not* be modified while the returned broker is in use.</param>
	/// <returns>The aggregate service broker.</returns>
	public static IServiceBroker Sequential(IReadOnlyList<IServiceBroker> serviceBrokers) => new SequentialBroker(Requires.NotNull(serviceBrokers));

	/// <summary>
	/// Creates a new <see cref="IServiceBroker"/>.
	/// Service requests are forwarded to a list of other <see cref="IServiceBroker"/> instances in parallel.
	/// At most one broker is allowed to respond with a service or all results are disposed and an exception is thrown.
	/// </summary>
	/// <param name="serviceBrokers">A collection of service brokers aggregated into the new one. This collection is stored; not copied. The collection should *not* be modified while the returned broker is in use.</param>
	/// <returns>The aggregate service broker.</returns>
	public static IServiceBroker Parallel(IReadOnlyCollection<IServiceBroker> serviceBrokers) => new ParallelAtMostOneBroker(Requires.NotNull(serviceBrokers));

	/// <summary>
	/// Creates a new <see cref="IServiceBroker"/> that forces all RPC calls to be marshaled even if a service is available locally.
	/// </summary>
	/// <param name="serviceBroker">The inner service broker.</param>
	/// <returns>The marshaling service broker.</returns>
	public static IServiceBroker ForceMarshal(IServiceBroker serviceBroker) => new ForceMarshalingBroker(Requires.NotNull(serviceBroker));

	/// <summary>
	/// Creates a new <see cref="IServiceBroker"/> that does not implement <see cref="IDisposable"/>
	/// and forwards all requests to a given <see cref="IServiceBroker"/>.
	/// </summary>
	/// <param name="serviceBroker">The inner service broker to forward requests to.</param>
	/// <returns>The non-disposable wrapper.</returns>
	/// <remarks>
	/// This is useful when an <see cref="IServiceBroker"/> that may implement <see cref="IDisposable"/> is being shared
	/// such that others <em>may</em> dispose of it if it is disposable, but the caller wants to retain exclusive control
	/// over the lifetime of the broker.
	/// </remarks>
	public static IServiceBroker NonDisposable(IServiceBroker serviceBroker) => new NonDisposingServiceBroker(Requires.NotNull(serviceBroker));

	/// <summary>
	/// Creates an <see cref="IServiceBroker"/> that will lazily create the inner broker when it is first needed.
	/// </summary>
	/// <param name="lazyServiceBroker">The factory for the inner <see cref="IServiceBroker"/>.</param>
	/// <param name="joinableTaskFactory">The <see cref="JoinableTaskFactory"/> applicable to the process, if any.</param>
	/// <returns>The delegating service broker.</returns>
	public static IServiceBroker Lazy(Func<ValueTask<IServiceBroker>> lazyServiceBroker, JoinableTaskFactory? joinableTaskFactory = null) => new LazyServiceBroker(Requires.NotNull(lazyServiceBroker), joinableTaskFactory);

	/// <summary>
	/// A broker which will query many other brokers sequentially, and return the first successful result.
	/// </summary>
	private sealed class SequentialBroker : IServiceBroker, IDisposable
	{
		private readonly IReadOnlyList<IServiceBroker> serviceBrokers;

		/// <summary>
		/// Initializes a new instance of the <see cref="SequentialBroker"/> class.
		/// </summary>
		/// <param name="serviceBrokers">A list of brokers to use. This collection is stored; not copied.</param>
		internal SequentialBroker(IReadOnlyList<IServiceBroker> serviceBrokers)
		{
			this.serviceBrokers = serviceBrokers;
			foreach (IServiceBroker broker in this.serviceBrokers)
			{
				broker.AvailabilityChanged += this.OnAvailabilityChanged;
			}
		}

		/// <inheritdoc />
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		/// <inheritdoc />
		public async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
			where T : class
		{
			Requires.NotNull(serviceDescriptor, nameof(serviceDescriptor));

			foreach (IServiceBroker broker in this.serviceBrokers)
			{
				T? clientProxy = await broker.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken).ConfigureAwait(false);
				if (clientProxy is object)
				{
					return clientProxy;
				}
			}

			return null;
		}

		/// <inheritdoc />
		public async ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			Requires.NotNull(serviceMoniker, nameof(serviceMoniker));

			foreach (IServiceBroker broker in this.serviceBrokers)
			{
				IDuplexPipe? pipe = await broker.GetPipeAsync(serviceMoniker, options, cancellationToken).ConfigureAwait(false);
				if (pipe != null)
				{
					return pipe;
				}
			}

			return null;
		}

		public void Dispose()
		{
			foreach (IServiceBroker broker in this.serviceBrokers)
			{
				broker.AvailabilityChanged -= this.OnAvailabilityChanged;
			}
		}

		/// <summary>
		/// Raises the <see cref="AvailabilityChanged"/> event.
		/// </summary>
		/// <param name="sender">This parameter is ignored. The event will be raised with "this" as the sender.</param>
		/// <param name="args">Details regarding what changes have occurred.</param>
		private void OnAvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	/// <summary>
	/// A broker which will query many other brokers in parallel, but assert that at most one service comes back.
	/// </summary>
	private sealed class ParallelAtMostOneBroker : IServiceBroker, IDisposable
	{
		private readonly IReadOnlyCollection<IServiceBroker> serviceBrokers;

		/// <summary>
		/// Initializes a new instance of the <see cref="ParallelAtMostOneBroker"/> class.
		/// </summary>
		/// <param name="serviceBrokers">A collection of brokers to use. This collection is stored; not copied.</param>
		internal ParallelAtMostOneBroker(IReadOnlyCollection<IServiceBroker> serviceBrokers)
		{
			this.serviceBrokers = serviceBrokers;
			foreach (IServiceBroker broker in this.serviceBrokers)
			{
				broker.AvailabilityChanged += this.OnAvailabilityChanged;
			}
		}

		/// <inheritdoc />
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		/// <inheritdoc />
		public async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
			where T : class
		{
			Requires.NotNull(serviceDescriptor, nameof(serviceDescriptor));

			// TODO: handle exceptions
			T?[] queryResult = await Task.WhenAll(this.serviceBrokers.Select(broker => broker.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken).AsTask())).ConfigureAwait(false);
			return FindZeroOrOneMatch(serviceDescriptor.Moniker, queryResult);
		}

		/// <inheritdoc />
		public async ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			Requires.NotNull(serviceMoniker, nameof(serviceMoniker));

			IDuplexPipe?[] queryResult = await Task.WhenAll(this.serviceBrokers.Select(broker => broker.GetPipeAsync(serviceMoniker, options, cancellationToken).AsTask())).ConfigureAwait(false);
			return FindZeroOrOneMatch(serviceMoniker, queryResult);
		}

		/// <inheritdoc />
		public void Dispose()
		{
			foreach (IServiceBroker broker in this.serviceBrokers)
			{
				broker.AvailabilityChanged -= this.OnAvailabilityChanged;
			}
		}

		private static T? FindZeroOrOneMatch<T>(ServiceMoniker serviceMoniker, IReadOnlyCollection<T?> queryResult)
			where T : class
		{
			IEnumerable<T?> clients = queryResult.Where(r => r != null);
			switch (clients.Count())
			{
				case 0:
					return null;
				case 1:
					return clients.First();
				default:
					// We found too many. First we need to dispose of them all so as to avoid a leak.
					foreach (T? client in clients)
					{
						if (client is IDisposable disposable)
						{
							disposable.Dispose();
						}
						else if (client is IDuplexPipe pipe)
						{
							pipe.Input.Complete(new ServiceCompositionException(Strings.FormatTooManyServices(serviceMoniker)));
							pipe.Output.Complete(new ServiceCompositionException(Strings.FormatTooManyServices(serviceMoniker)));
						}
					}

					// Now communicate the composition failure to the caller.
					throw new ServiceCompositionException(Strings.FormatTooManyServices(serviceMoniker));
			}
		}

		/// <summary>
		/// Raises the <see cref="AvailabilityChanged"/> event.
		/// </summary>
		/// <param name="sender">This parameter is ignored. The event will be raised with "this" as the sender.</param>
		/// <param name="args">Details regarding what changes have occurred.</param>
		private void OnAvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	private class LazyServiceBroker : IServiceBroker, IDisposable
	{
		private volatile AsyncLazy<IServiceBroker>? inner;

		internal LazyServiceBroker(Func<ValueTask<IServiceBroker>> lazyServiceBroker, JoinableTaskFactory? joinableTaskFactory)
		{
			this.inner = new AsyncLazy<IServiceBroker>(
				async delegate
				{
					IServiceBroker serviceBroker = await lazyServiceBroker().ConfigureAwait(false);
					serviceBroker.AvailabilityChanged += this.ServiceBroker_AvailabilityChanged;
					return serviceBroker;
				},
				joinableTaskFactory);
		}

		/// <inheritdoc />
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged;

		/// <inheritdoc />
		public async ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
		{
			AsyncLazy<IServiceBroker>? inner = this.inner;
			Verify.NotDisposed(inner is not null, this);
			IServiceBroker sb = await inner.GetValueAsync(cancellationToken).ConfigureAwait(false);
			return await sb.GetPipeAsync(serviceMoniker, options, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
		{
			AsyncLazy<IServiceBroker>? inner = this.inner;
			Verify.NotDisposed(inner is not null, this);
			IServiceBroker sb = await inner.GetValueAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable ISB001 // Dispose of proxies - Our caller should do that.
			return await sb.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken).ConfigureAwait(false);
#pragma warning restore ISB001 // Dispose of proxies
		}

		/// <inheritdoc />
		public void Dispose()
		{
			AsyncLazy<IServiceBroker>? inner = Interlocked.Exchange(ref this.inner, null);
			if (inner is object)
			{
				if (inner.IsValueCreated)
				{
					// It is imperative that we unsubscribe our event handler to avoid a memory leak.
					// Offer a fast path for the case where creation is already completed,
					// but fallback on a more expensive path that will reverse the pending creation process when it has completed.
					if (inner.IsValueFactoryCompleted)
					{
						inner.GetValue().AvailabilityChanged -= this.ServiceBroker_AvailabilityChanged;
					}
					else
					{
						inner.GetValueAsync().ContinueWith(
							(sbTask, state) => sbTask.Result.AvailabilityChanged -= ((LazyServiceBroker)state!).ServiceBroker_AvailabilityChanged,
							this,
							CancellationToken.None,
							TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
							TaskScheduler.Default).Forget();
					}
				}
			}
		}

		private void ServiceBroker_AvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs e) => this.AvailabilityChanged?.Invoke(this, e);
	}

	private class DelegatingServiceBroker(IServiceBroker inner) : IServiceBroker
	{
		private readonly object syncObject = new();
		private EventHandler<BrokeredServicesChangedEventArgs>? availabilityChanged;

		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
		{
			add
			{
				lock (this.syncObject)
				{
					if (this.availabilityChanged is null)
					{
						inner.AvailabilityChanged += this.OnInnerAvailabilityChanged;
					}

					this.availabilityChanged += value;
				}
			}

			remove
			{
				lock (this.syncObject)
				{
					this.availabilityChanged -= value;

					if (this.availabilityChanged is null)
					{
						inner.AvailabilityChanged -= this.OnInnerAvailabilityChanged;
					}
				}
			}
		}

		protected IServiceBroker Inner => inner;

		public virtual ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			=> inner.GetPipeAsync(serviceMoniker, options, cancellationToken);

		public virtual ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
			=> inner.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken);

		private void OnInnerAvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs e)
		{
			// We explicitly forward all events so that our subscribers see `this` as the sender
			// instead of `inner`.
			this.availabilityChanged?.Invoke(this, e);
		}
	}

	/// <summary>
	/// Wraps an <see cref="IServiceBroker"/> such that any locally provisioned service is forced to marshal all calls anyway.
	/// </summary>
	private class ForceMarshalingBroker(IServiceBroker inner) : DelegatingServiceBroker(inner)
	{
		/// <inheritdoc />
		public override async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
			where T : class
		{
			IDuplexPipe? pipe = await this.Inner.GetPipeAsync(serviceDescriptor.Moniker, options, cancellationToken).ConfigureAwait(false);
			if (pipe is null)
			{
				return null;
			}

			try
			{
				return serviceDescriptor.ConstructRpc<T>(pipe);
			}
			catch
			{
				await pipe.Input.CompleteAsync().ConfigureAwait(false);
				await pipe.Output.CompleteAsync().ConfigureAwait(false);
				throw;
			}
		}
	}

	private class NonDisposingServiceBroker(IServiceBroker inner) : DelegatingServiceBroker(inner);
}
