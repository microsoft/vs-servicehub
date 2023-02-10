import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { Map } from 'immutable'
import { RemoteServiceConnections } from '../constants'
import { IDisposable } from '../IDisposable'
import { IRemoteServiceBroker } from '../IRemoteServiceBroker'
import { IServiceBroker } from '../IServiceBroker'
import { RemoteServiceConnectionInfo } from '../RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from '../ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from '../ServiceBrokerClientMetadata'
import { ServiceMoniker } from '../ServiceMoniker'
import { ServiceRpcDescriptor } from '../ServiceRpcDescriptor'
import { ClientCredentialsPolicy } from './ClientCredentialsPolicy'
import { GlobalBrokeredServiceContainer } from './GlobalBrokeredServiceContainer'
import { ServiceAudience } from './ServiceAudience'
import { ServiceBrokerEmitter } from './ServiceBrokerEmitter'

/**
 * A filtered view into a brokered service container.
 */
export class View extends (EventEmitter as new () => ServiceBrokerEmitter) implements IServiceBroker, IRemoteServiceBroker {
	constructor(
		private readonly container: GlobalBrokeredServiceContainer,
		private readonly audience: ServiceAudience,
		private readonly clientCredentials: Map<string, string>,
		private readonly clientCredentialsPolicy: ClientCredentialsPolicy,
		private readonly clientCulture?: string,
		private readonly clientUICulture?: string
	) {
		super()
	}

	async getProxy<T extends object>(
		serviceDescriptor: ServiceRpcDescriptor,
		options?: ServiceActivationOptions,
		cancellationToken?: CancellationToken
	): Promise<(T & IDisposable) | null> {
		cancellationToken?.throwIfCancelled()
		options = this.applyOptionsFilter(options)
		const source = await this.container.getProfferingSource(serviceDescriptor.moniker, this.audience)
		if (source?.proffered) {
			return await source.proffered.getProxy<T>(serviceDescriptor, options, cancellationToken)
		}

		return null
	}

	async getPipe(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions,
		cancellationToken?: CancellationToken
	): Promise<NodeJS.ReadWriteStream | null> {
		cancellationToken?.throwIfCancelled()
		options = this.applyOptionsFilter(options)
		const source = await this.container.getProfferingSource(serviceMoniker, this.audience)
		if (source?.proffered) {
			return await source.proffered.getPipe(serviceMoniker, options, cancellationToken)
		}

		return null
	}

	handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken?: CancellationToken | undefined): Promise<void> {
		if (!RemoteServiceConnections.contains(clientMetadata.supportedConnections, RemoteServiceConnections.IpcPipe)) {
			throw new Error('We only support pipes.')
		}

		return Promise.resolve()
	}

	async requestServiceChannel(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<RemoteServiceConnectionInfo> {
		cancellationToken?.throwIfCancelled()
		options = this.applyOptionsFilter(options)
		const source = await this.container.getProfferingSource(serviceMoniker, this.audience)
		if (source?.proffered) {
			return source.proffered.requestServiceChannel(serviceMoniker, options, cancellationToken)
		}

		return Promise.resolve({})
	}

	cancelServiceRequest(serviceRequestId: string, cancellationToken?: CancellationToken | undefined): Promise<void> {
		// Try sending the cancellation to all remote sources since we don't know which one actually handled the request
		// that's being canceled. Checking if a request should be canceled by the broker should be relatively cheap and
		// since request ids are guids there's no risk of id collisions
		throw new Error('Not yet implemented.')
	}

	private applyOptionsFilter(options: ServiceActivationOptions | undefined) {
		const { ...localOptions } = options ?? {}
		if (this.clientCredentialsPolicy === ClientCredentialsPolicy.filterOverridesRequest || (localOptions.clientCredentials?.length ?? 0) === 0) {
			localOptions.clientCredentials = this.clientCredentials.toObject()
		}

		localOptions.clientCulture = this.clientCulture
		localOptions.clientUICulture = this.clientUICulture

		return localOptions
	}
}
