/**
 * Information about how to connect to a remote service
 */
export interface RemoteServiceConnectionInfo {
	/**
	 * The ID assigned to the service request that this instance is in response to.
	 * @remarks
	 * This value is useful when canceling this service request without connecting to it.
	 * If null, no resources are allocated for this service prior to the client connecting to it,
	 * and thus no resources need to be released if the client decides not to connect.
	 */
	requestId?: string

	/**
	 * The id of the multiplexing channel that is open for connection
	 */
	multiplexingChannelId?: number

	/**
	 * The name of the pipe that is open for connection.
	 */
	pipeName?: string
}

export namespace RemoteServiceConnectionInfo {
	/**
	 * Indicates if there exists remote service connection information
	 * @param info The RemoteServiceConnectionInfo to look into.
	 */
	export function isEmpty(info: RemoteServiceConnectionInfo): boolean {
		return !info.multiplexingChannelId && !info.requestId && !info.pipeName
	}

	/**
	 * Checks if a remote service connection is of one of the provided types
	 * @param info The RemoteServiceConnectionInfo to look into.
	 * @param connections The connections to check against
	 */
	export function isOneOf(info: RemoteServiceConnectionInfo, connections: string): boolean {
		if (info.multiplexingChannelId) {
			return connections.indexOf('multiplexing') > -1
		}

		return false
	}
}
