import { MessageWriter, MessageReader, Message, Disposable, AbstractMessageReader, AbstractMessageWriter } from 'vscode-jsonrpc'
import CancellationToken from 'cancellationtoken'
import { DataCallback } from 'vscode-jsonrpc'
import { Writable } from 'stream'
import { Semaphore } from 'await-semaphore'
import { writeAsync, getBufferFrom } from 'nerdbank-streams/js/Utilities'

/** Reads JSON-RPC messages that have a 32-bit big endian header describing the length of each message. */
export class BE32MessageReader extends AbstractMessageReader implements MessageReader {
	constructor(private readonly readable: NodeJS.ReadableStream, private readonly decoder: (buffer: Uint8Array) => Promise<Message>) {
		super()
	}

	listen(callback: DataCallback): Disposable {
		this.readable.on('error', err => this.fireError(err))

		const cts = CancellationToken.create()
		;(async () => {
			try {
				while (true) {
					const headerBuffer = await getBufferFrom(this.readable, 4, true, cts.token)
					if (!headerBuffer) {
						this.fireClose()
						return
					}

					const payloadLength = headerBuffer.readInt32BE(0)
					const payload = await getBufferFrom(this.readable, payloadLength, false, cts.token)
					const msg = await this.decoder(payload)
					callback(msg)
				}
			} catch (error: any) {
				this.fireError(error)
			}
		})()
		return Disposable.create(() => cts.cancel())
	}
}

/** Writes JSON-RPC messages that have a 32-bit big endian header describing the length of each message. */
export class BE32MessageWriter extends AbstractMessageWriter implements MessageWriter {
	private readonly semaphore = new Semaphore(1)
	private readonly headerBuffer = Buffer.alloc(4)
	private errorCount = 0

	constructor(private readonly writable: NodeJS.WritableStream | Writable, private readonly encoder: (message: Message) => Promise<Uint8Array>) {
		super()
		writable.on('error', err => this.fireError([err, undefined, undefined]))
		writable.once('close', () => this.fireClose())
	}

	async write(msg: Message): Promise<void> {
		await this.semaphore.use(async () => {
			try {
				const encoded = await this.encoder(msg)
				this.headerBuffer.writeInt32BE(encoded.byteLength, 0)
				if (this.writable instanceof Writable) {
					this.writable.cork()
				}

				const writeOps = Promise.all([writeAsync(this.writable, this.headerBuffer), writeAsync(this.writable, encoded)])

				if (this.writable instanceof Writable) {
					this.writable.uncork()
				}

				await writeOps
			} catch (error) {
				this.errorCount++
				this.fireError([asError(error), undefined, this.errorCount])
			}
		})
	}

	end(): void {
		this.writable.end()
	}
}

function asError(error: any): Error {
	if (error instanceof Error) {
		return error
	} else {
		return new Error(`Writer received error. Reason: ${typeof error.message === 'string' ? error.message : 'unknown'}`)
	}
}
