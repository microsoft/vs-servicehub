import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { IRemoteServiceBroker } from '../../src/IRemoteServiceBroker'
import { RemoteServiceConnectionInfo } from '../../src/RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from '../../src/ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from '../../src/ServiceBrokerClientMetadata'
import { ServiceMoniker } from '../../src/ServiceMoniker'
import { ServiceBrokerEmitter } from '../../src/IServiceBroker'

export class AlwaysThrowingRemoteBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IRemoteServiceBroker {
	public isDisposed: boolean = false

	public handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken: CancellationToken): Promise<void> {
		throw new Error("Every day I'm throwing")
	}

	public requestServiceChannel(
		moniker: ServiceMoniker,
		options: ServiceActivationOptions,
		cancellationToken: CancellationToken
	): Promise<RemoteServiceConnectionInfo> {
		throw new Error("Every day I'm throwing")
	}

	public cancelServiceRequest(serviceRequestId: string): Promise<void> {
		throw new Error("Every day I'm throwing")
	}

	public dispose(): void {
		this.isDisposed = true
	}
}
