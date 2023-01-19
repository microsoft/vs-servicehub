// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Invokes an <see cref="Action"/> delegate upon disposal.
/// </summary>
internal class DisposeAction : IDisposable
{
	private readonly Action action;

	/// <summary>
	/// Initializes a new instance of the <see cref="DisposeAction"/> class.
	/// </summary>
	/// <param name="action">The delegate to invoke upon disposal.</param>
	internal DisposeAction(Action action)
	{
		this.action = action;
	}

	/// <inheritdoc/>
	public void Dispose() => this.action();
}
