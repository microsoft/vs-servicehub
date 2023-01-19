import CancellationToken from 'cancellationtoken'
import { PIPE_NAME_PREFIX, RemoteServiceConnections } from './constants'
import EventEmitter = require('events')
import { RemoteServiceConnectionInfo } from './RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from './ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from './ServiceBrokerClientMetadata'
import { ServiceMoniker } from './ServiceMoniker'
import { IDisposable } from './IDisposable'
import { IRemoteServiceBroker } from './IRemoteServiceBroker'
import { ServiceBrokerEmitter, IServiceBroker } from './IServiceBroker'
import { v4 as uuid } from 'uuid'
import { createServer, Server } from 'net'
import { BrokeredServicesChangedArgs } from './BrokeredServicesChangedArgs'
import { RpcEventServer } from './ServiceRpcDescriptor'

/**
 * An IRemoteServiceBroker which proffers all services from another IServiceBroker
 * over named pipes on Windows or Unix domain sockets on other operating systems.
 */
export class IpcRelayServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IRemoteServiceBroker, RpcEventServer, IDisposable {
	private static readonly _rpcEventNames = Object.freeze(['availabilityChanged'])
	readonly rpcEventNames = IpcRelayServiceBroker._rpcEventNames
	public readonly completion: Promise<void>
	private readonly channelsOfferedToClient: { [Key: string]: Server } = {}
	private disposed: (() => void) | undefined

	constructor(private readonly serviceBroker: IServiceBroker) {
		super()
		serviceBroker.on('availabilityChanged', this.onAvailabilityChanged.bind(this))
		this.completion = new Promise<void>(resolve => (this.disposed = resolve))
	}

	handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken?: CancellationToken): Promise<void> {
		if (!RemoteServiceConnections.contains(clientMetadata.supportedConnections, RemoteServiceConnections.IpcPipe)) {
			throw new Error('The client must support IpcPipe to use this service broker.')
		}

		return Promise.resolve()
	}
	async requestServiceChannel(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions,
		cancellationToken?: CancellationToken
	): Promise<RemoteServiceConnectionInfo> {
		const pipe = await this.serviceBroker.getPipe(serviceMoniker, options, cancellationToken)
		if (!pipe) {
			return {}
		}

		const requestId = uuid()
		const pipeName = uuid()

		const server = createServer(serverPipe => {
			// Drop the entry from our map once the connection has been made.
			delete this.channelsOfferedToClient[requestId]

			serverPipe.pipe(pipe)
			pipe.pipe(serverPipe)
		})
		server.listen(PIPE_NAME_PREFIX + pipeName)
		this.channelsOfferedToClient[requestId] = server

		return {
			requestId,
			pipeName,
		}
	}
	cancelServiceRequest(serviceRequestId: string, cancellationToken?: CancellationToken): Promise<void> {
		const server = this.channelsOfferedToClient[serviceRequestId]
		if (server) {
			delete this.channelsOfferedToClient[serviceRequestId]
			server.close()
			return Promise.resolve()
		} else {
			return Promise.reject('Request to cancel a channel that is not awaiting acceptance.')
		}
	}
	dispose() {
		this.serviceBroker.off('availabilityChanged', this.onAvailabilityChanged.bind(this))
		for (const requestId in this.channelsOfferedToClient) {
			const server = this.channelsOfferedToClient[requestId]
			server.close()
			delete this.channelsOfferedToClient[requestId]
		}

		if (this.disposed) {
			this.disposed()
		}
	}

	private onAvailabilityChanged(args: BrokeredServicesChangedArgs) {
		this.emit('availabilityChanged', args)
	}
}
