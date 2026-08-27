// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes an underlying service proxy that has been invalidated.
/// </summary>
public sealed class ResilientServiceProxyInvalidatedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ResilientServiceProxyInvalidatedEventArgs"/> class.
	/// </summary>
	/// <param name="serviceMoniker">The moniker of the invalidated service.</param>
	/// <param name="generation">The generation number of the invalidated proxy.</param>
	public ResilientServiceProxyInvalidatedEventArgs(ServiceMoniker serviceMoniker, long generation)
	{
		Requires.NotNull(serviceMoniker);
		this.ServiceMoniker = serviceMoniker;
		this.Generation = generation;
	}

	/// <summary>
	/// Gets the moniker of the invalidated service.
	/// </summary>
	public ServiceMoniker ServiceMoniker { get; }

	/// <summary>
	/// Gets the generation number of the invalidated proxy.
	/// </summary>
	public long Generation { get; }
}
