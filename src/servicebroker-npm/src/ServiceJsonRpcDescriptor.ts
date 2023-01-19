import { Channel } from 'nerdbank-streams'
import { MessageConnection, CancellationToken as vscodeCancellationToken, createMessageConnection, Message, ParameterStructures } from 'vscode-jsonrpc/node'
import { Formatters, MessageDelimiters } from './constants'
import { ServiceMoniker } from './ServiceMoniker'
import { RpcConnection, RpcEventServer, ServiceRpcDescriptor } from './ServiceRpcDescriptor'
import { clone, constructMessageConnection, isChannel } from './utilities'
import { IDisposable } from './IDisposable'
import { BE32MessageReader, BE32MessageWriter } from './BigEndianInt32LengthHeaderMessageHandler'
import * as msgpack from 'msgpack-lite'
import { CancellationTokenAdapters } from './CancellationTokenAdapter'
import { MultiplexingStream, MultiplexingStreamOptions } from 'nerdbank-streams'
import { EventEmitter } from 'stream'

/**
 * Constructs a JSON RPC message connection to a service
 */
export class ServiceJsonRpcDescriptor extends ServiceRpcDescriptor {
	public readonly protocol = 'json-rpc'
	/**
	 * The options to use when creating a new MultiplexingStream as a prerequisite to establishing an RPC connection.
	 */
	private readonly multiplexingStreamOptions?: Readonly<MultiplexingStreamOptions>
	private readonly connectionFactory: (stream: NodeJS.ReadableStream & NodeJS.WritableStream) => MessageConnection

	/**
	 * Initializes a new instance of the [ServiceJsonRpcDescriptor](#ServiceJsonRpcDescriptor) class
	 * @param moniker The moniker this descriptor describes
	 * @param formatter The formatter to use when sending messages
	 * @param messageDelimiter The delimiter to use in separating messages
	 * @param multiplexingStreamOptions Options to configure a multiplexing stream, on which channel 0 becomes the RPC channel. If undefined, no multiplexing stream will be set up.
	 */
	public constructor(
		moniker: ServiceMoniker,
		public readonly formatter: Formatters,
		public readonly messageDelimiter: MessageDelimiters,
		multiplexingStreamOptions?: MultiplexingStreamOptions
	) {
		super(moniker)

		let contentTypeEncoder: (msg: Message) => Promise<Uint8Array>
		let contentTypeDecoder: (buffer: Uint8Array) => Promise<Message>
		switch (formatter) {
			case Formatters.Utf8:
				contentTypeEncoder = msg => Promise.resolve(Buffer.from(JSON.stringify(msg)))
				contentTypeDecoder = buffer => JSON.parse(buffer.toString())
				break
			case Formatters.MessagePack:
				contentTypeEncoder = msg => Promise.resolve(msgpack.encode(msg))
				contentTypeDecoder = buffer => msgpack.decode(buffer)
				break
			default:
				throw new Error(`Unsupported formatter: ${formatter}.`)
		}
		this.multiplexingStreamOptions = multiplexingStreamOptions === undefined ? undefined : Object.freeze(multiplexingStreamOptions)

		if (messageDelimiter === MessageDelimiters.HttpLikeHeaders) {
			if (formatter !== Formatters.Utf8) {
				// The limited configurations we can support here can be relaxed after we update to vscode-jsonrpc 6.0,
				// which lets us mix-and-match the content type encoder (handler/delimiter) and the content encoder (foramtter) independently.
				throw new Error(`Utf8 is the only formatter supported while using HttpLikeHeaders.`)
			}

			this.connectionFactory = rw => createMessageConnection(rw, rw)
		} else if (messageDelimiter === MessageDelimiters.BigEndianInt32LengthHeader) {
			this.connectionFactory = rw => createMessageConnection(new BE32MessageReader(rw, contentTypeDecoder), new BE32MessageWriter(rw, contentTypeEncoder))
		} else {
			throw new Error(`Unsupported message delimiter: ${messageDelimiter}.`)
		}
	}

	public constructRpcConnection(pipe: NodeJS.ReadWriteStream | Channel): JsonRpcConnection {
		if (this.multiplexingStreamOptions) {
			const multiplexingStreamOptions = this.createSeedChannels()
			let stream: NodeJS.ReadWriteStream = isChannel(pipe) ? pipe.stream : pipe
			const mxstream: MultiplexingStream = MultiplexingStream.Create(stream, multiplexingStreamOptions)
			const rpcChannel = mxstream.acceptChannel(0)
			rpcChannel.completion.finally(() => mxstream.dispose())

			return new JsonRpcConnection(constructMessageConnection(rpcChannel, this.connectionFactory))
		}

		return new JsonRpcConnection(constructMessageConnection(pipe, this.connectionFactory))
	}

