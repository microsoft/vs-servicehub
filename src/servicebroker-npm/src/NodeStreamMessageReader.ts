import { Disposable, MessageReaderOptions, RAL, ReadableStreamMessageReader } from 'vscode-jsonrpc'

export class NodeStreamMessageReader extends ReadableStreamMessageReader {
	public constructor(readable: NodeJS.ReadableStream, encoding?: RAL.MessageBufferEncoding | MessageReaderOptions) {
		super(new ReadableStreamWrapper(readable), encoding)
	}
}

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
