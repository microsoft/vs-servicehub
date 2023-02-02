// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// The IDs used for events logged via <see cref="TraceSource.TraceEvent(TraceEventType, int)"/>.
	/// </summary>
	protected enum TraceEvents
	{
		/// <summary>
		/// Indicates brokered services have been registered.
		/// </summary>
		Registered,

		/// <summary>
		/// Indicates brokered services have been proffered.
		/// </summary>
		Proffered,

		/// <summary>
		/// Indicates a brokered service has been requested.
		/// </summary>
		Request,

		/// <summary>
		/// Indicates that the container is activating a host of a brokered service so that it may proffer a registered brokered service.
		/// </summary>
		LoadPackage,

		/// <summary>
		/// Indicates that a handler of some brokered service event threw an unhandled exception.
		/// </summary>
		EventHandlerFaulted,
	}
}