	public equals(descriptor: ServiceRpcDescriptor): boolean {
		if (!descriptor || !(descriptor instanceof ServiceJsonRpcDescriptor)) {
			return false
		}

		if (!ServiceMoniker.equals(descriptor.moniker, this.moniker)) {
			return false
		}

		return this.formatter === descriptor.formatter && this.messageDelimiter === descriptor.messageDelimiter
	}

	private createSeedChannels(): MultiplexingStreamOptions {
		if (this.multiplexingStreamOptions === undefined) {
			throw new Error('multiplexingStreamOptions unset.')
		}

		if (this.multiplexingStreamOptions.seededChannels === undefined || this.multiplexingStreamOptions.seededChannels.length === 0) {
			const result = clone<MultiplexingStreamOptions>(this.multiplexingStreamOptions)
			result.seededChannels = [{}]
			result.protocolMajorVersion = 3
			return result
		} else {
			return this.multiplexingStreamOptions
		}
	}
}

const rpcProxy = {
	get: (target: IProxyTarget, property: PropertyKey) => {
		switch (property.toString()) {
			case 'dispose':
				return function () {
					target.messageConnection.dispose()
				}

			case '_jsonRpc':
				return target.messageConnection

			case 'then':
				// When the proxy is returned from async methods,
				// promises look at the return value to see if it too is a promise.
				return undefined

			// EventEmitter methods. We sure hope these don't collide with actual RPC server methods,
			// since we're taking over them here.
			case 'on':
				return function (eventName: string, handler: (...args: any[]) => void): any {
					target.eventEmitter.on(eventName, handler)
					return target
				}
			case 'once':
				return function (eventName: string, handler: (...args: any[]) => void): any {
					target.eventEmitter.once(eventName, handler)
					return target
				}
			case 'prependListener':
				return function (eventName: string, handler: (...args: any[]) => void): any {
					target.eventEmitter.prependListener(eventName, handler)
					return target
				}
			case 'prependOnceListener':
				return function (eventName: string, handler: (...args: any[]) => void): any {
					target.eventEmitter.prependOnceListener(eventName, handler)
					return target
				}
			case 'addListener':
				return function (eventName: string, handler: (...args: any[]) => void): any {
					target.eventEmitter.addListener(eventName, handler)
					return target
				}
			case 'rawListeners':
				return target.eventEmitter.rawListeners
			case 'removeAllListeners':
				return function (eventName: string): any {
					target.eventEmitter.removeAllListeners(eventName)
					return target
				}
			case 'removeListener':
				return function (eventName: string, handler: (...args: any[]) => void): any {
					target.eventEmitter.removeListener(eventName, handler)
					return target
				}
			case 'off':
				return function (eventName: string, handler: (...args: any[]) => void): any {
					target.eventEmitter.off(eventName, handler)
					return target
				}
			case 'listenerCount':
				return target.eventEmitter.listenerCount
			case 'getMaxListeners':
				return target.eventEmitter.getMaxListeners
			case 'setMaxListeners':
				return function (n: number): any {
					target.eventEmitter.setMaxListeners(n)
					return target
				}
			case 'eventNames':
				return function (): any {
					target.eventEmitter.eventNames()
					return target
				}
			case 'emit':
				return function (eventName: string, args: any[]): any {
					target.eventEmitter.emit(eventName, args)
					return target
				}

			default:
				return function () {
					const methodName = property.toString()

					if (arguments.length > 0) {
						if (vscodeCancellationToken.is(arguments[arguments.length - 1])) {
							const ct = arguments[arguments.length - 1]
							const args = validateNoUndefinedElements(Array.prototype.slice.call(arguments, 0, arguments.length - 1))
							return target.messageConnection.sendRequest(methodName, ParameterStructures.byPosition, ...args, ct)
						} else if (CancellationTokenAdapters.IsCancellationToken(arguments[arguments.length - 1])) {
							const ct = CancellationTokenAdapters.CancellationTokenToVSCode(arguments[arguments.length - 1])
							const args = validateNoUndefinedElements(Array.prototype.slice.call(arguments, 0, arguments.length - 1))
							return target.messageConnection.sendRequest(methodName, ParameterStructures.byPosition, ...args, ct)
						} else if (arguments[arguments.length - 1] === undefined) {
							// The last arg is most likely a `CancellationToken?` that was propagated to the RPC call from another method that made it optional.
							// We can't tell, but we mustn't claim it's a CancellationToken nor an ordinary argument or else an RPC server
							// may fail to match the RPC call to a method because of an extra argument.
							// If this truly was a value intended to propagate, they should use `null` as the argument.
							const args = validateNoUndefinedElements(Array.prototype.slice.call(arguments, 0, arguments.length - 1))
							return target.messageConnection.sendRequest(methodName, ParameterStructures.byPosition, ...args)
						}
					}

					const validatedArgs = validateNoUndefinedElements(Array.prototype.slice.call(arguments))
					return target.messageConnection.sendRequest(methodName, ParameterStructures.byPosition, ...validatedArgs)
				}
		}
	},
}

