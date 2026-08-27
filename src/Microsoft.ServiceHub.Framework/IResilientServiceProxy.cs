// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes a stable client proxy that replaces its underlying service proxy when it becomes invalid.
/// </summary>
public interface IResilientServiceProxy : IDisposableObservable, INotifyDisposable, IDisposable, IAsyncDisposable
{
	/// <summary>
	/// Occurs when the proxy gains or loses a backing service.
	/// </summary>
	/// <remarks>
	/// Inspect <see cref="IsAvailable"/> when handling this event to determine the new state.
	/// </remarks>
	event EventHandler? AvailabilityChanged;

	/// <summary>
	/// Occurs when the underlying service proxy has been invalidated.
	/// </summary>
	/// <remarks>
	/// Contract event handlers are automatically applied to the replacement proxy.
	/// Clients should use this event to rebuild any other mutable state held by the service instance.
	/// </remarks>
	event EventHandler<ResilientServiceProxyInvalidatedEventArgs>? Invalidated;

	/// <summary>
	/// Gets a value indicating whether the proxy currently has a backing service.
	/// </summary>
	bool IsAvailable { get; }
}
