import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { ServiceActivationOptions } from './ServiceActivationOptions'
import { ServiceMoniker } from './ServiceMoniker'
import { ServiceRpcDescriptor } from './ServiceRpcDescriptor'
import { IDisposable } from './IDisposable'
import StrictEventEmitter from 'strict-event-emitter-types'
import { BrokeredServicesChangedArgs } from './BrokeredServicesChangedArgs'

/**
 * Describes the events that can be fired from {@linkcode IServiceBroker} or {@linkcode IRemoteServiceBroker}
 */
export interface ServiceBrokerEvents {
	availabilityChanged: (args: BrokeredServicesChangedArgs) => void
}

/**
 * The {@linkcode StrictEventEmitter} extended by {@linkcode IServiceBroker} and {@linkcode IRemoteServiceBroker}
 */
export type ServiceBrokerEmitter = StrictEventEmitter<EventEmitter, ServiceBrokerEvents>

/**
 * A service broker that can provide or activate services.
 * Emits an event when the availability of a service changes.
 */
export interface IServiceBroker extends ServiceBrokerEmitter {
	/**
	 * Gets a proxy to the requested service
	 * @param serviceDescriptor The service being requested
	 * @param options Activation options for that service
	 * @param cancellationToken A cancellation token
	 */
	getProxy<T extends object>(
		serviceDescriptor: ServiceRpcDescriptor,
		options?: ServiceActivationOptions,
		cancellationToken?: CancellationToken
	): Promise<(T & IDisposable) | null>

	/**
	 * Gets a pipe to communicate with a requested service
	 * @param serviceMoniker The service being requested
	 * @param options Activation options for that service
	 * @param cancellationToken A cancellation token
	 */
	getPipe(serviceMoniker: ServiceMoniker, options?: ServiceActivationOptions, cancellationToken?: CancellationToken): Promise<NodeJS.ReadWriteStream | null>
}
