// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.ServiceHub.Framework;

/// <summary>
/// Identifies the connections that are supported for a remote service connection.
/// </summary>
[Flags]
public enum RemoteServiceConnections
{
	/// <summary>
	/// No connection types.
	/// </summary>
	None = 0x0,

	/// <summary>
	/// Supports multiplexing channels across the existing stream shared between the remote service broker and its client.
	/// </summary>
	Multiplexing = 0x1,

	/// <summary>
	/// Supports opening an IPC pipe between service and its client.
	/// </summary>
	IpcPipe = 0x2,

	/// <summary>
	/// Supports sharing assembly path and full name of the type that represents the service (e.g. its entrypoint and RPC target).
	/// </summary>
	ClrActivation = 0x4,
}
