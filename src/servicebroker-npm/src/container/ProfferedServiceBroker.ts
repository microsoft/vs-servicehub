import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { BrokeredServicesChangedArgs } from '../BrokeredServicesChangedArgs'
import { IDisposable } from '../IDisposable'
import { IRemoteServiceBroker } from '../IRemoteServiceBroker'
import { IServiceBroker } from '../IServiceBroker'
import { RemoteServiceConnectionInfo } from '../RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from '../ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from '../ServiceBrokerClientMetadata'
import { ServiceMoniker } from '../ServiceMoniker'
import { ServiceRpcDescriptor } from '../ServiceRpcDescriptor'
import { GlobalBrokeredServiceContainer } from './GlobalBrokeredServiceContainer'
import { IProffered } from './IProffered'
import { RemoteServiceBrokerWrapper } from './RemoteServiceBrokerWrapper'
import { ServiceBrokerEmitter } from './ServiceBrokerEmitter'
import { ServiceMonikerValue } from './ServiceMonikerValue'
import { ServiceSource } from './ServiceSource'

export class ProfferedServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IProffered {
	readonly monikers: readonly ServiceMonikerValue[]
	private readonly remoteServiceBrokerWrapper: IRemoteServiceBroker
	constructor(
		private readonly container: GlobalBrokeredServiceContainer,
		private readonly serviceBroker: IServiceBroker,
		readonly source: ServiceSource,
		monikers: readonly ServiceMoniker[]
	) {
		super()
		this.monikers = monikers.map(ServiceMonikerValue.from)
		this.serviceBroker.on('availabilityChanged', this.onAvailabilityChanged)
		this.remoteServiceBrokerWrapper = new RemoteServiceBrokerWrapper(this)
	}

	getProxy<T extends object>(
		serviceDescriptor: ServiceRpcDescriptor,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<(T & IDisposable) | null> {
		return this.serviceBroker.getProxy<T>(serviceDescriptor, options, cancellationToken)
	}

	getPipe(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<NodeJS.ReadWriteStream | null> {
		return this.serviceBroker.getPipe(serviceMoniker, options, cancellationToken)
	}

	handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken?: CancellationToken | undefined): Promise<void> {
		return this.remoteServiceBrokerWrapper.handshake(clientMetadata, cancellationToken)
	}
	requestServiceChannel(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<RemoteServiceConnectionInfo> {
		return this.remoteServiceBrokerWrapper.requestServiceChannel(serviceMoniker, options, cancellationToken)
	}
	cancelServiceRequest(serviceRequestId: string, cancellationToken?: CancellationToken | undefined): Promise<void> {
		return this.remoteServiceBrokerWrapper.cancelServiceRequest(serviceRequestId, cancellationToken)
	}

	dispose(): void {
		this.serviceBroker.off('availabilityChanged', this.onAvailabilityChanged)
		this.container.removeRegistrations(this)
	}

	private onAvailabilityChanged(args: BrokeredServicesChangedArgs) {
		this.container.onAvailabilityChanged(null, this, args.impactedServices)
		this.emit('availabilityChanged', args)
	}
}
