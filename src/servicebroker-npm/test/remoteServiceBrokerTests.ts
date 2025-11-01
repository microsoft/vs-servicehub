import assert from 'assert'
import CancellationToken from 'cancellationtoken'
import { CancellationToken as vscodeCancellationToken } from 'vscode-jsonrpc'
import { Channel, FullDuplexStream, MultiplexingStream } from 'nerdbank-streams'
import { Deferred } from 'nerdbank-streams/js/Deferred'
import {
	Formatters,
	MessageDelimiters,
	RemoteServiceConnections,
	FrameworkServices,
	RemoteServiceBroker,
	ServiceActivationOptions,
	ServiceJsonRpcDescriptor,
	ServiceMoniker,
	BrokeredServicesChangedArgs,
	IRemoteServiceBroker,
	Observer,
	IDisposable,
	GlobalBrokeredServiceContainer,
	ServiceAudience,
	ServiceRegistration,
	MultiplexingRelayServiceBroker,
} from '../src'
import { AlwaysThrowingRemoteBroker } from './testAssets/alwaysThrowingBroker'
import { CallMeBackClient } from './testAssets/callMeBackClient'
import { ICalculatorService, IActivationService, ICallMeBackService } from './testAssets/interfaces'
import { MultiplexingRemoteServiceBroker } from './testAssets/multiplexingRemoteServiceBroker'
import { TestRemoteServiceBroker } from './testAssets/testRemoteServiceBroker'
import {
	calcDescriptorUtf8Http,
	hostMultiplexingServer,
	startCP,
	activationDescriptor,
	calcDescriptorUtf8BE32,
	calcDescriptorMsgPackBE32,
	callBackDescriptor,
} from './testAssets/testUtilities'
import { Descriptors } from './testAssets/Descriptors'
import { Calculator } from './testAssets/calculatorService'

