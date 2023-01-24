import { FullDuplexStream } from 'nerdbank-streams'
import { connect } from 'net'
import path from 'path'
import { BrokeredServicesChangedArgs } from '../src/BrokeredServicesChangedArgs'
import { PIPE_NAME_PREFIX, RemoteServiceConnections } from '../src/constants'
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

		it('get a service', async function () {
			const serviceBroker = await getServiceBroker()
			const calc = await serviceBroker.getProxy<ICalculatorService>(calcDescriptorUtf8Http)
			expect(await calc?.add(1, 2)).toEqual(3)
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
					const socket = connect(path.join(PIPE_NAME_PREFIX, channel.pipeName!))
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
})
