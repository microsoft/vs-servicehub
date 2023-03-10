/* eslint-disable @typescript-eslint/naming-convention */
import { MessageConnection } from 'vscode-jsonrpc'
import { IDisposable } from '../IDisposable'
import { invokeRpc, registerInstanceMethodsAsRpcTargets } from './rpcUtilities'

export type MarshaledObjectLifetime = 'call' | 'explicit'

const enum JsonRpcMarshaled {
	/**
	 * A real object is being marshaled. The receiver should generate a new proxy that directs all RPC requests back to the sender referencing the value of the `handle` property.
	 */
	realObject = 1,

	/**
	 * a marshaled proxy is being sent *back* to its owner. The owner uses the `handle` property to look up the original object and use it as the provided value.
	 */
	proxyReturned = 0,
}

const releaseMarshaledObjectMethodName = '$/releaseMarshaledObject'

function constructProxyMethodName(handle: number, method: string, optionalInterface?: number) {
	if (optionalInterface === undefined) {
		return `$/invokeProxy/${handle}/${method}`
	} else {
		return `$/invokeProxy/${handle}/${optionalInterface}.${method}`
	}
}

/**
 * An interface to be implemented by objects that should be marshaled across RPC instead of serialized.
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

interface ReleaseMarshaledObjectArgs {
	/** The `handle` named parameter (or first positional parameter) is set to the handle of the marshaled object to be released. */
	handle: number
	/** The `ownedBySender` named parameter (or second positional parameter) is set to a boolean value indicating whether the party sending this notification is also the party that sent the marshaled object. */
	ownedBySender: boolean
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

interface MessageConnectionWithMarshaldObjectCounter extends MessageConnection {
	_marshaledObjectCounter?: number
}

interface MarshaledObjectProxy extends IDisposable {
	_jsonrpcMarshaledHandle: number
}

module MarshaledObjectProxy {
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

			default:
				return function () {
					const rpcMethod = constructProxyMethodName(target._jsonrpcMarshaledHandle, property.toString())
					return invokeRpc(rpcMethod, arguments, target.messageConnection)
				}
		}
	},
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
	export function wrap(value: RpcMarshalable, jsonConnection: MessageConnection): IJsonRpcMarshaledObject {
		if (MarshaledObjectProxy.is(value)) {
			return {
				__jsonrpc_marshaled: JsonRpcMarshaled.proxyReturned,
				handle: value._jsonrpcMarshaledHandle,
			}
		} else {
			// Use the JSON-RPC connection itself to track the unique counter for us.
			const jsonConnectionWithCounter = jsonConnection as MessageConnectionWithMarshaldObjectCounter
			const handle = jsonConnectionWithCounter._marshaledObjectCounter === undefined ? 1 : jsonConnectionWithCounter._marshaledObjectCounter + 1
			jsonConnectionWithCounter._marshaledObjectCounter = handle

			// Register for requests on the connection to invoke the local object when the receiving side sends requests.
			registerInstanceMethodsAsRpcTargets(value, jsonConnection, methodName => constructProxyMethodName(handle, methodName))

			// Release the object and registrations when the remote side sends the release notification.
			// TODO: code here

			return {
				__jsonrpc_marshaled: JsonRpcMarshaled.realObject,
				handle,
				lifetime: value._jsonRpcMarshalableLifetime,
				optionalInterfaces: value._jsonRpcOptionalInterfaces,
			}
		}
	}

	/**
	 * Produces a proxy for a marshaled object received over JSON-RPC.
	 * @param value The value received over JSON-RPC that is expected to contain data for remotely invoking another object.
	 * @returns An RPC proxy. This should be disposed of when done to release resources held by the remote party.
	 */
	export function unwrap<T>(value: IJsonRpcMarshaledObject, jsonConnection: MessageConnection): T & IDisposable {
		const target: MarshaledObjectProxyTarget = {
			messageConnection: jsonConnection,
			_jsonrpcMarshaledHandle: value.handle,
			dispose: () => {},
		}
		const proxy = new Proxy<MarshaledObjectProxyTarget>(target, rpcProxy)
		return proxy as unknown as T & IDisposable & MarshaledObjectProxy
	}
}
