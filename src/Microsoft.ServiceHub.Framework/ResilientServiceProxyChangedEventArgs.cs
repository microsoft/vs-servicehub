// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes a change to the backing service of a resilient proxy.
/// </summary>
public sealed class ResilientServiceProxyChangedEventArgs : EventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ResilientServiceProxyChangedEventArgs"/> class.
	/// </summary>
	/// <param name="serviceMoniker">The service moniker.</param>
	/// <param name="previousGeneration">The prior generation, if any.</param>
	/// <param name="currentGeneration">The current generation, if any.</param>
	internal ResilientServiceProxyChangedEventArgs(ServiceMoniker serviceMoniker, long? previousGeneration, long? currentGeneration)
	{
		Requires.NotNull(serviceMoniker);
		Assumes.True(previousGeneration.HasValue || currentGeneration.HasValue);
		this.ServiceMoniker = serviceMoniker;
		this.PreviousGeneration = previousGeneration;
		this.CurrentGeneration = currentGeneration;
		this.ChangeKind = previousGeneration.HasValue
			? currentGeneration.HasValue ? ResilientServiceProxyChangeKind.Replaced : ResilientServiceProxyChangeKind.Lost
			: ResilientServiceProxyChangeKind.Gained;
	}

	/// <summary>
	/// Gets the service moniker.
	/// </summary>
	public ServiceMoniker ServiceMoniker { get; }

	/// <summary>
	/// Gets how the backing service changed.
	/// </summary>
	public ResilientServiceProxyChangeKind ChangeKind { get; }

	/// <summary>
	/// Gets the prior backing service generation, or <see langword="null"/> when <see cref="ChangeKind"/> is <see cref="ResilientServiceProxyChangeKind.Gained"/>.
	/// </summary>
	public long? PreviousGeneration { get; }

	/// <summary>
	/// Gets the current backing service generation, or <see langword="null"/> when <see cref="ChangeKind"/> is <see cref="ResilientServiceProxyChangeKind.Lost"/>.
	/// </summary>
	public long? CurrentGeneration { get; }
}
