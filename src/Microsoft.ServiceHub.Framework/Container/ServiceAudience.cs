// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.ServiceHub.Framework;

namespace Microsoft.VisualStudio.Shell.ServiceBroker;

/// <summary>
/// Identifies various audiences that may want or need to access a service.
/// When used to register a service (e.g. ProvideBrokeredServiceAttribute)
/// it determines whether that service can be accessed locally, remotely and/or by 3rd parties.
/// </summary>
/// <remarks>
/// This enum may also be used as a filter when constructing an <see cref="IServiceBroker"/>
/// where each flag reduces the set of services available as only services that specify every flag
/// in the filter are available.
/// </remarks>
[Flags]
public enum ServiceAudience
{
	/// <summary>
	/// No flags. The service is not available to anyone.
	/// When used for a filtered view, it means apply no filters (all services are available).
	/// </summary>
	None = 0x0,

	/// <summary>
	/// Services are available for clients running in the same process (or <see cref="AppDomain" /> on the .NET Framework).
	/// They will not be available from other processes (e.g. ServiceHub services).
	/// A brokered service that includes this flag may still be *indirectly* exposed to <see cref="LiveShareGuest"/>
	/// by way of another brokered service that is exposed to <see cref="LiveShareGuest"/> that is proffered from this process.
	/// </summary>
	Process = 0x1,

	/// <summary>
	/// The service is available for clients that support this process (e.g. ServiceHub services). These always run on the same machine and user account.
	/// It does *not* include processes connected over Live Share or a Visual Studio Online Environment connection, even if these processes are running on the same machine.
	/// A brokered service that includes this flag may still be *indirectly* exposed to <see cref="LiveShareGuest"/>
	/// by way of another brokered service that is exposed to <see cref="LiveShareGuest"/> that is proffered from this machine.
	/// </summary>
	Local = Process | 0x2,

	/// <summary>
	/// When the service is running on an Visual Studio Online environment it is available to the client.
	/// </summary>
	/// <remarks>
	/// Host services are available for the *one* client running on any machine that is connected remotely using the exclusive
	/// owner connection (not the traditional Live Share sharing session).
	/// Such a connection is *always* owned by the same owner as the server and thus is considered trusted.
	/// </remarks>
	RemoteExclusiveClient = 0x100,

	/// <summary>
	/// When the service is running on a Live Share host it is available for Live Share guests,
	/// which may or may not be using the same user account as the host.
	/// </summary>
	/// <remarks>
	/// Host services are available for remote Live Share clients running under *any* user account.
	/// Any necessary authorization checks are the responsibility of the service.
	/// </remarks>
	LiveShareGuest = 0x400,

	/// <summary>
	/// When the service is running on a client of an Visual Studio Online environment, it is available to the server.
	/// </summary>
	/// <remarks>
	/// Client services are proffered to a server over an exclusive connection that is always operated by the owner at both ends
	/// (and is not the traditional Live Share sharing session).
	/// A server never has more than one of these connections concurrently.
	/// </remarks>
	RemoteExclusiveServer = 0x800,

	/// <summary>
	/// The service is available for local processes as well as clients of Visual Studio Online environments and all Live Share guests (including untrusted strangers).
	/// </summary>
	/// <remarks>
	/// Host services are available for all clients (owner or guest), whether they are local, remote over Live Share or remote over an exclusive connection.
	/// </remarks>
	AllClientsIncludingGuests = Local | RemoteExclusiveClient | LiveShareGuest,

	/// <summary>
	/// The service is considered part of the public SDK,
	/// and thus is available to 3rd party clients that are only privileged to access public SDK services.
	/// This flag should only be specified for public services that have stable APIs.
	/// This flag must be combined with other flags to indicate which local and/or remote clients are allowed to request this service.
	/// </summary>
	PublicSdk = 0x10000000,
}
