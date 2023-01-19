// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.ServiceHub.Utility;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// A class containing extension methods for <see cref="Stream"/>.
/// </summary>
public static class StreamExtensions
{
	/// <summary>
	/// Attempts to get the handle of ServiceHub stream.
	/// </summary>
	/// <param name="stream">The stream to get the handle of.</param>
	/// <param name="handle">The handle of the stream if it exists, null otherweise.</param>
	/// <returns>True if the stream has a <see cref="SafePipeHandle"/>, false otherwise.</returns>
	public static bool TryGetHandle(this Stream? stream, [NotNullWhen(true)] out SafePipeHandle? handle)
	{
		return stream.TryGetHandleCore(out handle);
	}
}
