// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Pipelines;
using Microsoft.ServiceHub.Framework;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// Services brokered services that are proffered via an in-proc <see cref="IServiceBroker"/>.
	/// </summary>
	[DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
	protected class ProfferedServiceBroker : IProffered
	{
		private readonly GlobalBrokeredServiceContainer container;
		private readonly IRemoteServiceBroker remoteServiceBrokerWrapper;

		internal ProfferedServiceBroker(GlobalBrokeredServiceContainer container, IServiceBroker serviceBroker, ServiceSource source, IReadOnlyCollection<ServiceMoniker> serviceMonikers)
		{
			this.container = container ?? throw new ArgumentNullException(nameof(container));
			this.ServiceBroker = serviceBroker ?? throw new ArgumentNullException(nameof(serviceBroker));
			this.Source = source;
			this.Monikers = serviceMonikers?.ToImmutableHashSet() ?? throw new ArgumentNullException(nameof(serviceMonikers));
			serviceBroker.AvailabilityChanged += this.OnAvailabilityChanged;
			this.remoteServiceBrokerWrapper = new RemoteServiceBrokerWrapper(this);
		}

		/// <inheritdoc/>
		public event EventHandler<BrokeredServicesChangedEventArgs>? AvailabilityChanged
		{
			add => this.ServiceBroker.AvailabilityChanged += value;
			remove => this.ServiceBroker.AvailabilityChanged -= value;
		}

		/// <inheritdoc/>
		public ImmutableHashSet<ServiceMoniker> Monikers { get; }

		/// <inheritdoc/>
		public ServiceSource Source { get; }

		private IServiceBroker ServiceBroker { get; }

		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private string DebuggerDisplay => $"{this.ServiceBroker} with {this.Monikers.Count} services including {this.Monikers.FirstOrDefault()}.";

		/// <inheritdoc/>
		public void Dispose()
		{
			this.ServiceBroker.AvailabilityChanged -= this.OnAvailabilityChanged;
			this.container.RemoveRegistrations(this);
		}

		/// <inheritdoc/>
		public ValueTask<IDuplexPipe?> GetPipeAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions options, CancellationToken cancellationToken)
		{
			return this.ServiceBroker.GetPipeAsync(serviceMoniker, options, cancellationToken);
		}

		/// <inheritdoc/>
		public ValueTask<T?> GetProxyAsync<T>(ServiceRpcDescriptor serviceDescriptor, ServiceActivationOptions options, CancellationToken cancellationToken)
			where T : class
		{
			return this.ServiceBroker.GetProxyAsync<T>(serviceDescriptor, options, cancellationToken);
		}

		/// <inheritdoc />
		Task IRemoteServiceBroker.HandshakeAsync(ServiceBrokerClientMetadata clientMetadata, CancellationToken cancellationToken)
		{
			return this.remoteServiceBrokerWrapper.HandshakeAsync(clientMetadata, cancellationToken);
		}

		/// <inheritdoc />
		Task<RemoteServiceConnectionInfo> IRemoteServiceBroker.RequestServiceChannelAsync(ServiceMoniker serviceMoniker, ServiceActivationOptions serviceActivationOptions, CancellationToken cancellationToken)
		{
			return this.remoteServiceBrokerWrapper.RequestServiceChannelAsync(serviceMoniker, serviceActivationOptions, cancellationToken);
		}

		/// <inheritdoc />
		Task IRemoteServiceBroker.CancelServiceRequestAsync(Guid serviceRequestId)
		{
			return this.remoteServiceBrokerWrapper.CancelServiceRequestAsync(serviceRequestId);
		}

		private void OnAvailabilityChanged(object? sender, BrokeredServicesChangedEventArgs args)
		{
			this.container.OnAvailabilityChanged(null, this, args.OtherServicesImpacted ? this.Monikers : args.ImpactedServices);
		}
	}
}
