// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// An interface that offers notification after the implementing object is disposed.
/// </summary>
public interface INotifyDisposable : IDisposable
{
	/// <summary>
	/// Occurs when the object is disposed.
	/// </summary>
	/// <remarks>
	/// <para>If the object has already been disposed, an attempt to add a handler will result in
	/// the handler being invoked synchronously before the returning without retaining a reference.</para>
	/// <para>Once disposed, all references to handlers are removed.</para>
	/// </remarks>
	event EventHandler Disposed;
}
