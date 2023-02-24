import assert from 'assert'
import CancellationToken from 'cancellationtoken'
import { IRemoteServiceBroker } from '../src/IRemoteServiceBroker'
import { FullDuplexStream } from 'nerdbank-streams'
import { Formatters, MessageDelimiters, RemoteServiceConnections } from '../src/constants'
import { ServiceBrokerClientMetadata } from '../src/ServiceBrokerClientMetadata'
import { ServiceJsonRpcDescriptor } from '../src/ServiceJsonRpcDescriptor'
import { ServiceMoniker } from '../src/ServiceMoniker'
import { Calculator } from './testAssets/calculatorService'
import { IAppleTreeService, ApplePickedEventArgs, ICalculatorService, ICallMeBackClient, ICallMeBackService, IWaitToBeCanceled } from './testAssets/interfaces'
import { TestRemoteServiceBroker } from './testAssets/testRemoteServiceBroker'
import { IDisposable } from '../src/IDisposable'
import { appleTreeDescriptor, calcDescriptorUtf8Http, callBackDescriptor, cancellationWaiter } from './testAssets/testUtilities'
import { CallMeBackService } from './testAssets/callMeBackService'
import { CallMeBackClient } from './testAssets/callMeBackClient'
import { WaitToBeCanceledService } from './testAssets/waitToBeCanceledService'
import { AppleTree } from './testAssets/appleTreeService'

