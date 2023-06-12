import { Disposable, MessageReaderOptions, MessageWriterOptions, RAL, ReadableStreamMessageReader, WriteableStreamMessageWriter } from 'vscode-jsonrpc'

// https://github.com/microsoft/vscode-languageserver-node/blob/4d7d8f4229a539f36bc28c634470539bcda8b362/jsonrpc/src/node/main.ts#L157-L161
export class NodeStreamMessageReader extends ReadableStreamMessageReader {
	public constructor(readable: NodeJS.ReadableStream, encoding?: RAL.MessageBufferEncoding | MessageReaderOptions) {
		super(new ReadableStreamWrapper(readable), encoding)
	}
}

// https://github.com/microsoft/vscode-languageserver-node/blob/4d7d8f4229a539f36bc28c634470539bcda8b362/jsonrpc/src/node/main.ts#L163-L167
export class NodeStreamMessageWriter extends WriteableStreamMessageWriter {
	public constructor(writable: NodeJS.WritableStream, options?: RAL.MessageBufferEncoding | MessageWriterOptions) {
		super(new WritableStreamWrapper(writable), options)
	}
}

// https://github.com/microsoft/vscode-languageserver-node/blob/4d7d8f4229a539f36bc28c634470539bcda8b362/jsonrpc/src/node/ril.ts#L48-L72
class ReadableStreamWrapper implements RAL.ReadableStream {
	constructor(private stream: NodeJS.ReadableStream) {}

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

	public onData(listener: (data: Uint8Array) => void): Disposable {
		this.stream.on('data', listener)
		return Disposable.create(() => this.stream.off('data', listener))
	}
}

// https://github.com/microsoft/vscode-languageserver-node/blob/4d7d8f4229a539f36bc28c634470539bcda8b362/jsonrpc/src/node/ril.ts#L74-L114
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
