import assert from 'assert'
import { PassThrough } from 'stream'
import { Message } from 'vscode-jsonrpc'
import { BE32MessageReader } from '../src/BigEndianInt32LengthHeaderMessageHandler'

describe('BE32MessageReader', function () {
	it('rejects negative message length', async function () {
		const result = await readHeaderError(-1)

		assert.match(result.error.message, /Invalid message length: -1/)
		assert.strictEqual(result.decoded, false)
		assert.strictEqual(result.receivedMessage, false)
	})
})

async function readHeaderError(payloadLength: number): Promise<{ error: Error; decoded: boolean; receivedMessage: boolean }> {
	const readable = new PassThrough()
	let decoded = false
	let receivedMessage = false
	const reader = new BE32MessageReader(readable, async (): Promise<Message> => {
		decoded = true
		return {} as Message
	})

	const errorPromise = new Promise<Error>((resolve, reject) => {
		reader.onError(error => resolve(error))
		reader.onClose(() => reject(new Error('Reader closed without rejecting the invalid message length.')))
	})

	reader.listen(() => {
		receivedMessage = true
	})

	const header = Buffer.alloc(4)
	header.writeInt32BE(payloadLength, 0)
	readable.write(header)

	let timeoutId: NodeJS.Timeout
	const timeout = new Promise<Error>((_, reject) => {
		timeoutId = setTimeout(() => reject(new Error('Reader did not reject the invalid message length.')), 100)
	})

	try {
		const error = await Promise.race([errorPromise, timeout])
		return { error, decoded, receivedMessage }
	} finally {
		clearTimeout(timeoutId!)
		readable.destroy()
	}
}
