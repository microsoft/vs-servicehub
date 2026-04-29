import { FullDuplexStream } from 'nerdbank-streams'
import { existsSync, mkdtempSync, rmSync, statSync } from 'fs'
import { connect, Socket } from 'net'
import { tmpdir } from 'os'
import path from 'path'
import { PassThrough } from 'stream'
import { BrokeredServicesChangedArgs } from '../src/BrokeredServicesChangedArgs'
import { RemoteServiceConnections } from '../src/constants'
import { FrameworkServices } from '../src/FrameworkServices'
import { IDisposable } from '../src/IDisposable'
import { IpcRelayServiceBroker } from '../src/IpcRelayServiceBroker'
import { IRemoteServiceBroker } from '../src/IRemoteServiceBroker'
import { IServiceBroker } from '../src/IServiceBroker'
import { RemoteServiceBroker } from '../src/RemoteServiceBroker'
import { ICalculatorService } from './testAssets/interfaces'
import { MockServiceBroker } from './testAssets/mockServiceBroker'
import { calcDescriptorMsgPackBE32, calcDescriptorUtf8Http } from './testAssets/testUtilities'

describe('IpcRelayServiceBroker', function () {
	let innerServer: IServiceBroker

	beforeEach(function () {
		innerServer = new MockServiceBroker()
	})

	describe('handshake', function () {
		it('without IPC pipes', async function () {
			const client = await getRemoteClientProxy()
			try {
				await expect(client.handshake({ supportedConnections: RemoteServiceConnections.Multiplexing })).rejects.toThrow()
			} finally {
				client.dispose()
			}
		})

		it('with IPC pipes', async function () {
			const client = await getRemoteClientProxy()
			try {
				await client.handshake({ supportedConnections: RemoteServiceConnections.IpcPipe })
				await client.handshake({ supportedConnections: RemoteServiceConnections.Multiplexing | RemoteServiceConnections.IpcPipe })
			} finally {
				client.dispose()
			}
		})
	})

	describe('requestServiceChannel', function () {
		it('non-existent service', async function () {
			const serviceBroker = await getServiceBroker()
			try {
				const pipe = await serviceBroker.getPipe(calcDescriptorMsgPackBE32.moniker)
				expect(pipe).toBeNull()
			} finally {
				serviceBroker.dispose()
			}
		})

		it('service factory throws', async function () {
			const serviceBroker = await getServiceBroker()
			try {
				await expect(() => serviceBroker.getPipe({ name: 'throws' })).rejects.toThrow()
			} finally {
				serviceBroker.dispose()
			}
		})

		it('returns a pipe name', async function () {
			const client = await getRemoteClientProxy()
			try {
				const connectionInfo = await client.requestServiceChannel(calcDescriptorUtf8Http.moniker)
				expect(connectionInfo.multiplexingChannelId).toBeUndefined()
				expect(connectionInfo.pipeName).toBeTruthy()
				expect(connectionInfo.requestId).toBeTruthy()
			} finally {
				client.dispose()
			}
		})

		it('[CWE-377] uses a private temp directory for relay sockets', async function () {
			const client = await getRemoteClientProxy()
			try {
				const connectionInfo = await client.requestServiceChannel(calcDescriptorUtf8Http.moniker)
				expect(connectionInfo.pipeName).toBeTruthy()
				expect(connectionInfo.requestId).toBeTruthy()

				if (process.platform !== 'win32') {
					const pipeDirectory = path.dirname(connectionInfo.pipeName!)
					const relativePipeDirectory = path.relative(tmpdir(), pipeDirectory)
					expect(relativePipeDirectory).toBeTruthy()
					expect(relativePipeDirectory.startsWith('..')).toBe(false)
					expect(path.isAbsolute(relativePipeDirectory)).toBe(false)
					expect(statSync(pipeDirectory).mode & 0o777).toBe(0o700)
				}

				await client.cancelServiceRequest(connectionInfo.requestId!)
			} finally {
				client.dispose()
			}
		})

		it('[CWE-377] closes the relay server after the first accepted connection', async function () {
			const pipe = new PassThrough()
			const pipeSpy = jest.spyOn(pipe, 'pipe')
			const serviceBroker = new MockServiceBroker()
			jest.spyOn(serviceBroker, 'getPipe').mockResolvedValue(pipe)
			const relay = new IpcRelayServiceBroker(serviceBroker)
			let firstSocket: Socket | undefined
			let secondSocket: Socket | undefined
			try {
				const connectionInfo = await relay.requestServiceChannel(calcDescriptorUtf8Http.moniker)
				expect(connectionInfo.pipeName).toBeTruthy()

				firstSocket = await connectToPipe(connectionInfo.pipeName!)
				await waitFor(() => expect(pipeSpy).toHaveBeenCalledTimes(1))

				try {
					secondSocket = await connectToPipe(connectionInfo.pipeName!)
				} catch {
					secondSocket = undefined
				}
				await delay(50)
				expect(pipeSpy).toHaveBeenCalledTimes(1)
			} finally {
				firstSocket?.destroy()
				secondSocket?.destroy()
				pipe.destroy()
				relay.dispose()
			}
		})

		it('[CWE-377] cleans up the temp directory when listening fails', async function () {
			const relayType = IpcRelayServiceBroker as unknown as {
				createPipeDirectory: () => string
				listen: (server: unknown, pipeName: string) => Promise<void>
			}
			const listenError = new Error('listen failed')
			let pipeDirectory: string | undefined
			const createPipeDirectorySpy =
				process.platform === 'win32'
					? undefined
					: jest.spyOn(relayType, 'createPipeDirectory').mockImplementation(() => {
						pipeDirectory = mkdtempSync(path.join(tmpdir(), 'servicehub-ipc-test-'))
						return pipeDirectory
					})
			const listenSpy = jest.spyOn(relayType, 'listen').mockRejectedValue(listenError)
			const relay = new IpcRelayServiceBroker(innerServer)
			const channelMapRelay = relay as unknown as {
				channelsOfferedToClient: Record<string, unknown>
			}

			try {
				await expect(relay.requestServiceChannel(calcDescriptorUtf8Http.moniker)).rejects.toBe(listenError)
				expect(channelMapRelay.channelsOfferedToClient).toStrictEqual({})
				if (pipeDirectory) {
					expect(existsSync(pipeDirectory)).toBe(false)
				}
			} finally {
				listenSpy.mockRestore()
				createPipeDirectorySpy?.mockRestore()
				if (pipeDirectory) {
					rmSync(pipeDirectory, { recursive: true, force: true })
				}
				relay.dispose()
			}
		})

		it.skip('get a service', async function () {
			const serviceBroker = await getServiceBroker()
			try {
				const calc = await serviceBroker.getProxy<ICalculatorService>(calcDescriptorUtf8Http)
				try {
					expect(await calc?.add(1, 2)).toEqual(3)
				} finally {
					calc?.dispose()
				}
			} finally {
				serviceBroker.dispose()
			}
		})
	})

	describe('cancelServiceRequest', function () {
		it('cancels channel offer', async function () {
			const client = await getRemoteClientProxy()
			try {
				const channel = await client.requestServiceChannel(calcDescriptorUtf8Http.moniker)
				expect(channel.pipeName).toBeTruthy()
				expect(channel.requestId).toBeTruthy()
				await client.cancelServiceRequest(channel.requestId!)

				const connectAttempt = new Promise<void>((resolve, reject) => {
					const socket = connect(channel.pipeName!)
					socket.once('connect', () => resolve())
					socket.once('error', err => reject(err))
				})
				await expect(connectAttempt).rejects.toThrow()
			} finally {
				client.dispose()
			}
		})
	})

	describe('completion', function () {
		it('client closed', async function () {
			const relay = new IpcRelayServiceBroker(innerServer)
			relay.dispose()
			await relay.completion
		})
	})

	it('repeats availabilityChanged event', async function () {
		const serviceBroker = await getServiceBroker()
		try {
			const eventRaised = new Promise<BrokeredServicesChangedArgs>(resolve => {
				serviceBroker.once('availabilityChanged', args => resolve(args))
			})
			innerServer.emit('availabilityChanged', { impactedServices: [{ name: 'changed' }] })
			const argsRaised = await eventRaised
			expect(argsRaised.impactedServices![0].name).toStrictEqual('changed')
		} finally {
			serviceBroker.dispose()
		}
	})

	async function getRemoteClientProxy() {
		const pair = FullDuplexStream.CreatePair()

		const relay = new IpcRelayServiceBroker(innerServer)
		FrameworkServices.remoteServiceBroker.constructRpc(relay, pair.first)

		const clientBrokerProxy = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(pair.second)
		return clientBrokerProxy
	}

	async function getServiceBroker(): Promise<IServiceBroker & IDisposable> {
		const remoteProxy = await getRemoteClientProxy()
		return await RemoteServiceBroker.connectToRemoteServiceBroker(remoteProxy)
	}

	function connectToPipe(pipeName: string): Promise<Socket> {
		return new Promise((resolve, reject) => {
			const socket = connect(pipeName)
			const timeout = setTimeout(() => {
				socket.destroy()
				reject(new Error('Timed out connecting to pipe.'))
			}, 1000)

			socket.once('connect', () => {
				clearTimeout(timeout)
				resolve(socket)
			})
			socket.once('error', err => {
				clearTimeout(timeout)
				reject(err)
			})
		})
	}

	async function waitFor(assert: () => void): Promise<void> {
		for (let attempt = 0; attempt < 10; attempt++) {
			try {
				assert()
				return
			} catch {
				await delay(10)
			}
		}

		assert()
	}

	function delay(milliseconds: number): Promise<void> {
		return new Promise(resolve => setTimeout(resolve, milliseconds))
	}
})