describe('ServiceJsonRpcDescriptor', function () {
	it('Should set properties provided in constructor', function () {
		const info = new ServiceJsonRpcDescriptor(calcDescriptorUtf8Http.moniker, Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
		assert.strictEqual(info.moniker, calcDescriptorUtf8Http.moniker, 'Should set moniker from constructor')
		assert.strictEqual(info.formatter, Formatters.Utf8, 'Should set formatter from the constructor')
		assert.strictEqual(info.messageDelimiter, MessageDelimiters.HttpLikeHeaders, 'Should set message delimiter from constructor')
	})

	it('Should return use the same protocol', function () {
		assert.strictEqual(calcDescriptorUtf8Http.protocol, 'json-rpc', 'Should have protocol set to json-rpc by default')
	})

	describe('proxies', () => {
		let rpc: ICalculatorService & IDisposable

		beforeEach(() => {
			const pipes = FullDuplexStream.CreatePair()
			calcDescriptorUtf8Http.constructRpc(new Calculator(), pipes.first)
			rpc = calcDescriptorUtf8Http.constructRpc<ICalculatorService>(pipes.second)
		})

		it('can make calls with 2 args', async function () {
			let result: number = await rpc.add(3, 5)
			assert.strictEqual(result, 8, 'Should be able to construct RPC and make request')

			result = await rpc.add(3, 5, CancellationToken.CONTINUE)
			assert.strictEqual(result, 8, 'Should be able to construct RPC and make request')

			rpc.dispose()
		})

		it('can make calls with 1 arg', async function () {
			let result: number = await rpc.add5(3)
			assert.strictEqual(result, 8, 'Should be able to construct RPC and make request')

			result = await rpc.add5(3, CancellationToken.CONTINUE)
			assert.strictEqual(result, 8, 'Should be able to construct RPC and make request')
			rpc.dispose()
		})

		it('reject extra undefined args', async function () {
			assert.rejects(async () => await rpc.add5(undefined as any, CancellationToken.CONTINUE))
			rpc.dispose()
		})

		it('can receive notifications', async function () {
			const pipes = FullDuplexStream.CreatePair()
			const server = new AppleTree()
			appleTreeDescriptor.constructRpc(server, pipes.first)
			const rpc = appleTreeDescriptor.constructRpc<IAppleTreeService>(pipes.second)

			const receivedPickedArgsPromise = new Promise<ApplePickedEventArgs>(resolve => rpc.once('picked', args => resolve(args)))
			await rpc.pick({ color: 'green', weight: 5 })
			const receivedPickedArgs = await receivedPickedArgsPromise
			assert.strictEqual(receivedPickedArgs.color, 'green')
			assert.strictEqual(receivedPickedArgs.weight, 5)

			const receivedGrownArgsPromise = new Promise<{ seeds: number; weight: number }>(resolve =>
				rpc.on('grown', (seeds, weight) => resolve({ seeds, weight }))
			)
			await rpc.grow(8, 5)
			const receivedGrownArgs = await receivedGrownArgsPromise
			assert.strictEqual(receivedGrownArgs.seeds, 8)
			assert.strictEqual(receivedGrownArgs.weight, 5)
		})
	})

	it('propagates CancellationToken args', async function () {
		const pipes = FullDuplexStream.CreatePair()
		const server = new WaitToBeCanceledService()
		cancellationWaiter.constructRpc(server, pipes.first)
		const rpc = cancellationWaiter.constructRpc<IWaitToBeCanceled>(pipes.second)
		const cts = CancellationToken.create()
		const rpcCall = rpc.waitForCancellation(cts.token)
		await server.methodReached
		cts.cancel()
		await rpcCall
	})

	describe('client RPC targets', () => {
		let proxyToService: ICallMeBackService & IDisposable
		let clientTarget: CallMeBackClient
		beforeEach(() => {
			const pipes = FullDuplexStream.CreatePair()

			// Set up server objects first so we can't accidentally consume direct client objects.
			// Do it within a code block so the client can't accidentally use server objects directly either.
			{
				const serverConnection = callBackDescriptor.constructRpcConnection(pipes.first)
				const proxyToClient = serverConnection.constructRpcClient<ICallMeBackClient>()
				const serverTarget = new CallMeBackService(proxyToClient)
				serverConnection.addLocalRpcTarget(serverTarget)
				serverConnection.startListening()
			}

			// Set up client objects.
			{
				const clientConnection = callBackDescriptor.constructRpcConnection(pipes.second)
				proxyToService = clientConnection.constructRpcClient<ICallMeBackService>()
				clientTarget = new CallMeBackClient()
				clientConnection.addLocalRpcTarget(clientTarget)
				clientConnection.startListening()
			}
		})

		it('can be invoked', async () => {
			const msg = 'my message'
			await proxyToService.callMeBack(msg)
			expect(clientTarget.lastMessage).toEqual(msg)
		})
	})

	it('Should close connections when disposed', function () {
		const pipes = FullDuplexStream.CreatePair()
		calcDescriptorUtf8Http.constructRpc(new Calculator(), pipes.first)
		const connection = calcDescriptorUtf8Http.constructRpc(pipes.second)
		connection.dispose()
		const readFromPipe = pipes.first.read()
		assert(!readFromPipe, 'Should not read anything from pipe once service is disposed')
	})

	it('Should have logical equality', function () {
		const info1a = new ServiceJsonRpcDescriptor(calcDescriptorUtf8Http.moniker, Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
		const info1b = new ServiceJsonRpcDescriptor(calcDescriptorUtf8Http.moniker, Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
		const moniker = ServiceMoniker.create('SomeMoniker')
		const info2 = new ServiceJsonRpcDescriptor(moniker, Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
		const info3a = new ServiceJsonRpcDescriptor(moniker, Formatters.MessagePack, MessageDelimiters.BigEndianInt32LengthHeader)
		const info3b = new ServiceJsonRpcDescriptor(moniker, Formatters.Utf8, MessageDelimiters.BigEndianInt32LengthHeader)

		assert(info1a.equals(info1b), 'Should be equal with same moniker, formatters, and message delimiters')
		assert(!info1a.equals(info2), 'Should not be equal with different monikers')
		assert(!info2.equals(info3a), 'Should not be equal with different formatter and message delimiter')
		assert(!info2.equals(info3b), 'Should not be equal with different message delimiter')
	})
})

describe('Various formatters and delimiters', function () {
	interface Parameters {
		formatter: Formatters
		delimiter: MessageDelimiters
		invalid?: boolean
	}

	const testParameters: Parameters[] = [
		{ formatter: Formatters.Utf8, delimiter: MessageDelimiters.HttpLikeHeaders },
		{ formatter: Formatters.MessagePack, delimiter: MessageDelimiters.HttpLikeHeaders, invalid: true },
		{ formatter: Formatters.Utf8, delimiter: MessageDelimiters.BigEndianInt32LengthHeader },
		{ formatter: Formatters.MessagePack, delimiter: MessageDelimiters.BigEndianInt32LengthHeader },
	]

	testParameters.forEach(value => {
		const invalidSuffix = value.invalid ? ' (invalid)' : ''
		it(`${value.formatter}+${value.delimiter}${invalidSuffix}`, async () => {
			if (value.invalid) {
				assert.throws(() => new ServiceJsonRpcDescriptor(calcDescriptorUtf8Http.moniker, value.formatter, value.delimiter))
				return
			}

			const descriptor = new ServiceJsonRpcDescriptor(calcDescriptorUtf8Http.moniker, value.formatter, value.delimiter)
			const pipes = FullDuplexStream.CreatePair()
			const serverRpc = descriptor.constructRpc(new Calculator(), pipes.first)
			const clientRpc = descriptor.constructRpc<ICalculatorService>(pipes.second)
			const result = await clientRpc.add(3, 5)
			assert.strictEqual(result, 8, 'Should construct RPC and make request')
			clientRpc.dispose()
			pipes.first.end()
			pipes.second.end()
			serverRpc.dispose()
		})
	})
})

describe('Remote Service Broker Service RPC Tests', function () {
	it('Should be able to compute with a local RPC target', async function () {
		const info = new ServiceJsonRpcDescriptor(ServiceMoniker.create('RemoteServiceBroker'), Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
		const localTarget = new TestRemoteServiceBroker()
		const pipes = FullDuplexStream.CreatePair()
		const rpc = info.constructRpc<IRemoteServiceBroker>(pipes.first)
		info.constructRpc(localTarget, pipes.second)
		const clientMetadata: ServiceBrokerClientMetadata = { supportedConnections: RemoteServiceConnections.None }
		await rpc.handshake(clientMetadata)
		assert(localTarget.clientMetadata, 'Handshake request should have set client metadata')
		rpc.dispose()
	})
})
