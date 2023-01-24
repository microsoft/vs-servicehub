import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { IDisposable } from '../../src/IDisposable'
import { IRemoteServiceBroker } from '../../src/IRemoteServiceBroker'
import { RemoteServiceConnectionInfo } from '../../src/RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from '../../src/ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from '../../src/ServiceBrokerClientMetadata'
import { ServiceMoniker } from '../../src/ServiceMoniker'
import { ServiceBrokerEmitter } from '../../src/IServiceBroker'
import { PIPE_NAME_PREFIX } from '../../src/constants'
import { v4 as uuid } from 'uuid'
import { createServer, Server } from 'net'

const calcMoniker = ServiceMoniker.create('Calculator')

export class TestRemoteServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IDisposable, IRemoteServiceBroker {
	private readonly testPipeName
	private readonly server: Server
	public isDisposed: boolean = false
	public clientMetadata?: ServiceBrokerClientMetadata
	public lastReceivedOptions?: ServiceActivationOptions

	constructor() {
		super()

		this.testPipeName = 'testRemoteServiceBroker' + uuid()

		this.server = createServer()
		this.server.listen(PIPE_NAME_PREFIX + this.testPipeName)
	}

	public handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken: CancellationToken): Promise<void> {
		this.clientMetadata = clientMetadata
		return Promise.resolve()
	}

	public async requestServiceChannel(
		moniker: ServiceMoniker,
		options: ServiceActivationOptions,
		cancellationToken: CancellationToken
	): Promise<RemoteServiceConnectionInfo> {
		const result: RemoteServiceConnectionInfo = {}
		this.lastReceivedOptions = options
		if (moniker.name === calcMoniker.name) {
			return {
				multiplexingChannelId: undefined,
				pipeName: this.testPipeName,
			}
		} else if (moniker.name === 'DoesNotExist') {
			result.multiplexingChannelId = 12
		}

		return result
	}

	public cancelServiceRequest(serviceRequestId: string): Promise<void> {
		// no-op
		return Promise.resolve()
	}

	public dispose(): void {
		this.isDisposed = true
		this.server.close()
	}
}
