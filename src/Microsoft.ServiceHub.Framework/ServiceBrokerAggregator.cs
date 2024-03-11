// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.IO.Pipelines;
using Nerdbank.Streams;

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
	public static IServiceBroker Sequential(IReadOnlyList<IServiceBroker> serviceBrokers) => new SequentialBroker(serviceBrokers);

	/// <summary>
	/// Creates a new <see cref="IServiceBroker"/>.
	/// Service requests are forwarded to a list of other <see cref="IServiceBroker"/> instances in parallel.
	/// At most one broker is allowed to respond with a service or all results are disposed and an exception is thrown.
	/// </summary>
	/// <param name="serviceBrokers">A collection of service brokers aggregated into the new one. This collection is stored; not copied. The collection should *not* be modified while the returned broker is in use.</param>
	/// <returns>The aggregate service broker.</returns>
	public static IServiceBroker Parallel(IReadOnlyCollection<IServiceBroker> serviceBrokers) => new ParallelAtMostOneBroker(serviceBrokers);

	/// <summary>
	/// Creates a new <see cref="IServiceBroker"/> that forces all RPC calls to be marshaled even if a service is available locally.
	/// </summary>
	/// <param name="serviceBroker">The inner service broker.</param>
	/// <returns>The marshaling service broker.</returns>
	public static IServiceBroker ForceMarshal(IServiceBroker serviceBroker) => new ForceMarshalingBroker(serviceBroker);

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
	public static IServiceBroker NonDisposable(IServiceBroker serviceBroker) => new NonDisposingServiceBroker(serviceBroker);

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
			this.serviceBrokers = serviceBrokers ?? throw new ArgumentNullException(nameof(serviceBrokers));
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
			this.serviceBrokers = serviceBrokers ?? throw new ArgumentNullException(nameof(serviceBrokers));
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
							pipe.Input.Complete(new ServiceCompositionException(string.Format(CultureInfo.CurrentCulture, Strings.TooManyServices, serviceMoniker)));
							pipe.Output.Complete(new ServiceCompositionException(string.Format(CultureInfo.CurrentCulture, Strings.TooManyServices, serviceMoniker)));
						}
					}

					// Now communicate the composition failure to the caller.
					throw new ServiceCompositionException(string.Format(CultureInfo.CurrentCulture, Strings.TooManyServices, serviceMoniker));
			}
		}

		/// <summary>
		/// Raises the <see cref="AvailabilityChanged"/> event.
		/// </summary>
		/// <param name="sender">This parameter is ignored. The event will be raised with "this" as the sender.</param>
		/// <param name="args">Details regarding what changes have occurred.</param>
		private void OnAvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs args) => this.AvailabilityChanged?.Invoke(this, args);
	}

	private class DelegatingServiceBroker : IServiceBroker
	{
		private readonly object syncObject = new();
		private EventHandler<BrokeredServicesChangedEventArgs>? availabilityChanged;

		internal DelegatingServiceBroker(IServiceBroker inner) => this.Inner = Requires.NotNull(inner);

		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
		{
			add
			{
				lock (this.syncObject)
				{
					if (this.availabilityChanged is null)
					{
						this.Inner.AvailabilityChanged += this.OnInnerAvailabilityChanged;
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
						this.Inner.AvailabilityChanged -= this.OnInnerAvailabilityChanged;
					}
				}
			}
		}

		protected IServiceBroker Inner { get; }

		public virtual ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			=> this.Inner.GetPipeAsync(serviceMoniker, options, cancellationToken);

		public virtual ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options = default, CancellationToken cancellationToken = default)
			where T : class
			=> this.Inner.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken);

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
