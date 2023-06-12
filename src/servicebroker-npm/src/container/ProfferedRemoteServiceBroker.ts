import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { MultiplexingStream } from 'nerdbank-streams'
import { BrokeredServicesChangedArgs } from '../BrokeredServicesChangedArgs'
import { IDisposable } from '../IDisposable'
import { IRemoteServiceBroker } from '../IRemoteServiceBroker'
import { IServiceBroker } from '../IServiceBroker'
import { RemoteServiceBroker } from '../RemoteServiceBroker'
import { RemoteServiceConnectionInfo } from '../RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from '../ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from '../ServiceBrokerClientMetadata'
import { ServiceMoniker } from '../ServiceMoniker'
import { ServiceRpcDescriptor } from '../ServiceRpcDescriptor'
import { GlobalBrokeredServiceContainer } from './GlobalBrokeredServiceContainer'
import { IProffered } from './IProffered'
import { ServiceBrokerEmitter } from './ServiceBrokerEmitter'
import { ServiceMonikerValue } from './ServiceMonikerValue'
import { ServiceSource } from './ServiceSource'

export class ProfferedRemoteServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IProffered {
	private readonly serviceBroker: Promise<IServiceBroker>
	readonly monikers: readonly ServiceMonikerValue[]
	constructor(
		private readonly container: GlobalBrokeredServiceContainer,
		private readonly remoteServiceBroker: IRemoteServiceBroker,
		readonly multiplexingStream: MultiplexingStream | null,
		readonly source: ServiceSource,
		monikers: readonly ServiceMoniker[]
	) {
		super()
		this.monikers = monikers.map(ServiceMonikerValue.from)
		this.serviceBroker = new Promise<IServiceBroker>(async (resolve, reject) => {
			try {
				const serviceBroker = multiplexingStream
					? await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(remoteServiceBroker, multiplexingStream)
					: await RemoteServiceBroker.connectToRemoteServiceBroker(remoteServiceBroker)
				resolve(serviceBroker)
			} catch (e) {
				reject(e)
			}
		})

		this.remoteServiceBroker.on('availabilityChanged', this.onAvailabilityChanged)
	}

	async getProxy<T extends object>(
		serviceDescriptor: ServiceRpcDescriptor,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<(T & IDisposable) | null> {
		const sb = await this.serviceBroker
		return await sb.getProxy<T>(serviceDescriptor, options, cancellationToken)
	}

	async getPipe(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<NodeJS.ReadWriteStream | null> {
		const sb = await this.serviceBroker
		return await sb.getPipe(serviceMoniker, options, cancellationToken)
	}

	handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken?: CancellationToken | undefined): Promise<void> {
		return this.remoteServiceBroker.handshake(clientMetadata, cancellationToken)
	}

	requestServiceChannel(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<RemoteServiceConnectionInfo> {
		return this.remoteServiceBroker.requestServiceChannel(serviceMoniker, options, cancellationToken)
	}

	cancelServiceRequest(serviceRequestId: string, cancellationToken?: CancellationToken | undefined): Promise<void> {
		return this.remoteServiceBroker.cancelServiceRequest(serviceRequestId, cancellationToken)
	}

	dispose(): void {
		this.remoteServiceBroker.off('availabilityChanged', this.onAvailabilityChanged)
		this.container.removeRegistrations(this)
	}

	private onAvailabilityChanged(args: BrokeredServicesChangedArgs) {
		this.container.onAvailabilityChanged(null, this, args.impactedServices)
		this.emit('availabilityChanged', args)
	}
}
