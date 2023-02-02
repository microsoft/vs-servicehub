// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Microsoft.VisualStudio.Threading;
using Nerdbank.Streams;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// Services a brokered service that is proffered via a <see cref="IRemoteServiceBroker"/>.
	/// </summary>
	[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
	protected class ProfferedRemoteServiceBroker : IProffered
	{
		private readonly GlobalBrokeredServiceContainer container;
		private readonly AsyncLazy<IServiceBroker> serviceBroker;

		internal ProfferedRemoteServiceBroker(GlobalBrokeredServiceContainer container, IRemoteServiceBroker remoteServiceBroker, MultiplexingStream? multiplexingStream, ServiceSource source, IReadOnlyCollection<ServiceMoniker> serviceMonikers)
		{
			this.container = container ?? throw new ArgumentNullException(nameof(container));
			this.RemoteServiceBroker = remoteServiceBroker ?? throw new ArgumentNullException(nameof(remoteServiceBroker));
			this.Source = source;
			this.Monikers = serviceMonikers?.ToImmutableHashSet() ?? throw new ArgumentNullException(nameof(serviceMonikers));

			this.RemoteServiceBroker.AvailabilityChanged += this.OnAvailabilityChanged;

			this.serviceBroker = new AsyncLazy<IServiceBroker>(
				async () =>
				{
					return multiplexingStream is not null
						? await Microsoft.ServiceHub.Framework.RemoteServiceBroker.ConnectToMultiplexingServerAsync(remoteServiceBroker, multiplexingStream).ConfigureAwait(false)
						: await Microsoft.ServiceHub.Framework.RemoteServiceBroker.ConnectToServerAsync(remoteServiceBroker).ConfigureAwait(false);
				},
				this.container.joinableTaskFactory);
		}

		/// <inheritdoc/>
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
		{
			add => this.RemoteServiceBroker.AvailabilityChanged += value;
			remove => this.RemoteServiceBroker.AvailabilityChanged -= value;
		}

		/// <inheritdoc/>
		public ImmutableHashSet<ServiceMoniker> Monikers { get; }

		/// <inheritdoc/>
		public ServiceSource Source { get; }

		private IRemoteServiceBroker RemoteServiceBroker { get; }

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string DebuggerDisplay => $"{this.RemoteServiceBroker} with {this.Monikers.Count} services including {this.Monikers.FirstOrDefault()}.";

		/// <inheritdoc/>
		public void Dispose()
		{
			this.RemoteServiceBroker.AvailabilityChanged -= this.OnAvailabilityChanged;
			this.container.RemoveRegistrations(this);
		}

		/// <inheritdoc/>
		public async ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			IServiceBroker broker = await this.serviceBroker.GetValueAsync().ConfigureAwait(false);
			return await broker.GetPipeAsync(serviceMoniker, options, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
			where T : class
		{
			IServiceBroker broker = await this.serviceBroker.GetValueAsync().ConfigureAwait(false);
			GC.KeepAlive(typeof(ValueTask<T>)); // workaround CLR bug https://dev.azure.com/devdiv/DevDiv/_workitems/edit/1358442
			return await broker.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		Task IRemoteServiceBroker.HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken)
		{
			return this.RemoteServiceBroker.HandshakeAsync(clientMetadata, cancellationToken);
		}

		/// <inheritdoc />
		Task<RemoteServiceConnectionInfo> IRemoteServiceBroker.RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken)
		{
			return this.RemoteServiceBroker.RequestServiceChannelAsync(serviceMoniker, serviceActivationOptions, cancellationToken);
		}

		/// <inheritdoc />
		Task IRemoteServiceBroker.CancelServiceRequestAsync(Guid serviceRequestId)
		{
			return this.RemoteServiceBroker.CancelServiceRequestAsync(serviceRequestId);
		}

		private void OnAvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs args)
		{
			this.container.OnAvailabilityChanged(null, this, args.ImpactedServices);
		}
	}
}
