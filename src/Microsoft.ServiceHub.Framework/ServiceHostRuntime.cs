// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// The set of recognized runtimes for service hosts.
/// </summary>
public enum ServiceHostRuntime
{
	/// <summary>
	/// No runtime (i.e. native code only).
	/// </summary>
	None,

	/// <summary>
	/// The .NET Framework.
	/// </summary>
	NETFramework,

	/// <summary>
	/// .NET Core.
	/// </summary>
	NETCore,

	/// <summary>
	/// Mono.
	/// </summary>
	Mono,

	/// <summary>
	/// Node.JS.
	/// </summary>
	NodeJS,
}
