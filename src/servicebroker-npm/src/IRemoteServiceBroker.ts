import CancellationToken from 'cancellationtoken'
import { RemoteServiceConnectionInfo } from './RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from './ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from './ServiceBrokerClientMetadata'
import { ServiceMoniker } from './ServiceMoniker'
import { ServiceBrokerEmitter } from './IServiceBroker'

/**
 * A service broker that can proffer services remotely.
 * Emits event when the availability of a service has changed.
 */
export interface IRemoteServiceBroker extends ServiceBrokerEmitter {
	/**
	 * Establishes a connection with a client
	 * @param clientMetadata Information about the client's capabilities
	 * @param cancellationToken A cancellation token
	 */
	handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken?: CancellationToken): Promise<void>

	/**
	 * Requests information about how to connect to the given service
	 * @param serviceMoniker The moniker for the service being requested
	 * @param options The activation options for the service
	 * @param cancellationToken A cancellation token
	 * @returns An object that describes supported service connections. Never null or undefined, but may be empty if the requested service is not available.
	 */
	requestServiceChannel(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions,
		cancellationToken?: CancellationToken
	): Promise<RemoteServiceConnectionInfo>

	/**
	 * Cancels a request for a service
	 * @param serviceRequestId The GUID of the service request to cancel
	 * @param cancellationToken A cancellation token
	 */
	cancelServiceRequest(serviceRequestId: string, cancellationToken?: CancellationToken): Promise<void>
}
