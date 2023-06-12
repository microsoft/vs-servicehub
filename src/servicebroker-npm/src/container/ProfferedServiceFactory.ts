import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { FullDuplexStream } from 'nerdbank-streams'
import { IDisposable } from '../IDisposable'
import { IRemoteServiceBroker } from '../IRemoteServiceBroker'
import { IServiceBroker } from '../IServiceBroker'
import { RemoteServiceConnectionInfo } from '../RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from '../ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from '../ServiceBrokerClientMetadata'
import { ServiceMoniker } from '../ServiceMoniker'
import { ServiceRpcDescriptor } from '../ServiceRpcDescriptor'
import { GlobalBrokeredServiceContainer } from './GlobalBrokeredServiceContainer'
import { BrokeredServiceFactory } from './IBrokeredServiceContainer'
import { IProffered } from './IProffered'
import { RemoteServiceBrokerWrapper } from './RemoteServiceBrokerWrapper'
import { ServiceBrokerEmitter } from './ServiceBrokerEmitter'
import { ServiceMonikerValue } from './ServiceMonikerValue'
import { ServiceSource } from './ServiceSource'

export class ProfferedServiceFactory extends (EventEmitter as new () => ServiceBrokerEmitter) implements IProffered {
	source = ServiceSource.sameProcess
	monikers: readonly ServiceMonikerValue[]
	private readonly remoteServiceBrokerWrapper: IRemoteServiceBroker

	constructor(
		private readonly container: GlobalBrokeredServiceContainer,
		private readonly descriptor: ServiceRpcDescriptor,
		private readonly factory: BrokeredServiceFactory
	) {
		super()
		this.monikers = Object.freeze([new ServiceMonikerValue(descriptor.moniker)])
		this.remoteServiceBrokerWrapper = new RemoteServiceBrokerWrapper(this)
	}

	async getProxy<T extends object>(
		serviceDescriptor: ServiceRpcDescriptor,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<(T & IDisposable) | null> {
		cancellationToken?.throwIfCancelled()

		const serviceBroker = this.container.getSecureServiceBroker(options)
		const service = (await this.invokeFactory(
			serviceBroker,
			serviceDescriptor.moniker,
			options ?? {},
			cancellationToken ?? CancellationToken.CONTINUE
		)) as any
		if (!service) {
			return null
		}

		switch (typeof service.dispose) {
			case 'undefined':
				service.dispose = function () {}
				break
			case 'function':
				break
			default:
				throw new Error(`Service has a dispose property of type ${typeof service.dispose} instead of a function.`)
		}

		return service as T & IDisposable
	}
	async getPipe(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<NodeJS.ReadWriteStream | null> {
		cancellationToken?.throwIfCancelled()

		const pipePair = FullDuplexStream.CreatePair()
		const serviceBroker = this.container.getSecureServiceBroker(options)
		const connection = this.descriptor.constructRpcConnection(pipePair.first)

		// TODO: consider mxstream support
		// TODO: add missing ClientRpcTarget support

		const service = (await this.invokeFactory(serviceBroker, serviceMoniker, options ?? {}, cancellationToken ?? CancellationToken.CONTINUE)) as {
			dispose?: () => void
		}
		try {
			if (service) {
				connection.addLocalRpcTarget(service)
				connection.startListening()
				return pipePair.second
			} else {
				connection.dispose()
				return null
			}
		} catch (e) {
			if (typeof service?.dispose === 'function') {
				service.dispose()
			}

			throw e
		}
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
		this.container.removeRegistrations(this)
	}

	private invokeFactory(serviceBroker: IServiceBroker, moniker: ServiceMoniker, options: ServiceActivationOptions, cancellationToken: CancellationToken) {
		const allowGuests = this.container.getServiceRegistration(ServiceMonikerValue.from(moniker))?.registration.allowGuestClients === true

		return this.factory(moniker, options, serviceBroker, cancellationToken)
	}
}
