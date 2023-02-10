export enum ServiceSource {
	/**
	 * The services are proffered from within this process.
	 */
	sameProcess,

	/**
	 * The services are proffered by local (and trusted) sources, outside this process.
	 */
	otherProcessOnSameMachine,

	/**
	 * The services are proffered by a remote server (e.g. Live Share host) that is under the control of the same user account as the local one (the guest who is joining the session).
	 */
	trustedServer,

	/**
	 * The services are proffered by a remote server (e.g. Live Share host) that is NOT under the control of the same user account as the local one (the guest who is joining the session).
	 */
	untrustedServer,
}
