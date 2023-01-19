import { Channel } from 'nerdbank-streams'
import { MessageConnection } from 'vscode-jsonrpc'
import assert from 'assert'

/**
 * Constructs a message connection to a given pipe
 * @param pipe The channel or duplex to use for communicating
 */
export function constructMessageConnection(
	pipe: Channel | NodeJS.ReadWriteStream,
	connectionFactory: (stream: NodeJS.ReadableStream & NodeJS.WritableStream) => MessageConnection
): MessageConnection {
	assert(pipe)

	let rpc: MessageConnection
	if (IsReadWriteStream(pipe)) {
		rpc = connectionFactory(pipe)
		pipe.on('close', () => {
			rpc?.dispose()
		})
		rpc.onDispose(() => pipe.end())
		rpc.onClose(() => pipe.end())
	} else {
		rpc = connectionFactory(pipe.stream)
		pipe.completion.then(() => {
			rpc?.dispose()
		})
		rpc.onDispose(() => pipe.dispose())
		rpc.onClose(() => pipe.dispose())
	}

	return rpc
}

export function IsReadWriteStream(object: any): object is NodeJS.ReadWriteStream {
	return object && 'unshift' in object
}

export function clone<T extends object>(obj: T): T {
	var copy = obj.constructor()
	for (var attr in obj) {
		if (obj.hasOwnProperty(attr)) {
			copy[attr] = obj[attr]
		}
	}
	return copy
}

export function isChannel(object: any): object is Channel {
	return object && 'stream' in object && 'acceptance' in object && 'completion' in object && 'dispose' in object
}
