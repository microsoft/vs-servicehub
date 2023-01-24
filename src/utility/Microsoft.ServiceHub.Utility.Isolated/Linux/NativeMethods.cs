// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace Microsoft.ServiceHub.Framework.Linux;

/// <summary>
/// NativeMethods to be used on Linux platforms.
/// </summary>
internal static class NativeMethods
{
	/// <summary>
	/// The root user id.
	/// </summary>
	public const int RootUserId = 0;

	/// <summary>
	/// Get the real user ID of the calling process.
	/// </summary>
	/// <returns>the real user ID of the calling process.</returns>
	[DllImport("libc")]
	public static extern int getuid();
}
