// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// Enumerates the possible handling result of a brokered service request.
	/// </summary>
	/// <devremarks>
	/// Although these enum values are each set to unique bits, it isn't a flags enum.
	/// They are unique bits to allow convenient packing as we internally track which result types we've already posted telemetry events for.
	/// </devremarks>
	protected enum RequestResult
	{
		/// <summary>
		/// The requested brokered service was activated, fulfilling the request.
		/// </summary>
		Fulfilled = 0x1,

		/// <summary>
		/// The request was declined for reasons other than the service not being found.
		/// This could be because authorization was denied, or the service factory returned <see langword="null" /> or threw an exception.
		/// </summary>
		Declined = 0x2,

		/// <summary>
		/// The request was declined because the service was not registered.
		/// </summary>
		DeclinedNotFound = 0x4,
	}
}
