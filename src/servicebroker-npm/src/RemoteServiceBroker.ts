import assert from 'assert'
import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { Channel, MultiplexingStream, MultiplexingStreamOptions } from 'nerdbank-streams'
import { AuthorizationServiceClient } from './AuthorizationServiceClient'
import { availabilityChangedEvent, PIPE_NAME_PREFIX, RemoteServiceConnections } from './constants'
import { IAuthorizationService } from './IAuthorizationService'
import { BrokeredServicesChangedArgs } from './BrokeredServicesChangedArgs'
import { IDisposable } from './IDisposable'
import { IRemoteServiceBroker } from './IRemoteServiceBroker'
import { IServiceBroker, ServiceBrokerEmitter } from './IServiceBroker'
import { RemoteServiceConnectionInfo } from './RemoteServiceConnectionInfo'
import { ServiceActivationOptions } from './ServiceActivationOptions'
import { ServiceBrokerClientMetadata } from './ServiceBrokerClientMetadata'
import { ServiceMoniker } from './ServiceMoniker'
import { ServiceRpcDescriptor } from './ServiceRpcDescriptor'
import { isChannel, IsReadWriteStream } from './utilities'
import { createConnection } from 'net'
import { FrameworkServices } from './FrameworkServices'
import path from 'path'

/**
 * An {@link IServiceBroker} that provisions services from a (typically remote) {@link IRemoteServiceBroker}.
 */
export class RemoteServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IDisposable, IServiceBroker {
	private static readonly FullConnectionSupport: RemoteServiceConnections = RemoteServiceConnections.Multiplexing | RemoteServiceConnections.IpcPipe

	/**
	 * Connects to a pipe to a remote service broker that can then answer service requests
	 * @param server The pipe to connect to
	 * @param cancellationToken A cancellation token
	 */
	public static async connectToDuplex(
		server: NodeJS.ReadWriteStream,
		cancellationToken: CancellationToken = CancellationToken.CONTINUE
	): Promise<RemoteServiceBroker> {
		assert(server)

		const remoteBroker = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(server)

		return await RemoteServiceBroker.initializeBrokerConnection(remoteBroker, cancellationToken)
	}

	/**
	 * Connects to a remote service broker that can answer service requests
	 * @param server The remote service broker
	 * @param cancellationToken A cancellation token
	 */
	public static async connectToRemoteServiceBroker(
		server: IRemoteServiceBroker,
		cancellationToken: CancellationToken = CancellationToken.CONTINUE
	): Promise<RemoteServiceBroker> {
		assert(server)

		return await RemoteServiceBroker.initializeBrokerConnection(server, cancellationToken)
	}

	/**
	 * Connects to a multiplexing remote service broker through a pipe
	 * @param server The pipe to connect to
	 * @param cancellationToken A cancellation token
	 */
	public static async connectToMultiplexingDuplex(
		server: NodeJS.ReadWriteStream,
		options?: MultiplexingStreamOptions,
		cancellationToken: CancellationToken = CancellationToken.CONTINUE
	): Promise<RemoteServiceBroker> {
		assert(server)

		const multiplexingStream = await MultiplexingStream.CreateAsync(server, options, cancellationToken)
		const channel = await multiplexingStream.acceptChannelAsync('', undefined, cancellationToken)
		const serviceBroker = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(channel.stream)
		const result = await this.connectToMultiplexingRemoteServiceBroker(serviceBroker, multiplexingStream, cancellationToken)
		result.ownsMxStream = true
		return result
	}

	/**
	 * Connects to a multiplexing remote service broker
	 * @param server The remote service broker
	 * @param stream A multiplexing stream to use in communication
	 * @param cancellationToken A cancellation token
	 */
	public static async connectToMultiplexingRemoteServiceBroker(
		server: IRemoteServiceBroker,
		stream: MultiplexingStream,
		cancellationToken: CancellationToken = CancellationToken.CONTINUE
	): Promise<RemoteServiceBroker> {
		assert(server)
		assert(stream)

		const clientMetadata: ServiceBrokerClientMetadata = {
			supportedConnections: RemoteServiceBroker.FullConnectionSupport,
		}
		const result = await RemoteServiceBroker.initializeBrokerConnection(server, cancellationToken, clientMetadata, stream)
		result.ownsMxStream = false
		return result
	}

	private static async initializeBrokerConnection(
		server: IRemoteServiceBroker,
		cancellationToken: CancellationToken = CancellationToken.CONTINUE,
		clientMetadata?: ServiceBrokerClientMetadata,
		multiplexingStream?: MultiplexingStream
	): Promise<RemoteServiceBroker> {
		if (!clientMetadata) {
			let supportedConnections = this.FullConnectionSupport
			if (!multiplexingStream) {
				supportedConnections &= ~RemoteServiceConnections.Multiplexing
			}

			clientMetadata = { supportedConnections }
		}

		try {
			await server.handshake(clientMetadata, cancellationToken)
		} catch (err) {
			const disposableServer = server as unknown as IDisposable
			if (typeof disposableServer.dispose === 'function') {
				disposableServer.dispose()
			}

			throw err
		}

		return new RemoteServiceBroker(server, clientMetadata, multiplexingStream)
	}

	/**
	 * Indicates if the service broker has been disposed
	 */
	public isDisposed: boolean = false

	/**
	 * Indicates if the service broker owns the multiplexing stream
	 */
	private ownsMxStream: boolean = false

	/**
	 * Defines the default client culture used in communication.
	 */
	public defaultClientCulture = 'en-US'

	/**
	 * Defines the default client UI culture used in communication.
	 */
	public defaultClientUICulture = 'en-US'

	private authorizationClient?: AuthorizationServiceClient

