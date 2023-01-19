import { RemoteServiceConnections } from './constants'

/**
 * Describes the capabilities of a [IRemoteServiceBroker](#IRemoteServiceBroker)
 */
export interface ServiceBrokerClientMetadata {
	/**
	 * The connection types supported by the service broker.
	 * This may be a comma-delimited list of the enum names or the bitwise-OR of their values.
	 */
	supportedConnections: RemoteServiceConnections | string
}
