// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// The recognized operating systems that can act as service hosts.
/// </summary>
public enum ServiceHostOperatingSystem
{
	/// <summary>
	/// The Windows operating system.
	/// </summary>
	Windows,

	/// <summary>
	/// The Linux operating system.
	/// </summary>
	Linux,

	/// <summary>
	/// The Mac OSX operating system.
	/// </summary>
	OSX,
}