	/**
	 * Initializes a new instance of the [ServiceBroker](#ServiceBroker) class
	 * @param serviceBroker The remote service broker to use for requests
	 * @param clientMetadata The client metadata for the remote service broker
	 * @param multiplexingStream An optional multiplexing stream to use in making requests
	 */
	public constructor(
		private readonly serviceBroker: IRemoteServiceBroker,
		clientMetadata: ServiceBrokerClientMetadata,
		private readonly multiplexingStream?: MultiplexingStream
	) {
		super()
		assert(serviceBroker)
		assert(clientMetadata)
		if (RemoteServiceConnections.contains(clientMetadata.supportedConnections, RemoteServiceConnections.Multiplexing)) {
			assert(multiplexingStream)
		}

		this.serviceBroker.on('availabilityChanged', (args: BrokeredServicesChangedArgs) => {
			this.emit('availabilityChanged', args)
		})
	}

	/**
	 * Sets the authorization service to use when sending requests
	 * @param authorizationService The authorization service
	 */
	public setAuthorizationService(authorizationService: IAuthorizationService): void {
		assert(authorizationService)
		this.authorizationClient?.dispose()

		this.authorizationClient = new AuthorizationServiceClient(authorizationService)
	}

	public async getProxy<T extends object>(
		serviceDescriptor: ServiceRpcDescriptor,
		options?: ServiceActivationOptions,
		cancellationToken: CancellationToken = CancellationToken.CONTINUE
	): Promise<(T & IDisposable) | null> {
		assert(serviceDescriptor)
		options = options ? options : { clientCulture: this.defaultClientCulture, clientUICulture: this.defaultClientUICulture }
		options = await this.applyAuthorization(options, cancellationToken)

		let pipe: NodeJS.ReadWriteStream | Channel | undefined
		let remoteConnectionInfo: RemoteServiceConnectionInfo = {}
		try {
			remoteConnectionInfo = await this.serviceBroker.requestServiceChannel(serviceDescriptor.moniker, options, cancellationToken)
			if (!remoteConnectionInfo || RemoteServiceConnectionInfo.isEmpty(remoteConnectionInfo)) {
				return null
			} else if (remoteConnectionInfo.multiplexingChannelId && this.multiplexingStream) {
				pipe = await this.multiplexingStream.acceptChannel(remoteConnectionInfo.multiplexingChannelId)
			} else if (remoteConnectionInfo.pipeName) {
				// Accommodate Windows pipe names that may or may not include the requisite prefix.
				const pipeName =
					process.platform === 'win32' && !remoteConnectionInfo.pipeName.startsWith(PIPE_NAME_PREFIX)
						? PIPE_NAME_PREFIX + remoteConnectionInfo.pipeName
						: remoteConnectionInfo.pipeName
				pipe = createConnection(pipeName)
			} else {
				throw new Error('Unsupported connection type')
			}
		} catch (err) {
			if (IsReadWriteStream(pipe)) {
				pipe.end()
			} else if (isChannel(pipe)) {
				pipe.dispose()
			}

			if (remoteConnectionInfo?.requestId) {
				await this.serviceBroker.cancelServiceRequest(remoteConnectionInfo.requestId, cancellationToken)
			}

			throw err
		}

		const rpc = serviceDescriptor.constructRpc<T>(options?.clientRpcTarget, pipe)
		return rpc
	}

	public async getPipe(
		serviceMoniker: ServiceMoniker,
		options?: ServiceActivationOptions,
		cancellationToken: CancellationToken = CancellationToken.CONTINUE
	): Promise<NodeJS.ReadWriteStream | null> {
		assert(serviceMoniker)
		options = options ? options : { clientCulture: 'en-US', clientUICulture: 'en-US' }
		options = await this.applyAuthorization(options, cancellationToken)
		if (options.clientRpcTarget) {
			throw new Error('Cannot connect pipe to service with client RPC target')
		}

		let remoteConnectionInfo: RemoteServiceConnectionInfo = {}
		let pipe: NodeJS.ReadWriteStream | null = null
		let channel: Channel | null = null
		try {
			remoteConnectionInfo = await this.serviceBroker.requestServiceChannel(serviceMoniker, options, cancellationToken)
			if (!remoteConnectionInfo || RemoteServiceConnectionInfo.isEmpty(remoteConnectionInfo)) {
				return null
			}
			if (remoteConnectionInfo.multiplexingChannelId && this.multiplexingStream) {
				channel = await this.multiplexingStream.acceptChannelAsync('', undefined, cancellationToken)
				pipe = channel.stream as NodeJS.ReadWriteStream
			} else if (remoteConnectionInfo.pipeName) {
				throw new Error('Cannot connect to named pipe')
			} else {
				throw new Error('Unsupported connection type')
			}

			return pipe
		} catch (err) {
			channel?.dispose()
			pipe?.end()

			if (!pipe && remoteConnectionInfo?.requestId) {
				await this.serviceBroker.cancelServiceRequest(remoteConnectionInfo.requestId, cancellationToken)
			}

			throw err
		}
	}

	public dispose(): void {
		this.isDisposed = true
		this.serviceBroker.removeAllListeners(availabilityChangedEvent)
		const disposableServer = this.serviceBroker as unknown as IDisposable
		if (typeof disposableServer.dispose === 'function') {
			disposableServer.dispose()
		}

		this.authorizationClient?.dispose()
		if (this.multiplexingStream && this.ownsMxStream) {
			this.multiplexingStream.dispose()
		}
	}

	private async applyAuthorization(options: ServiceActivationOptions, cancellationToken: CancellationToken): Promise<ServiceActivationOptions> {
		if (this.authorizationClient && !options.clientCredentials) {
			options.clientCredentials = await this.authorizationClient.getCredentials(cancellationToken)
		}

		return options
	}
}
