import CancellationToken from 'cancellationtoken'
import { BrokeredServicesChangedArgs } from './BrokeredServicesChangedArgs'
import { RemoteServiceConnections } from './constants'
import EventEmitter = require('events')
import { IDisposable } from './IDisposable'
import { IRemoteServiceBroker } from './IRemoteServiceBroker'
import { ServiceBrokerEmitter, IServiceBroker } from './IServiceBroker'
import { Channel, MultiplexingStream } from 'nerdbank-streams'
import { RemoteServiceConnectionInfo } from './RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from './ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from './ServiceBrokerClientMetadata'
import { ServiceMoniker } from './ServiceMoniker'
import { FrameworkServices } from './FrameworkServices'
import caught from 'caught'
import { v4 as uuid } from 'uuid'
import { RpcEventServer } from './ServiceRpcDescriptor'

export class MultiplexingRelayServiceBroker
	extends (EventEmitter as new () => ServiceBrokerEmitter)
	implements IRemoteServiceBroker, RpcEventServer, IDisposable
{
	private static readonly _rpcEventNames = Object.freeze(['availabilityChanged'])
	private readonly channelsOfferedToClient: { [Key: string]: Channel } = {}
	public readonly completion: Promise<void>
	readonly rpcEventNames = MultiplexingRelayServiceBroker._rpcEventNames
	private disposed: (() => void) | undefined

	constructor(
		private readonly serviceBroker: IServiceBroker,
		private readonly multiplexingStreamWithClient: MultiplexingStream,
		private readonly multiplexingStreamWithRemoteClientOwned: boolean
	) {
		super()

		serviceBroker.on('availabilityChanged', this.onAvailabilityChanged.bind(this))
		this.completion = new Promise<void>(resolve => (this.disposed = resolve))
	}

	/**
	 * Initializes a new instance of the [MultiplexingRelayServiceBroker](#MultiplexingRelayServiceBroker)
	 * and establishes a [MultiplexingStream](#MultiplexingStream) protocol with the client over the given stream.
	 * @param serviceBroker A broker for the services to be relayed.
	 * @param duplexStreamWithClient
	 * The duplex stream over which the client will make RPC calls to the returned [IRemoteServiceBroker](#IRemoteServiceBroker) instance.
	 * A multiplexing stream will be established on this stream and the client is expected to accept an offer for a channel with an empty string for a name.
	 * This object is considered "owned" by the returned [MultiplexingRelayServiceBroker](#MultiplexingRelayServiceBroker) and will be disposed when the returned value is disposed,
	 * or disposed before this method throws.
	 * @param cancellationToken A cancellation token
	 * @returns A [MultiplexingRelayServiceBroker](#MultiplexingRelayServiceBroker) that provides access to remote services, all over a multiplexing stream.
	 * @remarks The [RemoteServiceBroker](#RemoteServiceBroker) is used as the wire protocol.
	 */
	static async connectToServer(serviceBroker: IServiceBroker, duplexStreamWithClient: NodeJS.ReadWriteStream, cancellationToken?: CancellationToken) {
		try {
			const multiplexingStreamWithClient = await MultiplexingStream.CreateAsync(duplexStreamWithClient, undefined, cancellationToken)
			const clientChannel = await multiplexingStreamWithClient.offerChannelAsync('', undefined, cancellationToken)
			const result = new MultiplexingRelayServiceBroker(serviceBroker, multiplexingStreamWithClient, true)
			FrameworkServices.remoteServiceBroker.constructRpc(result, clientChannel)
			return result
		} catch (e) {
			duplexStreamWithClient.end()
			throw e
		}
	}

	handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken?: CancellationToken): Promise<void> {
		if (!RemoteServiceConnections.contains(clientMetadata.supportedConnections, RemoteServiceConnections.Multiplexing)) {
			throw new Error('The client must support multiplexing to use this service broker.')
		}

		return Promise.resolve()
	}

	async requestServiceChannel(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions | undefined,
		cancellationToken?: CancellationToken | undefined
	): Promise<RemoteServiceConnectionInfo> {
		const requestId = uuid()
		options = options ? { ...options } : {}
		options.multiplexingStream = this.multiplexingStreamWithClient
		const servicePipe = await this.serviceBroker.getPipe(serviceMoniker, options, cancellationToken)
		if (!servicePipe) {
			return {}
		}

		const outerChannel = this.multiplexingStreamWithClient.createChannel()
		outerChannel.stream.pipe(servicePipe)
		servicePipe.pipe(outerChannel.stream)
		this.channelsOfferedToClient[requestId] = outerChannel
		caught(outerChannel.acceptance.finally(() => delete this.channelsOfferedToClient[requestId]))

		return {
			requestId,
			multiplexingChannelId: outerChannel.qualifiedId.id,
		}
	}

	async cancelServiceRequest(serviceRequestId: string, cancellationToken?: CancellationToken | undefined): Promise<void> {
		const channel = this.channelsOfferedToClient[serviceRequestId]
		if (channel) {
			channel.dispose()
			return Promise.resolve()
		} else {
			return Promise.reject('Request to cancel a channel that is not awaiting acceptance.')
		}
	}

	dispose(): void {
		this.serviceBroker.off('availabilityChanged', this.onAvailabilityChanged.bind(this))

		if (this.multiplexingStreamWithRemoteClientOwned) {
			this.multiplexingStreamWithClient.dispose()
		}

		if (this.disposed) {
			this.disposed()
		}
	}

	private onAvailabilityChanged(args: BrokeredServicesChangedArgs) {
		this.emit('availabilityChanged', args)
	}
}
