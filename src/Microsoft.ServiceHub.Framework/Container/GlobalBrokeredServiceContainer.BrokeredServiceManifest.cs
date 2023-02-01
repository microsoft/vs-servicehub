// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Framework;
using Microsoft.ServiceHub.Framework.Services;
using Microsoft.VisualStudio.Shell.ServiceBroker;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// Exposes details about availability of locally proffered services for clients with a specific audience.
	/// </summary>
	private class BrokeredServiceManifest : IBrokeredServiceManifest
	{
		private readonly GlobalBrokeredServiceContainer container;
		private readonly ServiceAudience serviceAudience;

		internal BrokeredServiceManifest(GlobalBrokeredServiceContainer container, ServiceAudience serviceAudience)
		{
			this.container = container ?? throw new ArgumentNullException(nameof(container));
			this.serviceAudience = serviceAudience;
		}

		/// <inheritdoc />
		public ValueTask<IReadOnlyCollection<ServiceMoniker>> GetAvailableServicesAsync(CancellationToken cancellationToken)
		{
			ImmutableHashSet<ServiceMoniker>.Builder monikers = ImmutableHashSet.CreateBuilder<ServiceMoniker>();

			// Enumerate every registered service
			foreach (KeyValuePair<ServiceMoniker, ServiceRegistration> registration in this.container.registeredServices)
			{
				cancellationToken.ThrowIfCancellationRequested();

				// Filter to just those services that are exposed to the service audience that received this service.
				if (registration.Value.IsExposedTo(this.serviceAudience))
				{
					monikers.Add(registration.Key);
				}
			}

			return new ValueTask<IReadOnlyCollection<ServiceMoniker>>(monikers.ToImmutable());
		}

		/// <inheritdoc />
		public ValueTask<ImmutableSortedSet<Version?>> GetAvailableVersionsAsync(string serviceName, CancellationToken cancellationToken)
		{
			Requires.NotNull(serviceName, nameof(serviceName));

			ImmutableSortedSet<Version?>.Builder versions = ImmutableSortedSet.CreateBuilder<Version?>();

			// Enumerate every registered service looking for matching names.
			// We can't simply look up based on the key in the dictionary because
			// we want to find all the available versions.
			foreach (KeyValuePair<ServiceMoniker, ServiceRegistration> registration in this.container.registeredServices)
			{
				cancellationToken.ThrowIfCancellationRequested();
				ServiceMoniker registeredMoniker = registration.Key;

				if (serviceName == registeredMoniker.Name)
				{
					// Filter to just those services that are exposed to the service audience that received this service.
					if (registration.Value.IsExposedTo(this.serviceAudience))
					{
						versions.Add(registeredMoniker.Version);
					}
				}
			}

			return new ValueTask<ImmutableSortedSet<Version?>>(versions.ToImmutable());
		}
	}
}
