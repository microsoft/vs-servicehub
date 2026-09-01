import CancellationToken from 'cancellationtoken'
import { randomUUID } from 'crypto'
import { EventEmitter } from 'events'
import { unlinkSync } from 'fs'
import { createServer, Server } from 'net'
import { tmpdir } from 'os'
import path from 'path'
import { Channel } from 'nerdbank-streams'
import { RemoteServiceBroker, RemoteServiceConnections, ServiceActivationOptions, ServiceBrokerClientMetadata, ServiceMoniker, ServiceRpcDescriptor } from '../src'
import { RemoteServiceConnectionInfo } from '../src/RemoteServiceConnectionInfo'
import { IDisposable } from '../src/IDisposable'
import { IRemoteServiceBroker } from '../src/IRemoteServiceBroker'
import { ServiceBrokerEmitter } from '../src/IServiceBroker'
import { RpcConnection } from '../src/ServiceRpcDescriptor'

describe('RemoteServiceBroker security', function () {
	it('[CWE-610] rejects pipeName returned for proxy requests when disallowed', async function () {
		const pipeName = getTestPipeName()
		const server = createServer(socket => socket.destroy())
		let acceptedConnection = false
		server.on('connection', () => {
			acceptedConnection = true
		})

		await listen(server, pipeName)

		const remoteBroker = new PipeNameRemoteServiceBroker(pipeName)
		const broker = await RemoteServiceBroker.connectToRemoteServiceBroker(remoteBroker, undefined, { supportedConnections: RemoteServiceConnections.None })
		let proxy: IDisposable | null = null
		try {
			const getProxy = broker.getProxy<object>(new TestRpcDescriptor(), undefined, CancellationToken.CONTINUE).then(result => {
				proxy = result
				return result
			})

			await expect(getProxy).rejects.toThrow('Unsupported connection type')
			expect(acceptedConnection).toBe(false)
		} finally {
			const proxyToDispose = proxy as IDisposable | null
			proxyToDispose?.dispose()
			broker.dispose()
			await close(server)
			if (process.platform !== 'win32') {
				try {
					unlinkSync(pipeName)
				} catch {
					// Best effort cleanup for Unix-domain socket paths.
				}
			}
		}
	})
})

class PipeNameRemoteServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IDisposable, IRemoteServiceBroker {
	public constructor(private readonly pipeName: string) {
		super()
	}

	public handshake(clientMetadata: ServiceBrokerClientMetadata): Promise<void> {
		return Promise.resolve()
	}

	public requestServiceChannel(
		moniker: ServiceMoniker,
		options?: ServiceActivationOptions,
		cancellationToken?: CancellationToken
	): Promise<RemoteServiceConnectionInfo> {
		return Promise.resolve({ pipeName: this.pipeName })
	}

	public cancelServiceRequest(serviceRequestId: string): Promise<void> {
		return Promise.resolve()
	}

	public dispose(): void {}
}

class TestRpcDescriptor extends ServiceRpcDescriptor {
	public constructor() {
		super(ServiceMoniker.create('Calculator'))
	}

	public get protocol(): string {
		return 'test'
	}

	public constructRpcConnection(pipe: NodeJS.ReadWriteStream | Channel): RpcConnection {
		return new TestRpcConnection(pipe)
	}

	public equals(descriptor: ServiceRpcDescriptor): boolean {
		return descriptor === this
	}
}

class TestRpcConnection extends RpcConnection {
	public constructor(private readonly pipe: NodeJS.ReadWriteStream | Channel) {
		super()
	}

	public addLocalRpcTarget(rpcTarget: any): void {}

	public constructRpcClient<T extends object>(): T & IDisposable {
		return { dispose: () => this.dispose() } as T & IDisposable
	}

	public startListening(): void {}

	public dispose(): void {
		if ('dispose' in this.pipe) {
			this.pipe.dispose()
		} else {
			this.pipe.end()
		}
	}
}

function getTestPipeName(): string {
	const pipeId = `remote-service-broker-security-${process.pid}-${randomUUID()}`
	return process.platform === 'win32' ? path.join('\\\\.\\pipe\\', pipeId) : path.join(tmpdir(), `${pipeId}.sock`)
}

function listen(server: Server, pipeName: string): Promise<void> {
	return new Promise((resolve, reject) => {
		server.once('error', reject)
		server.listen(pipeName, () => {
			server.off('error', reject)
			resolve()
		})
	})
}

function close(server: Server): Promise<void> {
	return new Promise((resolve, reject) => {
		server.close(err => {
			if (err) {
				reject(err)
			} else {
				resolve()
			}
		})
	})
}
