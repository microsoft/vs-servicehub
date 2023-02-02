// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.Utilities.ServiceBroker;

/// <summary>
/// Enumerates the possible sources of a brokered service.
/// </summary>
public enum ServiceSource
{
	/// <summary>
	/// The services are proffered from within this process.
	/// </summary>
	SameProcess,

	/// <summary>
	/// The services are proffered by local (and trusted) sources, outside this process.
	/// </summary>
	OtherProcessOnSameMachine,

	/// <summary>
	/// The services are proffered by a remote server (e.g. Live Share host) that is under the control of the same user account as the local one (the guest who is joining the session).
	/// </summary>
	TrustedServer,

	/// <summary>
	/// The services are proffered by a remote server (e.g. Live Share host) that is NOT under the control of the same user account as the local one (the guest who is joining the session).
	/// </summary>
	UntrustedServer,

	/// <summary>
	/// The services are proffered by a remote server that is under the control of the same user account as the local one
	/// using an exclusive connection (that isn't the traditional Live Share sharing session).
	/// For example a Codespace server.
	/// </summary>
	TrustedExclusiveServer,

	/// <summary>
	/// The services are proffered by a remote <em>client</em> under the control of the same user account as the local one.
	/// This is a special 1:1 relationship.
	/// For example the client of a Codespace.
	/// </summary>
	TrustedExclusiveClient,
}
