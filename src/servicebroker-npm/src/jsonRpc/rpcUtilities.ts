import { MessageConnection, CancellationToken as vscodeCancellationToken, ParameterStructures, Disposable } from 'vscode-jsonrpc'
import { CancellationTokenAdapters } from '../CancellationTokenAdapter'
import { IJsonRpcMarshaledObject, MarshaledObjectProxy, RpcMarshalable } from './MarshalableObject'

export async function invokeRpc(methodName: string, inputArgs: IArguments, messageConnection: MessageConnection): Promise<any> {
	if (inputArgs.length > 0) {
		if (vscodeCancellationToken.is(inputArgs[inputArgs.length - 1])) {
			const ct = inputArgs[inputArgs.length - 1]
			const args = filterOutboundArgs(messageConnection, Array.prototype.slice.call(inputArgs, 0, inputArgs.length - 1))
			return messageConnection.sendRequest(methodName, ParameterStructures.byPosition, ...args, ct)
		} else if (CancellationTokenAdapters.isCancellationToken(inputArgs[inputArgs.length - 1])) {
			const ct = CancellationTokenAdapters.cancellationTokenToVSCode(inputArgs[inputArgs.length - 1])
			const args = filterOutboundArgs(messageConnection, Array.prototype.slice.call(inputArgs, 0, inputArgs.length - 1))
			return messageConnection.sendRequest(methodName, ParameterStructures.byPosition, ...args, ct)
		} else if (inputArgs[inputArgs.length - 1] === undefined) {
			// The last arg is most likely a `CancellationToken?` that was propagated to the RPC call from another method that made it optional.
			// We can't tell, but we mustn't claim it's a CancellationToken nor an ordinary argument or else an RPC server
			// may fail to match the RPC call to a method because of an extra argument.
			// If this truly was a value intended to propagate, they should use `null` as the argument.
			const args = filterOutboundArgs(messageConnection, Array.prototype.slice.call(inputArgs, 0, inputArgs.length - 1))
			return messageConnection.sendRequest(methodName, ParameterStructures.byPosition, ...args)
		}
	}

	const validatedArgs = filterOutboundArgs(messageConnection, Array.prototype.slice.call(inputArgs))
	try {
		const result = await messageConnection.sendRequest(methodName, ParameterStructures.byPosition, ...validatedArgs)
		return filterInboundResult(messageConnection, result)
	} catch (reason) {
		// If any args were marshaled objects, dispose of them.
		for (const arg of validatedArgs) {
			if (IJsonRpcMarshaledObject.is(arg)) {
				IJsonRpcMarshaledObject.cancelWrap(arg, messageConnection)
			}
		}

		throw reason
	}
}

function filterOutboundArgs(connection: MessageConnection, args: any[]): any[] {
	return validateNoUndefinedElements(args).map(v => filterOutboundMarshalableObject(connection, v))
}

async function filterOutboundResult(connection: MessageConnection, value: any | Promise<any>): Promise<any> {
	const unwrappedPromiseValue = await value
	return filterOutboundMarshalableObject(connection, unwrappedPromiseValue)
}

function filterOutboundMarshalableObject(connection: MessageConnection, value: any): any | IJsonRpcMarshaledObject {
	if (RpcMarshalable.is(value) || MarshaledObjectProxy.is(value)) {
		return IJsonRpcMarshaledObject.wrap(value, connection)
	} else {
		return value
	}
}

function validateNoUndefinedElements<T>(values: T[]): T[] {
	for (let i = 0; i < values.length; i++) {
		if (values[i] === undefined) {
			throw new Error(`Argument at 0-based index ${i} is set as undefined, which is not a valid JSON value.`)
		}
	}

	return values
}

function isMethod(obj: object, name: string): boolean {
	const desc = Object.getOwnPropertyDescriptor(obj, name)
	return !!desc && typeof desc.value === 'function'
}

function getInstanceMethodNames(obj: object, stopPrototype?: any): string[] {
	const array: string[] = []
	let proto = Object.getPrototypeOf(obj)
	while (proto && proto !== stopPrototype) {
		Object.getOwnPropertyNames(proto).forEach(name => {
			if (name !== 'constructor') {
				if (isMethod(proto, name)) {
					array.push(name)
				}
			}
		})
		proto = Object.getPrototypeOf(proto)
	}

	return array
}

function wrapCancellationTokenIfPresent(args: any[]): any[] {
	if (args.length > 0 && CancellationTokenAdapters.isVSCode(args[args.length - 1])) {
		const adaptedCancellationToken = CancellationTokenAdapters.vscodeToCancellationToken(args[args.length - 1])
		args[args.length - 1] = adaptedCancellationToken
	}

	return args
}

function filterInboundMarshalableObject(connection: MessageConnection, value: any | IJsonRpcMarshaledObject): any {
	if (IJsonRpcMarshaledObject.is(value)) {
		return IJsonRpcMarshaledObject.unwrap(value, connection)
	} else {
		return value
	}
}

function filterInboundValue(connection: MessageConnection, value: any): any {
	return filterInboundMarshalableObject(connection, value)
}

function filterInboundArguments(connection: MessageConnection, args: any[]): any[] {
	return wrapCancellationTokenIfPresent(args).map(v => filterInboundValue(connection, v))
}

function filterInboundResult(connection: MessageConnection, value: any): any {
	return filterInboundValue(connection, value)
}

export function registerInstanceMethodsAsRpcTargets(
	rpcTarget: any,
	connection: MessageConnection,
	rpcMethodNameTransform?: (functionName: string) => string
): Disposable {
	const disposables: Disposable[] = []

	function registerRequestAndNotification(methodName: string, method: any) {
		const rpcMethodName = rpcMethodNameTransform ? rpcMethodNameTransform(methodName) : methodName
		disposables.push(
			connection.onRequest(rpcMethodName, (...args: []) =>
				filterOutboundResult(connection, method.apply(rpcTarget, filterInboundArguments(connection, args)))
			)
		)
		disposables.push(connection.onNotification(rpcMethodName, (...args: []) => method.apply(rpcTarget, filterInboundArguments(connection, args))))
	}

	getInstanceMethodNames(rpcTarget, Object.prototype).forEach(methodName => {
		if (methodName !== 'dispose') {
			const method = rpcTarget[methodName]
			registerRequestAndNotification(methodName, method)

			// Add an alias for the method so that we support with and without the Async suffix.
			const suffix = 'Async'
			const alias = methodName.endsWith(suffix) ? methodName.substring(0, methodName.length - suffix.length) : `${methodName}${suffix}`
			registerRequestAndNotification(alias, method)
		}
	})

	return Disposable.create(() => disposables.forEach(d => d.dispose()))
}
