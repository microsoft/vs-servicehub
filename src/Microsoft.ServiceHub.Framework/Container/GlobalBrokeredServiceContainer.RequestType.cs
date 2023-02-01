// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

public abstract partial class GlobalBrokeredServiceContainer
{
	/// <summary>
	/// Enumerates the types of brokered service requests that can be made.
	/// </summary>
	protected enum RequestType
	{
		/// <summary>
		/// The request was for a proxy to a brokered service.
		/// </summary>
		Proxy,

		/// <summary>
		/// The request was for a pipe to a brokered service.
		/// </summary>
		Pipe,
	}
}
