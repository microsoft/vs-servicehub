/**
 * Represents the format to use in encoding messages
 */
export enum Formatters {
	Utf8 = 0,
	MessagePack = 1,
}

/**
 * Represents the delimiter to use in separating messages
 */
export enum MessageDelimiters {
	HttpLikeHeaders = 0,
	BigEndianInt32LengthHeader = 1,
}

/**
 * Represents the connection formats to a service. Multiple values of this enum can be applied at once,
 * see https://stackoverflow.com/questions/39359740/what-are-enum-flags-in-typescript for more info
 */
export enum RemoteServiceConnections {
	None = 0,
	Multiplexing = 1 << 0,
	IpcPipe = 1 << 1,
	ClrActivation = 1 << 3, // this is represented as 0x4 in .NET, keep it like that for consistency.
}

export module RemoteServiceConnections {
	export function contains(connections: RemoteServiceConnections | string, subset: RemoteServiceConnections) {
		if (typeof connections === 'string') {
			connections = parse(connections)
		}

		return (connections & subset) == subset
	}

	export function parse(connections: string): RemoteServiceConnections {
		let result = RemoteServiceConnections.None
		for (const element of connections.split(',')) {
			switch (element.trim()) {
				case 'None':
					break
				case 'Multiplexing':
					result |= RemoteServiceConnections.Multiplexing
					break
				case 'IpcPipe':
					result |= RemoteServiceConnections.IpcPipe
					break
				case 'ClrActivation':
					result |= RemoteServiceConnections.ClrActivation
					break
				default:
					throw new Error(`unrecognized element '${element.trim()}'.`)
			}
		}
		return result
	}
}

/**
 * Gets a string representing the name emitted for the "AvailabilityChanged" event.
 */
export const availabilityChangedEvent: string = 'availabilityChanged'

export const PIPE_NAME_PREFIX = '\\\\.\\pipe\\'