function validateNoUndefinedElements<T>(values: T[]): T[] {
	for (let i = 0; i < values.length; i++) {
		if (values[i] === undefined) {
			throw new Error(`Argument at 0-based index ${i} is set as undefined, which is not a valid JSON value.`)
		}
	}

	return values
}

export class JsonRpcConnection extends RpcConnection {
	constructor(public readonly messageConnection: MessageConnection) {
		super()
	}

	public addLocalRpcTarget(rpcTarget: any | RpcEventServer): void {
		function wrapCancellationTokenIfPresent(args: any[]): any[] {
			if (args.length > 0 && CancellationTokenAdapters.IsVSCode(args[args.length - 1])) {
				const adaptedCancellationToken = CancellationTokenAdapters.VSCodeToCancellationToken(args[args.length - 1])
				args[args.length - 1] = adaptedCancellationToken
			}

			return args
		}

		function registerRequestAndNotification(connection: MessageConnection, methodName: string, method: any) {
			connection.onRequest(methodName, (...args: []) => method.apply(rpcTarget, wrapCancellationTokenIfPresent(args)))
			connection.onNotification(methodName, (...args: []) => method.apply(rpcTarget, wrapCancellationTokenIfPresent(args)))
		}

		JsonRpcConnection.getInstanceMethodNames(rpcTarget, Object.prototype).forEach(methodName => {
			if (methodName !== 'dispose') {
				const method = rpcTarget[methodName]
				registerRequestAndNotification(this.messageConnection, methodName, method)

				// Add an alias for the method so that we support with and without the Async suffix.
				const suffix = 'Async'
				const alias = methodName.endsWith(suffix) ? methodName.substring(0, methodName.length - suffix.length) : `${methodName}${suffix}`
				registerRequestAndNotification(this.messageConnection, alias, method)
			}
		})

		// If the RPC target is an event emitter, hook up a handler that forwards all events across RPC.
		if (RpcConnection.IsRpcEventServer(rpcTarget)) {
			for (let eventName of rpcTarget.rpcEventNames) {
				rpcTarget.on(eventName, args => {
					this.messageConnection.sendNotification(eventName, args)
				})
			}
		}

		if (typeof rpcTarget.dispose === 'function') {
			this.messageConnection.onDispose(() => rpcTarget.dispose())
		}
	}

	public constructRpcClient<T extends object>(): T & IDisposable {
		const target: IProxyTarget = {
			messageConnection: this.messageConnection,
			eventEmitter: new EventEmitter(),
		}
		this.messageConnection.onNotification((method: string, args: any[] | object | undefined) => target.eventEmitter.emit(method, args))
		return new Proxy<IProxyTarget>(target, rpcProxy) as unknown as T & IDisposable
	}

	public startListening(): void {
		this.messageConnection?.listen()
	}

	public dispose(): void {}

	private static isMethod(obj: object, name: string): boolean {
		const desc = Object.getOwnPropertyDescriptor(obj, name)
		return !!desc && typeof desc.value === 'function'
	}

	private static getInstanceMethodNames(obj: object, stopPrototype?: any): string[] {
		const array: string[] = []
		let proto = Object.getPrototypeOf(obj)
		while (proto && proto !== stopPrototype) {
			Object.getOwnPropertyNames(proto).forEach(name => {
				if (name !== 'constructor') {
					if (JsonRpcConnection.isMethod(proto, name)) {
						array.push(name)
					}
				}
			})
			proto = Object.getPrototypeOf(proto)
		}

		return array
	}
}

interface IProxyTarget {
	messageConnection: MessageConnection
	eventEmitter: EventEmitter
}
