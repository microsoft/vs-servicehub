import CancellationToken from 'cancellationtoken'
import EventEmitter from 'events'
import { IRemoteServiceBroker } from '../../src/IRemoteServiceBroker'
import { ServiceBrokerEmitter } from '../../src/IServiceBroker'
import { RemoteServiceConnectionInfo } from '../../src/RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from '../../src/ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from '../../src/ServiceBrokerClientMetadata'
import { ServiceMoniker } from '../../src/ServiceMoniker'

export class EmptyRemoteServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IRemoteServiceBroker {
	public handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken: CancellationToken): Promise<void> {
		// no-op
		return Promise.resolve()
	}

	public requestServiceChannel(
		moniker: ServiceMoniker,
		options: ServiceActivationOptions,
		cancellationToken: CancellationToken
	): Promise<RemoteServiceConnectionInfo> {
		return Promise.resolve({})
	}

	public cancelServiceRequest(serviceRequestId: string): Promise<void> {
		// no-op
		return Promise.resolve()
	}

	public dispose(): void {
		// no-op
	}
}
