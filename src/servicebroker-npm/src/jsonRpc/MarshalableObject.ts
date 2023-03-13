/* eslint-disable @typescript-eslint/naming-convention */
import { MessageConnection, ParameterStructures } from 'vscode-jsonrpc'
import { IDisposable } from '../IDisposable'
import { invokeRpc, registerInstanceMethodsAsRpcTargets } from './rpcUtilities'

export type MarshaledObjectLifetime = 'call' | 'explicit'

const enum JsonRpcMarshaled {
	/**
	 * A real object is being marshaled. The receiver should generate a new proxy (or retrieve an existing one) that directs all RPC requests back to the sender referencing the value of the `handle` property.
	 */
	realObject = 1,

	/**
	 * A marshaled proxy is being sent *back* to its owner. The owner uses the `handle` property to look up the original object and use it as the provided value.
	 */
	proxyReturned = 0,
}

/** The method that releases a marshaled object. Use with {@link ReleaseMarshaledObjectArgs}. */
const releaseMarshaledObjectMethodName = '$/releaseMarshaledObject'

/** The request type to use when sending {@link releaseMarshaledObjectMethodName} notifications. */
interface ReleaseMarshaledObjectArgs {
	/** The `handle` named parameter (or first positional parameter) is set to the handle of the marshaled object to be released. */
	handle: number
	/** The `ownedBySender` named parameter (or second positional parameter) is set to a boolean value indicating whether the party sending this notification is also the party that sent the marshaled object. */
	ownedBySender: boolean
}

export function registerReleaseMarshaledObjectCallback(connection: MessageConnection) {
	connection.onNotification(releaseMarshaledObjectMethodName, (params: ReleaseMarshaledObjectArgs | any[]) => {
		const releaseArgs: ReleaseMarshaledObjectArgs = Array.isArray(params) ? { handle: params[0], ownedBySender: params[1] } : params
		const connectionExtensions = connection as MessageConnectionWithMarshaldObjectSupport
		if (connectionExtensions._marshaledObjectTracker) {
			const releaseByHandle = releaseArgs.ownedBySender
				? connectionExtensions._marshaledObjectTracker.theirsByHandle
				: connectionExtensions._marshaledObjectTracker.ownByHandle
			releaseByHandle[releaseArgs.handle]?.dispose()
		}
	})
}

function constructProxyMethodName(handle: number, method: string, optionalInterface?: number) {
	if (optionalInterface === undefined) {
		return `$/invokeProxy/${handle}/${method}`
	} else {
		return `$/invokeProxy/${handle}/${optionalInterface}.${method}`
	}
}

/**
 * An interface to be implemented by objects that should be marshaled across RPC instead of serialized.
 * An object that also implements {@link IDisposable} will have its {@link IDisposable.dispose} method invoked
 * when the receiver disposes of the proxy.
 */
export interface RpcMarshalable {
	readonly _jsonRpcMarshalableLifetime: MarshaledObjectLifetime
	readonly _jsonRpcOptionalInterfaces?: number[]
}

export module RpcMarshalable {
	export function is(value: any): value is RpcMarshalable {
		const candidate = value as RpcMarshalable | undefined
		return typeof candidate?._jsonRpcMarshalableLifetime === 'string'
	}
}

/**
 * Describes the contract for JSON-RPC marshaled objects as specified by https://github.com/microsoft/vs-streamjsonrpc/blob/main/doc/general_marshaled_objects.md
 */
export interface IJsonRpcMarshaledObject {
	/**
	 * A required property that identifies a marshaled object and its significance.
	 */
	__jsonrpc_marshaled: JsonRpcMarshaled

	/**
	 * A number that SHOULD be unique within the scope and duration of the entire JSON-RPC connection.
	 * A single object is assigned a new handle each time it gets marshaled and each handle's lifetime is distinct.
	 */
	handle: number

	/**
	 * When set to `call`, the marshaled object may only be invoked until the containing RPC call completes. This value is only allowed when used within a JSON-RPC argument. No explicit release using `$/releaseMarshaledObject` is required.
	 * When set to `explicit`, the marshaled object may be invoked until `$/releaseMarshaledObject` releases it. **This is the default behavior when the `lifetime` property is omitted.**
	 */
	lifetime?: MarshaledObjectLifetime

