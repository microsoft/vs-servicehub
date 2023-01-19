import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { Channel } from 'nerdbank-streams'
import { Deferred } from 'nerdbank-streams/js/Deferred'
import { availabilityChangedEvent } from '../../src/constants'
import { IRemoteServiceBroker } from '../../src/IRemoteServiceBroker'
import { RemoteServiceConnectionInfo } from '../../src/RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from '../../src/ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from '../../src/ServiceBrokerClientMetadata'
import { ServiceMoniker } from '../../src/ServiceMoniker'
import { BrokeredServicesChangedArgs } from '../../src/BrokeredServicesChangedArgs'
import { IDisposable } from '../../src/IDisposable'
import { ServiceBrokerEmitter } from '../../src/IServiceBroker'
import { FrameworkServices } from '../../src/FrameworkServices'
import { IJsonRpcClientProxy } from '../../src/IJsonRpcClientProxy'

/**
 * A client proxy for talking to an IRemoteServiceBroker.
 */
export class MultiplexingRemoteServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IRemoteServiceBroker, IDisposable {
	public readonly availabilityChangedRaised: Deferred<void> = new Deferred<void>()
	private readonly clientProxy: IRemoteServiceBroker & IDisposable
	public lastIssuedChannelId?: number
	public clientMetadata?: ServiceBrokerClientMetadata
	public lastReceivedOptions?: ServiceActivationOptions
	public eventArgs?: BrokeredServicesChangedArgs

	public constructor(channel: Channel) {
		super()
		this.clientProxy = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(channel.stream)
			; (this.clientProxy as unknown as IJsonRpcClientProxy)._jsonRpc.onNotification(availabilityChangedEvent, (args: BrokeredServicesChangedArgs) => {
				this.eventArgs = args
				this.emit('availabilityChanged', args)
				this.availabilityChangedRaised.resolve()
			})
	}

	public async handshake(clientMetadata: ServiceBrokerClientMetadata, cancellationToken?: CancellationToken): Promise<void> {
		this.clientMetadata = clientMetadata
		return await this.clientProxy.handshake(clientMetadata)
	}

	public async requestServiceChannel(
		moniker: ServiceMoniker,
		options?: ServiceActivationOptions,
		cancellationToken?: CancellationToken
	): Promise<RemoteServiceConnectionInfo> {
		this.lastReceivedOptions = options
		const result: RemoteServiceConnectionInfo = await this.clientProxy.requestServiceChannel(moniker, options)
		this.lastIssuedChannelId = result.multiplexingChannelId
		return result
	}

	public async cancelServiceRequest(serviceRequestId: string): Promise<void> {
		await this.clientProxy.cancelServiceRequest(serviceRequestId)
	}

	public dispose(): void {
		this.clientProxy.dispose()
	}
}
