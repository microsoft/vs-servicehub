export enum ServiceAudience {
	/**
	 * No flags. The service is not available to anyone.
	 * When used for a filtered view, it means apply no filters (all services are available).
	 */
	none = 0x0,
	/**
	 * Services are available for clients running in the same process.
	 * They will not be available from other processes (e.g. ServiceHub services).
	 * A brokered service that includes this flag may still be *indirectly* exposed to LiveShareGuest
	 * by way of another brokered service that is exposed to LiveShareGuest that is proffered from this process.
	 */
	process = 0x1,

	/**
	 * The service is available for clients that support this process (e.g. ServiceHub services). These always run on the same machine and user account.
	 * It does *not* include processes connected over Live Share or a Visual Studio Online Environment connection, even if these processes are running on the same machine.
	 * A brokered service that includes this flag may still be *indirectly* exposed to <see cref="LiveShareGuest"/>
	 * by way of another brokered service that is exposed to <see cref="LiveShareGuest"/> that is proffered from this machine.
	 */
	local = process | 0x2,

	/**
	 * When the service is running on a Live Share host it is available for Live Share guests,
	 * which may or may not be using the same user account as the host.
	 * @remarks
	 * Host services are available for remote Live Share clients running under *any* user account.
	 * Any necessary authorization checks are the responsibility of the service.
	 */
	liveShareGuest = 0x400,

	/**
	 * The service is available for local processes as well as clients of Visual Studio Online environments and all Live Share guests (including untrusted strangers).
	 * @remarks
	 * Host services are available for all clients (owner or guest), whether they are local, remote over Live Share or remote over an exclusive connection.
	 */
	allClientsIncludingGuests = local | liveShareGuest,
}