	/**
	 * Specify that the marshaled object implements additional known interfaces, where each array element represents one of these interfaces.
	 * Each element is expected to add to some base functionality that is assumed to be present for this object even if `optionalInterfaces` were omitted.
	 * These integers MUST be within the range of a signed, 32-bit integer.
	 * Each element in the array SHOULD be unique.
	 * A receiver MUST NOT consider order of the integers to be significant, and MUST NOT assume they will be sorted.
	 */
	optionalInterfaces?: number[]
}

interface MessageConnectionWithMarshaldObjectSupport extends MessageConnection {
	_marshaledObjectTracker?: {
		counter: number
		ownByHandle: {
			[key: number]: {
				target: {}
				dispose: () => void
			}
		}
		theirsByHandle: {
			[key: number]: {
				proxy: {}
				dispose: () => void
			}
		}
		/** The handles previously assigned, indexed by the objects they were created for. */
		ownTrackedObjectHandles: WeakMap<{}, number>
	}
}

export interface MarshaledObjectProxy extends IDisposable {
	_jsonrpcMarshaledHandle: number
}

export module MarshaledObjectProxy {
	export function is(value: any): value is MarshaledObjectProxy {
		const valueCandidate = value as MarshaledObjectProxy | undefined
		return typeof valueCandidate?._jsonrpcMarshaledHandle === 'number'
	}
}

interface MarshaledObjectProxyTarget extends MarshaledObjectProxy {
	messageConnection: MessageConnection
}

const rpcProxy = {
	// The proxy is expected to implement MarshaledObjectProxy & T
	get: (target: MarshaledObjectProxyTarget, property: PropertyKey) => {
		switch (property.toString()) {
			case 'dispose':
				return target.dispose

			case '_jsonrpcMarshaledHandle':
				return target._jsonrpcMarshaledHandle

			case 'then':
				// When the proxy is returned from async methods,
				// promises look at the return value to see if it too is a promise.
				return undefined

			case 'toJSON':
				// Tests sometimes fail after trying to call this function, so make it clear it isn't supported.
				return undefined

			default:
				return function () {
					const rpcMethod = constructProxyMethodName(target._jsonrpcMarshaledHandle, property.toString())
					return invokeRpc(rpcMethod, arguments, target.messageConnection)
				}
		}
	},
}

function getJsonConnectionMarshalingTracker(messageConnection: MessageConnection) {
	const jsonConnectionWithCounter = messageConnection as MessageConnectionWithMarshaldObjectSupport
	jsonConnectionWithCounter._marshaledObjectTracker ??= { counter: 0, ownByHandle: {}, theirsByHandle: {}, ownTrackedObjectHandles: new WeakMap() }
	return jsonConnectionWithCounter._marshaledObjectTracker
}

export module IJsonRpcMarshaledObject {
	/**
	 * Tests whether a given object implements {@link IJsonRpcMarshaledObject}.
	 * @param value the value to be tested.
	 * @returns true if the object conforms to the contract.
	 */
	export function is(value: any): value is IJsonRpcMarshaledObject {
		const valueCandidate = value as IJsonRpcMarshaledObject | undefined
		return typeof valueCandidate?.__jsonrpc_marshaled === 'number' && typeof valueCandidate.handle === 'number'
	}

