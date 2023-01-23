// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Extension methods to make multi-targeting require fewer <c>#if</c> regions.
/// </summary>
internal static class PolyfillExtensions
{
#if !NET5_0_OR_GREATER
	/// <summary>
	/// Disposes the stream.
	/// </summary>
	/// <param name="stream">The stream to be disposed.</param>
	/// <returns>A task.</returns>
	internal static ValueTask DisposeAsync(this Stream stream)
	{
		stream.Dispose();
		return default;
	}
#endif
}