describe.skip/*unstable*/('Service Broker tests', function () {
	let defaultTokenSource: {
		token: CancellationToken
		cancel: (reason?: any) => void
	}
	let defaultToken: CancellationToken

	beforeEach(() => {
		defaultTokenSource = CancellationToken.timeout(3000)
		defaultToken = defaultTokenSource.token
	})

	afterEach(() => {
		// release timer resource
		defaultTokenSource.cancel()
	})

	// Sometimes, the first time we start ServiceBrokerTest.exe it hangs and the test throws a CancellationError
	// We need to catch this error and close our connection to avoid tests not finishing
	describe('Tests that start service broker exe', function () {
		jest.setTimeout(615000)

		beforeEach(() => {
			defaultTokenSource = CancellationToken.timeout(15000)
			defaultToken = defaultTokenSource.token
		})

		it('Should be able to query proxy server', async function () {
			let mx: MultiplexingStream | null = null
			try {
				mx = await startCP(defaultToken)
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
				assert(s.clientMetadata, 'Server metadata should be non-null')
				const containsMx = RemoteServiceConnections.contains(s.clientMetadata!.supportedConnections, RemoteServiceConnections.Multiplexing)
				const containsClr = RemoteServiceConnections.contains(s.clientMetadata!.supportedConnections, RemoteServiceConnections.ClrActivation)
				assert(containsMx, 'Server metadata supported connections should contain multiplexing')
				assert(!containsClr, 'Should not contain local service offered')
				broker.dispose()
				assert(!mx.isDisposed, 'Multiplexing stream should not be disposed')
				await channel.completion
			} finally {
				mx?.dispose()
			}
		})

		it('Should get a proxy service with multiplexing streams and correctly perform operations', async function () {
			let mx: MultiplexingStream | null = null
			try {
				mx = await startCP(defaultToken)
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
				const proxy = await broker.getProxy<ICalculatorService>(calcDescriptorUtf8Http, undefined, defaultToken)
				const result = await proxy!.add(3, 5)
				assert.strictEqual(8, result)
				proxy!.dispose()
				broker.dispose()
				await channel.completion
			} finally {
				// operation cancelled
				mx?.dispose()
			}
		})

		it('Should get a proxy service with named pipes and correctly perform operations', async function () {
			let mx: MultiplexingStream | null = null
			try {
				mx = await startCP(defaultToken, ['--named-pipes'])
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
				const proxy = await broker.getProxy<ICalculatorService>(calcDescriptorUtf8Http, undefined, defaultToken)
				const result = await proxy!.add(3, 5)
				assert.strictEqual(8, result)
				proxy!.dispose()
				broker.dispose()
				await channel.completion
			} finally {
				// operation cancelled
				mx?.dispose()
			}
		})

		it('Should request services from remote broker with all message options', async function () {
			let mx: MultiplexingStream | null = null
			try {
				mx = await startCP(defaultToken)
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
				const activationOptions: ServiceActivationOptions | undefined = undefined

				let calcProxy = await broker.getProxy<ICalculatorService>(calcDescriptorUtf8Http, activationOptions, defaultToken)
				let result = await calcProxy!.add(3, 5)
				assert.strictEqual(result, 8, 'Should be able to add with UTF8 and Http-like headers')
				calcProxy!.dispose()

				calcProxy = await broker.getProxy(calcDescriptorUtf8BE32, activationOptions, defaultToken)
				result = await calcProxy!.add(3, 5)
				assert.strictEqual(result, 8, 'Should be able to add with UTF8 and Big Endian')
				calcProxy!.dispose()

				calcProxy = await broker.getProxy(calcDescriptorMsgPackBE32, activationOptions, defaultToken)
				result = await calcProxy!.add(3, 5)
				assert.strictEqual(result, 8, 'Should be able to add with MessagePack and Big Endian')
				calcProxy!.dispose()

				broker.dispose()
				await channel.completion
			} finally {
				mx?.dispose()
			}
		})

		it('Can interop with StreamJsonRpc while passing just 1 arg', async function () {
			let mx: MultiplexingStream | null = null
			try {
				mx = await startCP(defaultToken)
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
				const activationOptions: ServiceActivationOptions = {}

				const calcProxy = await broker.getProxy<ICalculatorService>(calcDescriptorUtf8Http, activationOptions, defaultToken)
				let result = await calcProxy!.add5(3)
				assert.strictEqual(result, 8, 'Should be able to add with UTF8 and Http-like headers')

				result = await calcProxy!.add5(3, CancellationToken.CONTINUE)
				assert.strictEqual(result, 8, 'Should be able to add with UTF8 and Http-like headers')

				const defaultCT: vscodeCancellationToken | undefined = undefined
				result = await calcProxy!.add5(3, defaultCT)
				assert.strictEqual(result, 8, 'Should be able to add with UTF8 and Http-like headers')

				broker.dispose()
				await channel.completion
			} finally {
				mx?.dispose()
			}
		})

		it('Should serialize and deserialize clientCredentials and activationOptions', async function () {
			let mx: MultiplexingStream | null = null
			try {
				mx = await startCP(defaultToken)
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
				const activationOptions: ServiceActivationOptions = {
					clientCredentials: {
						test: 'name',
					},
					activationArguments: {
						start: 'true',
					},
				}

				const serviceProxy = await broker.getProxy<IActivationService>(activationDescriptor, activationOptions, defaultToken)
				const clientCreds = await serviceProxy?.getClientCredentials()
				const activationArgs = await serviceProxy?.getActivationArguments()

				assert.strictEqual('name', clientCreds!['test'], 'Should receive client credentials with specified values.')
				assert.strictEqual('true', activationArgs!['start'], 'Should receive activation arguments from the server.')

				broker.dispose()
				await channel.completion
			} finally {
				mx?.dispose()
			}
		})

		it('Should throw if getting a pipe with a client RPC target', async function () {
			let mx: MultiplexingStream | null = null
			try {
				mx = await startCP(defaultToken)
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
				const client = new CallMeBackClient()
				const activationOptions: ServiceActivationOptions = {
					clientRpcTarget: client,
				}
				await assert.rejects(
					broker.getPipe(calcDescriptorUtf8Http.moniker, activationOptions, defaultToken),
					'Should throw if requesting pipe with client RPC server'
				)
				broker.dispose()
				await channel.completion
			} finally {
				mx?.dispose()
			}
		})

		it('Should acknowledge AvailabilityChanged event', async function () {
			let mx: MultiplexingStream | null = null
			try {
				const args = ['--event']
				mx = await startCP(defaultToken, args)
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const availabilityChanged = new Deferred<BrokeredServicesChangedArgs>()
				s.on('availabilityChanged', eventArgs => {
					availabilityChanged.resolve(eventArgs)
				})

				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)

				await s.availabilityChangedRaised
				const finalArgs = await availabilityChanged.promise
				assert(s.eventArgs, 'Should receive proper event args from server')
				assert.strictEqual(s.eventArgs?.impactedServices![0].name, 'ChangedService', 'First impacted service should be the expected service')
				assert.strictEqual(finalArgs.impactedServices![0].name, 'ChangedService', 'First impacted service should be the expected service')
				broker.dispose()
				await channel.completion
			} finally {
				mx?.dispose()
			}
		})

		it('client callback target can be invoked', async () => {
			let mx: MultiplexingStream | null = null
			try {
				mx = await startCP(defaultToken)
				const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
				const s = new MultiplexingRemoteServiceBroker(channel)
				const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
				const client = new CallMeBackClient()
				const activationOptions: ServiceActivationOptions = {
					clientRpcTarget: client,
				}
				const proxy = await broker.getProxy<ICallMeBackService>(callBackDescriptor, activationOptions, defaultToken)
				const msg = 'my message'
				await proxy?.callMeBack(msg)
				expect(client.lastMessage).toEqual(msg)
				broker.dispose()
				await channel.completion
			} finally {
				mx?.dispose()
			}
		})

		describe('IObserver<T> interop with .NET process', () => {
			it('with successful end', async function () {
				let mx: MultiplexingStream | null = null
				try {
					mx = await startCP(defaultToken)
					const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
					const s = new MultiplexingRemoteServiceBroker(channel)
					const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
					const proxy = await broker.getProxy<ICalculatorService>(calcDescriptorUtf8BE32, undefined, defaultToken)
					assert(proxy)
					try {
						let observer: Observer<number> | undefined
						const valuesPromise = new Promise<number[]>(async (resolve, reject) => {
							const values: number[] = []
							observer = new Observer<number>(
								value => values.push(value),
								error => {
									if (error) {
										reject(error)
									} else {
										resolve(values)
									}
								}
							)
						})
						let disposed = false
						const disposableObservable = observer as unknown as IDisposable
						disposableObservable.dispose = () => {
							disposed = true
						}
						await proxy.observeNumbers(observer!, 3, false)
						assert(disposed)
						assert.deepEqual(await valuesPromise, [1, 2, 3])
					} finally {
						proxy.dispose()
					}
					broker.dispose()
					await channel.completion
				} finally {
					mx?.dispose()
				}
			})

			it('with failure end', async function () {
				let mx: MultiplexingStream | null = null
				try {
					mx = await startCP(defaultToken)
					const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
					const s = new MultiplexingRemoteServiceBroker(channel)
					const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
					const proxy = await broker.getProxy<ICalculatorService>(calcDescriptorUtf8BE32, undefined, defaultToken)
					await assert.rejects(
						new Promise<number[]>(async (resolve, reject) => {
							const values: number[] = []
							const observer = new Observer<number>(
								value => values.push(value),
								error => {
									if (error) {
										reject(error)
									} else {
										resolve(values)
									}
								}
							)
							let disposed = false
							const disposableObservable = observer as unknown as IDisposable
							disposableObservable.dispose = () => {
								disposed = true
							}
							await proxy?.observeNumbers(observer, 3, true)
							assert(disposed)
						})
					)
					broker.dispose()
					await channel.completion
				} finally {
					mx?.dispose()
				}
			})
		})
	})

	describe('Tests that do not start an external process', function () {
		it('Should connect to multiplexing server', async function () {
			const s = new TestRemoteServiceBroker()
			const pipe = FullDuplexStream.CreatePair()
			const serverPromise = hostMultiplexingServer(pipe.first, _ => s, defaultToken)
			const broker = await RemoteServiceBroker.connectToMultiplexingDuplex(pipe.second, undefined, defaultToken)
			assert(s.clientMetadata, 'Client metadata should not be null after connection occurs')
			const containsMx = RemoteServiceConnections.contains(s.clientMetadata!.supportedConnections, RemoteServiceConnections.Multiplexing)
			const containsClr = RemoteServiceConnections.contains(s.clientMetadata!.supportedConnections, RemoteServiceConnections.ClrActivation)
			assert(containsMx, 'Should contain multiplexing as supported connection')
			assert(!containsClr, 'Should not contain local service offered')
			assert(!broker.isDisposed, 'Broker should not yet have been disposed')
			broker.dispose()
			assert(broker.isDisposed, 'Server has been properly disposed')
			await serverPromise
		})

		it('Should suppress MultiplexingStream on remote service requests', async function () {
			// Set up the server container.
			const serverContainer = new GlobalBrokeredServiceContainer()
			registerCommonServices(serverContainer)
			serverContainer.profferServiceFactory(Descriptors.calculator, () => new Calculator())

			// Establish a connection between them.
			const pipe = FullDuplexStream.CreatePair()
			const serverPromise = hostMultiplexingServer(
				pipe.first,
				mx => new MultiplexingRelayServiceBroker(serverContainer.getFullAccessServiceBroker(), mx, true),
				defaultToken
			)

			const clientMultiplexingStream = await MultiplexingStream.CreateAsync(pipe.second, undefined, defaultToken)
			const clientChannel = await clientMultiplexingStream.acceptChannelAsync('', undefined, defaultToken)
			const remoteServiceBroker = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(clientChannel.stream)
			const serviceBroker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(
				remoteServiceBroker,
				clientMultiplexingStream,
				defaultToken
			)

			// This is a somewhat contrived means to verify that the RemoteServiceBroker will clear out the multiplexing stream.
			// In a real-world scenario, the way this would happen is:
			// 1. An ordinary proxy request would be made for a service that is on the other end of a MultiplexingStream connection to a service host.
			// 2. The MultiplexingRelayServiceBroker would add the mxstream object on its end to the ServiceActivationOptions and forward the request to some IServiceBroker.
			// 3. The IServiceBroker happens to be a client of remote services, so the ServiceActivationOptions get re-serialized.
			// When this works, the RemoteServiceBroker should have deleted the mxstream object from the ServiceActivationOptions since it isn't serializable.
			const clientServicePipe = await serviceBroker.getPipe(
				Descriptors.calculator.moniker,
				{ multiplexingStream: clientMultiplexingStream },
				defaultToken
			)
			expect(clientServicePipe).toBeTruthy()
			clientServicePipe?.end()

			// Do it again with getProxy this time.
			const calc = await serviceBroker.getProxy<ICalculatorService>(
				Descriptors.calculator,
				{ multiplexingStream: clientMultiplexingStream },
				defaultToken
			)
			expect(calc).toBeTruthy()
			calc?.dispose()

			remoteServiceBroker.dispose()
			clientChannel.dispose()
			await clientChannel.completion
			clientMultiplexingStream.dispose()
			await serverPromise

			function registerCommonServices(container: GlobalBrokeredServiceContainer) {
				return container.register([
					{
						moniker: Descriptors.calculator.moniker,
						registration: new ServiceRegistration(ServiceAudience.local, false),
					},
				])
			}
		})

		it('Should handle a disconnecting client', async function () {
			const s = new TestRemoteServiceBroker()
			const pipe = FullDuplexStream.CreatePair()
			const serverDisconnectingSource = CancellationToken.create()
			const serverPromise = hostMultiplexingServer(pipe.first, _ => s, serverDisconnectingSource.token)
			const broker = await RemoteServiceBroker.connectToMultiplexingDuplex(pipe.second, undefined, defaultToken)
			serverDisconnectingSource.cancel()
			broker.dispose()
			await serverPromise
			assert(s.isDisposed, 'A disconnecting server should dispose client broker')
		})

		it('Should handle a disconnecting RPC-constructed client', async function () {
			const s = new TestRemoteServiceBroker()
			const pipe = FullDuplexStream.CreatePair()
			const serverDisconnectingSource = CancellationToken.create()
			const serverPromise = hostMultiplexingServer(pipe.first, _ => s, serverDisconnectingSource.token)

			const clientMxStream = await MultiplexingStream.CreateAsync(pipe.second)
			const hostChannel: Channel = await clientMxStream.acceptChannelAsync('')
			const serviceBroker = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(hostChannel.stream)
			const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(serviceBroker, clientMxStream, defaultToken)
			serverDisconnectingSource.cancel()
			broker.dispose()
			await serverPromise
			assert(s.isDisposed, 'A disconnecting server should dispose client')
		})

		it.skip('Should fail if handshake fails', async function () {
			const s = new AlwaysThrowingRemoteBroker()
			const pipes = FullDuplexStream.CreatePair()
			const serverPromise = hostMultiplexingServer(pipes.first, _ => s, defaultToken)

			const firstCompletion = new Deferred<void>()
			const secondCompletion = new Deferred<void>()
			pipes.first.on('close', () => firstCompletion.resolve())
			pipes.second.on('close', () => secondCompletion.resolve())
			await assert.rejects(
				RemoteServiceBroker.connectToMultiplexingDuplex(pipes.second, undefined, defaultToken),
				'Connecting to server should throw if handshake throws'
			)
			await serverPromise

			// should close both pipes
			await firstCompletion
			await secondCompletion
		})

		it.skip('Should fail if connecting to a proxy fails', async function () {
			const s = new AlwaysThrowingRemoteBroker()
			const pipes = FullDuplexStream.CreatePair()
			const serverPromise = hostMultiplexingServer(pipes.first, _ => s, defaultToken)

			const clientMxStream = await MultiplexingStream.CreateAsync(pipes.second, undefined, defaultToken)
			await assert.rejects(
				RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, clientMxStream, defaultToken),
				'Always throwing server should force a fail'
			)
			assert(!clientMxStream.isDisposed, 'Multiplexing stream should still be open')
			await assert.rejects(serverPromise, 'Should throw cancellation exception from the server')
			s.dispose()
		})

		it('Should be able to connect to server from pipe pair', async function () {
			const s = new TestRemoteServiceBroker()
			const pipes = FullDuplexStream.CreatePair()
			FrameworkServices.remoteServiceBroker.constructRpc(s, pipes.first)
			const broker = await RemoteServiceBroker.connectToDuplex(pipes.second, defaultToken)

			// Supported connections should not include multiplexing stream since we didn't provide one to the client.
			assert(s.clientMetadata, 'Server metadata should be non-null')
			expect(s.clientMetadata.supportedConnections).toEqual(RemoteServiceConnections.IpcPipe)

			const firstCompletion = new Deferred<void>()
			const secondCompletion = new Deferred<void>()
			pipes.first.on('close', () => firstCompletion.resolve())
			pipes.second.on('close', () => secondCompletion.resolve())
			broker.dispose()

			// should close both pipes
			await firstCompletion
			await secondCompletion
		})

		it('Should fail if handshake fails for pipe server', async function () {
			const s = new AlwaysThrowingRemoteBroker()
			const pipes = FullDuplexStream.CreatePair()
			FrameworkServices.remoteServiceBroker.constructRpc(s, pipes.first)
			const firstCompletion = new Deferred<void>()
			const secondCompletion = new Deferred<void>()
			pipes.first.on('close', () => firstCompletion.resolve())
			pipes.second.on('close', () => secondCompletion.resolve())
			await assert.rejects(RemoteServiceBroker.connectToDuplex(pipes.second, defaultToken), 'Always throwing server should throw on connection attempt')

			// should close both pipes
			await firstCompletion
			await secondCompletion
		})

		it('Should be able to connect to server and dipose', async function () {
			const s = new TestRemoteServiceBroker()
			const broker = await RemoteServiceBroker.connectToRemoteServiceBroker(s, defaultToken)
			assert(s.clientMetadata, 'Server metadata should not be null')
			broker.dispose()
			assert(s.isDisposed, 'Should have disposed remote server')
		})

		it('Should dispose server if handshake fails', async () => {
			const s = new AlwaysThrowingRemoteBroker()
			await assert.rejects(RemoteServiceBroker.connectToRemoteServiceBroker(s, defaultToken), 'Connecting to always throwing server should throw')
			assert(s.isDisposed, 'Should have disposed remote server on connection failure')
		})

		it('Should return null pipe if requesting a non-existant service', async function () {
			const s = new TestRemoteServiceBroker()
			const pipes = FullDuplexStream.CreatePair()
			FrameworkServices.remoteServiceBroker.constructRpc(s, pipes.first)
			const broker = await RemoteServiceBroker.connectToDuplex(pipes.second, defaultToken)
			const pipe = await broker.getPipe({ name: 'does not exist' }, undefined, defaultToken)
			assert.strictEqual(pipe, null, 'Pipe to non-existant service should be undefined')
			broker.dispose()
		})

		it('Should return undefined object if requesting a non-existant proxy service', async function () {
			const s = new TestRemoteServiceBroker()
			const pipes = FullDuplexStream.CreatePair()
			FrameworkServices.remoteServiceBroker.constructRpc(s, pipes.first)
			const broker = await RemoteServiceBroker.connectToDuplex(pipes.second, defaultToken)
			const nonexistantDescriptor = new ServiceJsonRpcDescriptor({ name: 'hehe this is fake' }, Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
			const proxy = await broker.getProxy(nonexistantDescriptor, undefined, defaultToken)
			assert.strictEqual(proxy, null, 'Should return undefined proxy to fake service')
			broker.dispose()
		})

		it('Should return proxy to service over named pipe', async function () {
			const s = new TestRemoteServiceBroker()
			const broker = await RemoteServiceBroker.connectToRemoteServiceBroker(s, defaultToken)
			const proxy = await broker.getProxy<ICalculatorService>(calcDescriptorUtf8Http, undefined, defaultToken)
			expect(proxy).toBeTruthy()
			proxy?.dispose()
			broker.dispose()
		})

		it('Should throw after disposal', async function () {
			const s = new TestRemoteServiceBroker()
			const pipes = FullDuplexStream.CreatePair()
			FrameworkServices.remoteServiceBroker.constructRpc(s, pipes.first)
			const broker = await RemoteServiceBroker.connectToDuplex(pipes.second, defaultToken)
			broker.dispose()

			await expect(() => broker.getProxy(calcDescriptorUtf8Http, undefined, defaultToken)).rejects.toThrow()
		})

		it('Should emit and listen for availabilityChanged event', async function () {
			const broker = new TestRemoteServiceBroker()
			try {
				const availabilityChanged: Deferred<BrokeredServicesChangedArgs> = new Deferred<BrokeredServicesChangedArgs>()

				// Listen for availabilityChanged event
				broker.on('availabilityChanged', args => {
					availabilityChanged.resolve(args)
				})

				// Fire availabilityChanged event
				const changedArgs: BrokeredServicesChangedArgs = {
					impactedServices: [ServiceMoniker.create('MyService')],
				}
				broker.emit('availabilityChanged', changedArgs)

				// Assert events have fired
				const receivedArgs = await availabilityChanged.promise
				assert(receivedArgs, 'Should have received event arguments when event is fired.')
				assert.strictEqual(
					receivedArgs?.impactedServices![0].name,
					changedArgs.impactedServices![0].name,
					'Should have received proper availabilityChanged event'
				)
			} finally {
				broker.dispose()
			}
		})
	})
})
