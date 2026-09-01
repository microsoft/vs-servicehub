// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// An object that tracks a proffered service or set of services, with a unique identity.
	/// </summary>
	protected interface IProfferedWithId : IProffered
	{
		/// <summary>
		/// Gets a unique ID associated with this particular instance.
		/// </summary>
		Guid Id { get; }
	}
}
