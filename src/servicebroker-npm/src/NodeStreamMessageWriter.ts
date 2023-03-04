import { Disposable, MessageWriterOptions, RAL, WriteableStreamMessageWriter } from 'vscode-jsonrpc'

export class NodeStreamMessageWriter extends WriteableStreamMessageWriter {
	public constructor(writable: NodeJS.WritableStream, options?: RAL.MessageBufferEncoding | MessageWriterOptions) {
		super(new WritableStreamWrapper(writable), options)
	}
}

class WritableStreamWrapper implements RAL.WritableStream {
	constructor(private stream: NodeJS.WritableStream) {}

	public onClose(listener: () => void): Disposable {
		this.stream.on('close', listener)
		return Disposable.create(() => this.stream.off('close', listener))
	}

	public onError(listener: (error: any) => void): Disposable {
		this.stream.on('error', listener)
		return Disposable.create(() => this.stream.off('error', listener))
	}

	public onEnd(listener: () => void): Disposable {
		this.stream.on('end', listener)
		return Disposable.create(() => this.stream.off('end', listener))
	}

	public write(data: Uint8Array | string, encoding?: RAL.MessageBufferEncoding): Promise<void> {
		return new Promise((resolve, reject) => {
			const callback = (error: Error | undefined | null) => {
				if (error === undefined || error === null) {
					resolve()
				} else {
					reject(error)
				}
			}
			if (typeof data === 'string') {
				this.stream.write(data, encoding, callback)
			} else {
				this.stream.write(data, callback)
			}
		})
	}

	public end(): void {
		this.stream.end()
	}
}