	/**
	 * Creates a JSON-RPC serializable object that provides the receiver with a means to invoke methods on the object.
	 * @param value The object to create a JSON-RPC marshalable wrapper around.
	 */
	export function wrap(value: RpcMarshalable | MarshaledObjectProxy, jsonConnection: MessageConnection): IJsonRpcMarshaledObject {
		// Use the JSON-RPC connection itself to track the unique counter for us.
		const connectionMarshalingTracker = getJsonConnectionMarshalingTracker(jsonConnection)

		if (MarshaledObjectProxy.is(value)) {
			return {
				__jsonrpc_marshaled: JsonRpcMarshaled.proxyReturned,
				handle: value._jsonrpcMarshaledHandle,
			}
		} else {
			if (value._jsonRpcMarshalableLifetime === 'call') {
				throw new Error('Receiving marshaled objects scoped to the lifetime of a single RPC request is not yet supported.')
			}

			const alreadyMarshaled = connectionMarshalingTracker.ownTrackedObjectHandles.has(value)
			const handle: number = alreadyMarshaled ? connectionMarshalingTracker.ownTrackedObjectHandles.get(value)! : ++connectionMarshalingTracker.counter
			if (!alreadyMarshaled) {
				// Associate this object and this message connection tuple with the handle so that if we ever wrap it again, we'll use the same handle.
				connectionMarshalingTracker.ownTrackedObjectHandles.set(value, handle)

				// Register for requests on the connection to invoke the local object when the receiving side sends requests.
				const registration = registerInstanceMethodsAsRpcTargets(value, jsonConnection, methodName => constructProxyMethodName(handle, methodName))

				// Arrange to release the object and registrations when the remote side sends the release notification.
				connectionMarshalingTracker.ownByHandle[handle] = {
					target: value,
					dispose: () => {
						registration.dispose()
						delete connectionMarshalingTracker.ownByHandle[handle]
						connectionMarshalingTracker.ownTrackedObjectHandles.delete(value)
						if ('dispose' in value && typeof value.dispose === 'function') {
							value.dispose()
						}
					},
				}
			}

			return {
				__jsonrpc_marshaled: JsonRpcMarshaled.realObject,
				handle,
				lifetime: value._jsonRpcMarshalableLifetime,
				optionalInterfaces: value._jsonRpcOptionalInterfaces,
			}
		}
	}

	export function cancelWrap(value: IJsonRpcMarshaledObject, jsonConnection: MessageConnection) {
		const connectionMarshalingTracker = getJsonConnectionMarshalingTracker(jsonConnection)
		const tracker = connectionMarshalingTracker.ownByHandle[value.handle]
		tracker?.dispose()
	}

	/**
	 * Produces a proxy for a marshaled object received over JSON-RPC.
	 * @param value The value received over JSON-RPC that is expected to contain data for remotely invoking another object.
	 * @returns An RPC proxy. This should be disposed of when done to release resources held by the remote party.
	 */
	export function unwrap<T>(value: IJsonRpcMarshaledObject, jsonConnection: MessageConnection): T {
		if (value.lifetime === 'call') {
			throw new Error('Receiving marshaled objects scoped to the lifetime of a single RPC request is not yet supported.')
		}

		const connectionMarshalingTracker = getJsonConnectionMarshalingTracker(jsonConnection)
		switch (value.__jsonrpc_marshaled) {
			case JsonRpcMarshaled.realObject:
				let proxy = connectionMarshalingTracker.theirsByHandle[value.handle]?.proxy

				if (!proxy) {
					// A novel object has been provided to us.
					const target: MarshaledObjectProxyTarget = {
						messageConnection: jsonConnection,
						_jsonrpcMarshaledHandle: value.handle,
						dispose: () => {
							if (connectionMarshalingTracker.theirsByHandle[value.handle]) {
								delete connectionMarshalingTracker.theirsByHandle[value.handle]

								// We need to notify the owner of the remote object.
								const releaseArgs: ReleaseMarshaledObjectArgs = {
									handle: value.handle,
									ownedBySender: false,
								}
								jsonConnection.sendNotification(releaseMarshaledObjectMethodName, ParameterStructures.byName, releaseArgs)
							}
						},
					}
					proxy = new Proxy<MarshaledObjectProxyTarget>(target, rpcProxy)
					connectionMarshalingTracker.theirsByHandle[value.handle] = {
						proxy,
						dispose: () => {
							// This is invoked when the remote party that owns the object notifies us that the object is destroyed.
							delete connectionMarshalingTracker.theirsByHandle[value.handle]
						},
					}
				}

				return proxy as unknown as T & IDisposable & MarshaledObjectProxy
			case JsonRpcMarshaled.proxyReturned:
				// A marshaled object that we provided to the remote party has come back to us.
				// Make sure we unwrap it as the original object.
				const tracker = connectionMarshalingTracker.ownByHandle[value.handle]
				if (!tracker) {
					throw new Error(`Unrecognized handle ${value.handle}`)
				}
				return tracker.target as T
			default:
				throw new Error(`Unsupported value for __jsonrpc_marshaled: ${value.__jsonrpc_marshaled}.`)
		}
	}
}
