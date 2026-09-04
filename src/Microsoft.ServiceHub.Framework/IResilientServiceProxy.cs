// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes a stable client proxy that replaces its underlying service proxy when it becomes invalid.
/// </summary>
public interface IResilientServiceProxy : IDisposableObservable, INotifyDisposable, IDisposable, IAsyncDisposable
{
	/// <summary>
	/// Occurs when the proxy gains, loses, or replaces its backing service.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each change is reported as exactly one mutually exclusive <see cref="ResilientServiceProxyChangeKind"/>
	/// value. In particular, replacing one backing service with another raises only
	/// <see cref="ResilientServiceProxyChangeKind.Replaced"/>, not separate
	/// <see cref="ResilientServiceProxyChangeKind.Lost"/> and
	/// <see cref="ResilientServiceProxyChangeKind.Gained"/> events.
	/// </para>
	/// <para>
	/// A loss is reported after the current replacement attempt completes without acquiring a service.
	/// While that attempt is in progress, <see cref="IsAvailable"/> is <see langword="false"/>, but no
	/// transition has been reported yet because the outcome may be <see cref="ResilientServiceProxyChangeKind.Replaced"/>.
	/// </para>
	/// <para>
	/// Clients can use <see cref="ResilientServiceProxyChangeKind.Gained"/> and
	/// <see cref="ResilientServiceProxyChangeKind.Lost"/> to enable or suspend operations that require an
	/// available service. Clients can use <see cref="ResilientServiceProxyChangeKind.Replaced"/> to rebuild
	/// mutable state associated with the prior service generation. Contract event handlers and observer
	/// subscriptions are transferred to replacements automatically.
	/// </para>
	/// </remarks>
	event EventHandler<ResilientServiceProxyChangedEventArgs>? BackingServiceChanged;

	/// <summary>
	/// Gets a value indicating whether the proxy currently has a backing service.
	/// </summary>
	bool IsAvailable { get; }
}
