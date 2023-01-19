// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Utility;

/// <summary>
/// Flags that can modify how an IPC channel connection is made.
/// </summary>
[Flags]
internal enum ChannelConnectionFlags
{
	/// <summary>
	/// No modifier flags.
	/// </summary>
	None = 0x0,

	/// <summary>
	/// Continuously retry or wait for the server to listen for and respond to connection requests
	/// until it is canceled.
	/// Without this flag, the connection will be attempted only once and immediately fail if the
	/// server is not online and responsive.
	/// </summary>
	WaitForServerToConnect = 0x1,
}
