// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Describes important attributes of a service host that are often required to assess compatibility with a service.
/// </summary>
public struct ServiceHostInformation
{
	/// <summary>
	/// Gets or sets the operating system the service host is running on.
	/// </summary>
	public ServiceHostOperatingSystem? OperatingSystem { get; set; }

	/// <summary>
	/// Gets or sets the version of the operating system the service host is running on.
	/// </summary>
	[JsonConverter(typeof(VersionConverter))]
	public Version? OperatingSystemVersion { get; set; }

	/// <summary>
	/// Gets or sets the architecture of the service host process.
	/// </summary>
	public Architecture? ProcessArchitecture { get; set; }

	/// <summary>
	/// Gets or sets the runtime offered by the service host.
	/// </summary>
	public ServiceHostRuntime? Runtime { get; set; }

	/// <summary>
	/// Gets or sets the version of the runtime, if applicable.
	/// </summary>
	[JsonConverter(typeof(VersionConverter))]
	public Version? RuntimeVersion { get; set; }
}
