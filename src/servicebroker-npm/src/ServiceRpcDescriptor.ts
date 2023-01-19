import assert from 'assert'
import { IDisposable } from './IDisposable'
import { Channel } from 'nerdbank-streams'
import { ServiceMoniker } from './ServiceMoniker'

/**
 * Represents a descriptor for a service broker service
 */
export abstract class ServiceRpcDescriptor {
	/**
	 * Gets or sets the protocol used to talk to this service
	 */
	public abstract get protocol(): string

	/**
	 * Initializes a new instance of the ServiceMoniker class
	 * @param moniker The moniker of the service
	 */
	public constructor(public readonly moniker: ServiceMoniker) {
		assert(moniker)
	}

	/**
	 * Establishes an RPC connection over an <see cref="IDuplexPipe"/>.
	 * @param pipe The pipe used to send and receive RPC messages.
	 * @returns An object representing the lifetime of the connection.
	 */
	public abstract constructRpcConnection(pipe: NodeJS.ReadWriteStream | Channel): RpcConnection

	/**
	 * Constructs an RPC connection with a pipe
	 * @param pipe The pipe to use in RPC construction
	 */
	public constructRpc<T extends object>(pipe: NodeJS.ReadWriteStream | Channel): T & IDisposable

	/**
	 * Constructs an RPC connection with a pipe
	 * @param rpcTarget A local RPC target object to supply to this end of the pipe.
	 * @param pipe The pipe to use in RPC construction
	 */
	public constructRpc<T extends object>(rpcTarget: any | undefined, pipe: NodeJS.ReadWriteStream | Channel): T & IDisposable

	public constructRpc<T extends object>(
		rpcTargetOrPipe: any | undefined | NodeJS.ReadWriteStream | Channel,
		pipe?: NodeJS.ReadWriteStream | Channel
	): T & IDisposable {
		let rpcTarget: any
		if (pipe) {
			rpcTarget = rpcTargetOrPipe
		} else {
			pipe = rpcTargetOrPipe as NodeJS.ReadWriteStream | Channel
		}

		if (pipe === undefined) {
			throw new Error('No pipe supplied.')
		}

		const connection = this.constructRpcConnection(pipe)
		if (rpcTarget !== undefined) {
			connection.addLocalRpcTarget(rpcTarget)
		}

		const client = connection.constructRpcClient<T>()
		connection.startListening()
		return client
	}

	/**
	 * Determines if two descriptors are equivalent values
	 * @param descriptor The descriptor to compare for equality
	 */
	public abstract equals(descriptor: ServiceRpcDescriptor): boolean
}

export abstract class RpcConnection {
	/**
	 * Adds a target object to receive RPC calls.
	 * @param rpcTarget A target for any RPC calls received over the connection. If this object implements <see cref="IDisposable"/>, <see cref="IDisposable.Dispose"/> will be invoked when the client closes their connection.
	 *                  This object should implement {@link RpcEventServer} if emitted events should propagate across the RPC to its client.
	 */
	public abstract addLocalRpcTarget(rpcTarget: any | RpcEventServer): void

	/**
	 * Produces a proxy that provides a strongly-typed API for invoking methods offered by the remote party.
	 * @returns The generated proxy.
	 */
	public abstract constructRpcClient<T extends object>(): T & IDisposable

	/**
	 * Begins listening for incoming messages.
	 */
	public abstract startListening(): void

	/**
	 * Disconnects from the RPC pipe, and disposes of managed and native resources held by this instance.
	 */
	public abstract dispose(): void

	protected static IsRpcEventServer(value: any): value is RpcEventServer {
		return value.rpcEventNames && value.on
	}
}

/**
 * An interface that may be implemented by an RPC target object in order to allow its events to propagate to the RPC client.
 */
export interface RpcEventServer {
	/** An array of names for the events that should propagate to the client. */
	readonly rpcEventNames: readonly string[]
	/** See {@link NodeJS.EventEmitter.on} */
	on(eventName: string | symbol, listener: (...args: any[]) => void): this
}
