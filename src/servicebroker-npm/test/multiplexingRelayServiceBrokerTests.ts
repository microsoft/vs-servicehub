import { RemoteServiceConnections } from '../src/constants'
import { FrameworkServices } from '../src/FrameworkServices'
import { IDisposable } from '../src/IDisposable'
import { IRemoteServiceBroker } from '../src/IRemoteServiceBroker'
import { IServiceBroker } from '../src/IServiceBroker'
import { MultiplexingRelayServiceBroker } from '../src/MultiplexingRelayServiceBroker'
import { FullDuplexStream, MultiplexingStream } from 'nerdbank-streams'
import { RemoteServiceBroker } from '../src/RemoteServiceBroker'
import { ICalculatorService } from './testAssets/interfaces'
import { MockServiceBroker } from './testAssets/mockServiceBroker'
import { calcDescriptorMsgPackBE32, calcDescriptorUtf8Http } from './testAssets/testUtilities'
import CancellationToken from 'cancellationtoken'
import { BrokeredServicesChangedArgs } from '../src/BrokeredServicesChangedArgs'

describe('MultiplexingRelayServiceBroker', function () {
	let innerServer: IServiceBroker

	beforeEach(function () {
		innerServer = new MockServiceBroker()
	})

	describe('connectToServer', function () {
		it('success case', async function () {
			const client = await getRelayClient()
			await assertCalculatorService(client)
		})

		it('canceled', async function () {
			const pair = FullDuplexStream.CreatePair()
			await expect(() =>
				MultiplexingRelayServiceBroker.connectToServer(new MockServiceBroker(), pair.first, CancellationToken.CANCELLED)
			).rejects.toThrowError(CancellationToken.CancellationError)
		})
	})

	describe('constructor', function () {
		it('owned mxstream', async function () {
			const pair = FullDuplexStream.CreatePair()
			const clientTask = RemoteServiceBroker.connectToMultiplexingDuplex(pair.first)
			const serverTask = MultiplexingStream.CreateAsync(pair.second)
			const serverMxStream = await serverTask
			const relayChannel = await serverMxStream.offerChannelAsync('')
			const relay = new MultiplexingRelayServiceBroker(innerServer, serverMxStream, true)
			FrameworkServices.remoteServiceBroker.constructRpc(relay, relayChannel)

			await assertCalculatorService(await clientTask)
		})
	})

	describe('requestServiceChannel', function () {
		it('non-existent service', async function () {
			const client = await getRelayClient()
			const pipe = await client.getPipe(calcDescriptorMsgPackBE32.moniker)
			expect(pipe).toBeNull()
		})
	})

	describe('cancelServiceRequest', function () {
		it('cancels channel offer', async function () {
			const pair = FullDuplexStream.CreatePair()
			const relayTask = MultiplexingRelayServiceBroker.connectToServer(innerServer, pair.second)
			const clientMx = await MultiplexingStream.CreateAsync(pair.first)
			const clientChannel = await clientMx.acceptChannelAsync('')
			await relayTask

			const clientBrokerProxy = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(clientChannel)
			const connectionInfo = await clientBrokerProxy.requestServiceChannel(calcDescriptorUtf8Http.moniker)
			await clientBrokerProxy.cancelServiceRequest(connectionInfo.requestId!)

			// Assert that the offer for the calculator channel is rescinded.
			expect(() => clientMx.acceptChannel(connectionInfo.multiplexingChannelId!)).toThrow()
		})
	})

	describe('completion', function () {
		it('client closed', async function () {
			const pair = FullDuplexStream.CreatePair()
			const clientTask = RemoteServiceBroker.connectToMultiplexingDuplex(pair.first)
			const serverTask = MultiplexingStream.CreateAsync(pair.second)
			const serverMxStream = await serverTask
			const relayChannel = await serverMxStream.offerChannelAsync('')
			const relay = new MultiplexingRelayServiceBroker(innerServer, serverMxStream, true)
			FrameworkServices.remoteServiceBroker.constructRpc(relay, relayChannel)
			relayChannel.dispose()
			await expect(clientTask).rejects.toThrow()
			await relay.completion
		})
	})

	describe('handshake', function () {
		it('without multiplexing', async function () {
			const client = await getRemoteClientProxy()
			await expect(client.handshake({ supportedConnections: RemoteServiceConnections.IpcPipe })).rejects.toThrow()
		})

		it('with multiplexing', async function () {
			const client = await getRemoteClientProxy()
			await client.handshake({ supportedConnections: RemoteServiceConnections.Multiplexing })
			await client.handshake({ supportedConnections: RemoteServiceConnections.Multiplexing | RemoteServiceConnections.IpcPipe })
		})
	})

	describe('availabilityChanged', function () {
		it('forwards events across RPC', async function () {
			const client = await getRelayClient()
			const eventRaised = new Promise<BrokeredServicesChangedArgs>(async resolve => {
				client.on('availabilityChanged', args => resolve(args))
			})
			expect(await client.getPipe({ name: 'calc' })).toBeNull()
			innerServer.emit('availabilityChanged', { impactedServices: [{ name: 'calc' }] })
			await eventRaised
		})
	})

	async function getRelayClient() {
		const pair = FullDuplexStream.CreatePair()
		const clientTask = RemoteServiceBroker.connectToMultiplexingDuplex(pair.first)
		const relayTask = MultiplexingRelayServiceBroker.connectToServer(innerServer, pair.second)
		await relayTask
		return await clientTask
	}

	async function getRemoteClientProxy() {
		const pair = FullDuplexStream.CreatePair()
		const relayTask = MultiplexingRelayServiceBroker.connectToServer(innerServer, pair.second)
		const clientMx = await MultiplexingStream.CreateAsync(pair.first)
		const clientChannel = await clientMx.acceptChannelAsync('')
		await relayTask

		const clientBrokerProxy = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(clientChannel)
		return clientBrokerProxy
	}

	async function assertCalculatorService(serviceBroker: IServiceBroker | (IServiceBroker & IDisposable)) {
		try {
			const rpc = await serviceBroker.getProxy<ICalculatorService>(calcDescriptorUtf8Http)
			expect(rpc).toBeTruthy()
			expect(await rpc?.add(3, 5)).toEqual(8)
			rpc?.dispose()
		} finally {
			if (IDisposable.is(serviceBroker)) {
				;(serviceBroker as IDisposable).dispose()
			}
		}
	}
})
