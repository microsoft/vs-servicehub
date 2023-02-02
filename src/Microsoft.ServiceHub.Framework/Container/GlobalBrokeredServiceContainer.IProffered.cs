// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.ServiceHub.Framework;

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// An object that tracks a proffered service or set of services.
	/// </summary>
	protected interface IProffered : IServiceBroker, IRemoteServiceBroker, IDisposable
	{
		/// <summary>
		/// Gets an identifier for where the services are proffered from.
		/// </summary>
		ServiceSource Source { get; }

		/// <summary>
		/// Gets the set of monikers for the proffered services.
		/// </summary>
		ImmutableHashSet<ServiceMoniker> Monikers { get; }
	}
}
