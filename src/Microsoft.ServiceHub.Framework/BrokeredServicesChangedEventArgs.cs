// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Newtonsoft.Json;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes changes to brokered service availability as raised by the <see cref="IServiceBroker.AvailabilityChanged"/> event.
/// </summary>
public class BrokeredServicesChangedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BrokeredServicesChangedEventArgs"/> class
	/// with an exhaustive set of impacted services.
	/// </summary>
	/// <param name="impactedServices">The set of services that are impacted by the change.</param>
	public BrokeredServicesChangedEventArgs(IImmutableSet<ServiceMoniker> impactedServices)
		: this(impactedServices, otherServicesImpacted: false)
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BrokeredServicesChangedEventArgs"/> class.
	/// </summary>
	/// <param name="impactedServices">The set of services that are impacted by the change.</param>
	/// <param name="otherServicesImpacted">A value indicating whether other services not included in <see cref="ImpactedServices"/> may also be impacted.</param>
	[JsonConstructor]
	public BrokeredServicesChangedEventArgs(IImmutableSet<ServiceMoniker> impactedServices, bool otherServicesImpacted)
	{
		Requires.NotNull(impactedServices, nameof(impactedServices));

		this.ImpactedServices = impactedServices;
		this.OtherServicesImpacted = otherServicesImpacted;
	}

	/// <summary>
	/// Gets the set of services that are impacted by the change.
	/// </summary>
	/// <remarks>
	/// Services in this set may have been added, removed, or proffered by a different <see cref="IServiceBroker"/>
	/// such that a service's implementation or location has changed.
	/// </remarks>
	public IImmutableSet<ServiceMoniker> ImpactedServices { get; }

	/// <summary>
	/// Gets a value indicating whether other services not included in <see cref="ImpactedServices"/>
	/// may also be impacted.
	/// </summary>
	/// <remarks>
	/// This may be true when an <see cref="IServiceBroker"/> is proffered, changed, or removed without exhaustively enumerating the services it may offer.
	/// </remarks>
	public bool OtherServicesImpacted { get; }
}
