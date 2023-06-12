import {
	ServiceAudience,
	ServiceSource,
	GlobalBrokeredServiceContainer,
	ServiceRegistration,
	MultiplexingRelayServiceBroker,
	FrameworkServices,
	IDisposable,
	IRemoteServiceBroker,
	IServiceBroker,
	ServiceRpcDescriptor,
	ClientCredentialsPolicy,
} from '../src'
import { Calculator } from './testAssets/calculatorService'
import { EmptyRemoteServiceBroker } from './testAssets/emptyRemoteServiceBroker'
import { ICalculatorService } from './testAssets/interfaces'
import { FullDuplexStream, MultiplexingStream } from 'nerdbank-streams'
import { nextTick } from 'process'
import immutable from 'immutable'
import assert from 'assert'
import { Descriptors } from './testAssets/Descriptors'

describe('GlobalBrokeredServiceContainer', function () {
	let container: GlobalBrokeredServiceContainer

	beforeEach(function () {
		container = new GlobalBrokeredServiceContainer()
	})

	describe('getFullAccessServiceBroker', function () {
		it('returns an IServiceBroker', function () {
			let isb = container.getFullAccessServiceBroker()
			expect(isb).toBeTruthy()
		})
	})

	describe('getLimitedAccessServiceBroker', function () {
		it('returns an IServiceBroker', function () {
			let isb = container.getLimitedAccessServiceBroker(ServiceAudience.local, immutable.Map(), ClientCredentialsPolicy.filterOverridesRequest)
			expect(isb).toBeTruthy()
			expect(typeof isb.getProxy).toStrictEqual('function')
		})

		it('returns an IRemoteServiceBroker', function () {
			let isb = container.getLimitedAccessServiceBroker(ServiceAudience.local, immutable.Map(), ClientCredentialsPolicy.filterOverridesRequest)
			expect(isb).toBeTruthy()
			expect(typeof isb.requestServiceChannel).toStrictEqual('function')
		})

		it('filters available services', async function () {
			const offerings: { [key: string]: { audience: ServiceAudience; descriptor: ServiceRpcDescriptor } } = {
				process: { audience: ServiceAudience.process, descriptor: Descriptors.create('process calc') },
				local: { audience: ServiceAudience.local, descriptor: Descriptors.create('local calc') },
				guest: { audience: ServiceAudience.liveShareGuest, descriptor: Descriptors.create('live share calc') },
				everyone: { audience: ServiceAudience.allClientsIncludingGuests, descriptor: Descriptors.create('global calc') },
			}

			for (const name in offerings) {
				const offering = offerings[name]
				container.register([{ moniker: offering.descriptor.moniker, registration: new ServiceRegistration(offering.audience, false) }])
				container.profferServiceFactory(offering.descriptor, () => new Calculator())
			}

			const processBroker = container.getLimitedAccessServiceBroker(
				ServiceAudience.process,
				immutable.Map(),
				ClientCredentialsPolicy.filterOverridesRequest
			)
			expect(processBroker.getProxy(offerings.process.descriptor)).resolves.toBeTruthy()
			expect(processBroker.getProxy(offerings.local.descriptor)).resolves.toBeTruthy()
			expect(processBroker.getProxy(offerings.everyone.descriptor)).resolves.toBeTruthy()
			expect(processBroker.getProxy(offerings.guest.descriptor)).resolves.toBeNull()

			const localBroker = container.getLimitedAccessServiceBroker(ServiceAudience.local, immutable.Map(), ClientCredentialsPolicy.filterOverridesRequest)
			expect(localBroker.getProxy(offerings.process.descriptor)).resolves.toBeNull()
			expect(localBroker.getProxy(offerings.local.descriptor)).resolves.toBeTruthy()
			expect(localBroker.getProxy(offerings.everyone.descriptor)).resolves.toBeTruthy()
			expect(localBroker.getProxy(offerings.guest.descriptor)).resolves.toBeNull()

			const guestBroker = container.getLimitedAccessServiceBroker(
				ServiceAudience.liveShareGuest,
				immutable.Map(),
				ClientCredentialsPolicy.filterOverridesRequest
			)
			expect(guestBroker.getProxy(offerings.process.descriptor)).resolves.toBeNull()
			expect(guestBroker.getProxy(offerings.local.descriptor)).resolves.toBeNull()
			expect(guestBroker.getProxy(offerings.everyone.descriptor)).resolves.toBeTruthy()
			expect(guestBroker.getProxy(offerings.guest.descriptor)).resolves.toBeTruthy()
		})
	})

	describe('register', function () {
		it('accepts empty array', function () {
			expect(container.register([])).toBeTruthy()
		})

		it('allows unregistration', function () {
			const registration = registerCommonServices(container)
			registration.dispose()
			registration.dispose()
		})

		it('sync profferCallback called once', async function () {
			let callbackFiredCount = 0
			container.register([
				{
					moniker: Descriptors.calculator.moniker,
					registration: new ServiceRegistration(ServiceAudience.process, false, (c, mk) => {
						callbackFiredCount++
						expect(c).toBe(container)
						expect(mk).toEqual(Descriptors.calculator.moniker)
						c.profferServiceFactory(Descriptors.calculator, () => Promise.resolve(new Calculator()))
					}),
				},
			])

			const sb = container.getFullAccessServiceBroker()
			expect(callbackFiredCount).toStrictEqual(0)
			let calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc).toBeTruthy()
			expect(callbackFiredCount).toStrictEqual(1)
			calc?.dispose()
			calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc).toBeTruthy()
			expect(callbackFiredCount).toStrictEqual(1)
			calc?.dispose()
		})

		it('async profferCallback called once', async function () {
			let callbackFiredCount = 0
			container.register([
				{
					moniker: Descriptors.calculator.moniker,
					registration: new ServiceRegistration(ServiceAudience.process, false, async (c, mk) => {
						callbackFiredCount++
						expect(c).toBe(container)
						expect(mk).toEqual(Descriptors.calculator.moniker)
						await new Promise<void>(nextTick)
						c.profferServiceFactory(Descriptors.calculator, () => Promise.resolve(new Calculator()))
					}),
				},
			])

			const sb = container.getFullAccessServiceBroker()
			expect(callbackFiredCount).toStrictEqual(0)
			let calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc).toBeTruthy()
			expect(callbackFiredCount).toStrictEqual(1)
			calc?.dispose()
			calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc).toBeTruthy()
			expect(callbackFiredCount).toStrictEqual(1)
			calc?.dispose()
		})

		it('async profferCallback blocks multiple requests', async function () {
			let allowCallbackCompletion: () => void
			let callbackFiredCount = 0
			const callbackPromise = new Promise<void>(r => (allowCallbackCompletion = r))
			container.register([
				{
					moniker: Descriptors.calculator.moniker,
					registration: new ServiceRegistration(ServiceAudience.process, false, async (c, mk) => {
						callbackFiredCount++
						await callbackPromise
						c.profferServiceFactory(Descriptors.calculator, () => Promise.resolve(new Calculator()))
					}),
				},
			])

			const sb = container.getFullAccessServiceBroker()
			expect(callbackFiredCount).toStrictEqual(0)
			let calc1Promise = sb.getProxy<ICalculatorService>(Descriptors.calculator)
			let calc2Promise = sb.getProxy<ICalculatorService>(Descriptors.calculator)
			allowCallbackCompletion!()
			const calc1 = await calc1Promise
			const calc2 = await calc2Promise
			expect(calc1).toBeTruthy()
			calc1?.dispose()
			expect(calc2).toBeTruthy()
			calc2?.dispose()
			expect(callbackFiredCount).toStrictEqual(1)
		})
	})

	describe('profferServiceFactory', function () {
		let registered: IDisposable
		beforeEach(function () {
			registered = registerCommonServices(container)
		})

		it('rejects unregistered services', function () {
			expect(() => container.profferServiceFactory(Descriptors.tree, () => Promise.resolve(null))).toThrowError()
		})

		it('returns proffer disposal', function () {
			const proffered = container.profferServiceFactory(Descriptors.calculator, (mk, options, sb, ct) => Promise.resolve(new Calculator()))
			expect(proffered?.dispose).toBeTruthy()
		})

		it('invokes service factory when queried', async function () {
			let invocationCount = 0
			container.profferServiceFactory(Descriptors.calculator, (mk, options, sb, ct) => {
				invocationCount++
				return Promise.resolve(null)
			})
			expect(invocationCount).toEqual(0)
			const sb = container.getFullAccessServiceBroker()
			const calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(invocationCount).toEqual(1)
			expect(calc).toBeNull()
		})

		it('disposing result makes the service unavailable', async function () {
			let invocationCount = 0
			container
				.profferServiceFactory(Descriptors.calculator, (mk, options, sb, ct) => {
					invocationCount++
					return Promise.resolve(new Calculator())
				})
				.dispose()
			const sb = container.getFullAccessServiceBroker()
			const calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc).toBeNull()
			expect(invocationCount).toEqual(0)
		})

		it('makes local objects disposable', async function () {
			// We'll return a 'service' that obviously doesn't offer a dispose method.
			container.profferServiceFactory(Descriptors.calculator, () => Promise.resolve({}))
			const sb = container.getFullAccessServiceBroker()
			const calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)

			// The dispose method should still be part of the proxy, and it should no-op.
			expect(typeof calc?.dispose).toStrictEqual('function')
			calc?.dispose()
		})

		it('factory returns non-promise service', async function () {
			container.profferServiceFactory(Descriptors.calculator, () => new Calculator())
			const sb = container.getFullAccessServiceBroker()
			const calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc?.add(3, 2)).resolves.toStrictEqual(5)
			calc?.dispose()
		})

		it('factory returns non-promise null', async function () {
			container.profferServiceFactory(Descriptors.calculator, () => null)
			const sb = container.getFullAccessServiceBroker()
			const calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc).toBeNull()
		})
	})

	describe('profferServiceBroker', function () {
		let subContainer: GlobalBrokeredServiceContainer
		let calcService: Calculator | undefined

		beforeAll(function () {
			subContainer = new GlobalBrokeredServiceContainer()
			registerCommonServices(subContainer)
			subContainer.profferServiceFactory(Descriptors.calculator, (mk, options, sb, ct) => Promise.resolve((calcService = new Calculator())))
		})

		beforeEach(function () {
			calcService = undefined
		})

		it('requires services to be registered', async function () {
			expect(() => container.profferServiceBroker(subContainer.getFullAccessServiceBroker(), [Descriptors.calculator.moniker])).toThrow()
		})

		it('makes services available from other service broker', async function () {
			registerCommonServices(container)
			container.profferServiceBroker(subContainer.getFullAccessServiceBroker(), [Descriptors.calculator.moniker])
			const sb = container.getFullAccessServiceBroker()
			const calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			await expect(calc?.add(3, 2)).resolves.toStrictEqual(5)
		})

		it('result disposal removes services', async function () {
			registerCommonServices(container)
			container.profferServiceBroker(subContainer.getFullAccessServiceBroker(), [Descriptors.calculator.moniker]).dispose()
			const sb = container.getFullAccessServiceBroker()
			expect(sb.getProxy<ICalculatorService>(Descriptors.calculator)).resolves.toStrictEqual(null)
		})
	})

	describe('profferRemoteServiceBroker', function () {
		let subContainer: GlobalBrokeredServiceContainer
		let calcService: Calculator | undefined
		let subContainerProxy: IRemoteServiceBroker
		let localMx: MultiplexingStream

		beforeAll(function () {
			subContainer = new GlobalBrokeredServiceContainer()
			registerCommonServices(subContainer)
			subContainer.profferServiceFactory(Descriptors.calculator, (mk, options, sb, ct) => Promise.resolve((calcService = new Calculator())))
		})

		beforeEach(async function () {
			registerCommonServices(container)
			const pair = FullDuplexStream.CreatePair()

			// Start the server, but don't await on it yet since we have to start the client too.
			const setupServer = MultiplexingRelayServiceBroker.connectToServer(subContainer.getFullAccessServiceBroker(), pair.first)

			// Start the client.
			localMx = await MultiplexingStream.CreateAsync(pair.second)
			const clientChannel = await localMx.acceptChannelAsync('')
			subContainerProxy = FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(clientChannel)

			// Observe promise rejection
			await setupServer
		})

		it('ignores unregistered services', function () {
			// We proffer with a service moniker that is NOT registered, to verify that no error is thrown.
			container.profferRemoteServiceBroker(new EmptyRemoteServiceBroker(), null, ServiceSource.otherProcessOnSameMachine, [Descriptors.tree.moniker])
		})

		it('makes remote services available with listed monikers', async function () {
			container.profferRemoteServiceBroker(subContainerProxy, localMx, ServiceSource.otherProcessOnSameMachine, [Descriptors.calculator.moniker])
			const sb = container.getFullAccessServiceBroker()
			const calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc).toBeTruthy()
			await expect(calc?.add(3, 2)).resolves.toStrictEqual(5)
		})

		it('makes remote services available without listing monikers', async function () {
			container.profferRemoteServiceBroker(subContainerProxy, localMx, ServiceSource.otherProcessOnSameMachine, null)
			const sb = container.getFullAccessServiceBroker()
			const calc = await sb.getProxy<ICalculatorService>(Descriptors.calculator)
			expect(calc).toBeTruthy()
			await expect(calc?.add(3, 2)).resolves.toStrictEqual(5)
		})

		it('result disposal removes services', async function () {
			container
				.profferRemoteServiceBroker(subContainerProxy, localMx, ServiceSource.otherProcessOnSameMachine, [Descriptors.calculator.moniker])
				.dispose()
			const sb = container.getFullAccessServiceBroker()
			expect(sb.getProxy<ICalculatorService>(Descriptors.calculator)).resolves.toStrictEqual(null)
		})
	})

	describe('IServiceBroker view', function () {
		let registered: IDisposable
		let proffered: IDisposable
		let view: IServiceBroker
		let calcService: Calculator | undefined
		let calcVersionRequested: string | undefined
		beforeEach(function () {
			registered = registerCommonServices(container)
			calcService = undefined
			proffered = container.profferServiceFactory(Descriptors.calculator, (mk, options, sb, ct) => {
				calcVersionRequested = mk.version
				return (calcService = new Calculator())
			})
			view = container.getFullAccessServiceBroker()
		})

		describe('GetProxy<T>', function () {
			it('returns null for unrecognized service', async function () {
				const nonservice = await view.getProxy<ICalculatorService>(Descriptors.tree)
				expect(nonservice).toBe(null)
			})
			it('returns a locally proffered service', async function () {
				const calc = await view.getProxy<ICalculatorService>(Descriptors.calculator)
				expect(calc).toBeTruthy()
				expect(await calc!.add(5, 3)).toEqual(8)
			})
			it('returns disposable proxy', async function () {
				const calc = await view.getProxy<ICalculatorService>(Descriptors.calculator)
				expect(calc).toBeTruthy()
				expect(calcService?.isDisposed).toBeFalsy()
				calc!.dispose()
				expect(calcService?.isDisposed).toBeTruthy()
			})
		})

		describe('GetPipe', function () {
			it('returns null for unrecognized service', async function () {
				const nonservice = await view.getPipe(Descriptors.tree.moniker)
				expect(nonservice).toBe(null)
			})
			it('returns a locally proffered service', async function () {
				const { ...monikerClone } = Descriptors.calculator.moniker // clone the moniker to verify that service lookups are by value rather than by reference.
				const pipe = await view.getPipe(monikerClone)
				expect(pipe).toBeTruthy()
				const calc = Descriptors.calculator.constructRpc<ICalculatorService>(pipe!)
				expect(await calc!.add(5, 3)).toEqual(8)
			})
			it('service disposed when pipe completed', async function () {
				const pipe = await view.getPipe(Descriptors.calculator.moniker)
				expect(pipe).toBeTruthy()
				pipe?.end()
				await calcService?.disposed
			})
		})

		describe('versioned request', function () {
			let treeVersionRequested: string | undefined
			beforeEach(function () {
				container.register([
					{
						moniker: Descriptors.treeWithVersion11.moniker,
						registration: new ServiceRegistration(ServiceAudience.local, false),
					},
				])
				proffered = container.profferServiceFactory(Descriptors.treeWithVersion11, (mk, options, sb, ct) => {
					treeVersionRequested = mk.version
					return {}
				})
			})

			it('matches unversioned service', async function () {
				const pipe = await view.getPipe({ name: Descriptors.calculator.moniker.name, version: '1.0' })
				assert(pipe)
				assert.strictEqual(calcVersionRequested, '1.0')
			})

			it('does not match mismatched versioned service', async function () {
				let pipe = await view.getPipe({ name: Descriptors.treeWithVersion11.moniker.name })
				assert(!pipe)
				pipe = await view.getPipe({ name: Descriptors.treeWithVersion11.moniker.name, version: '1.2' })
				assert(!pipe)
			})

			it('matches with versioned service', async function () {
				const pipe = await view.getPipe(Descriptors.treeWithVersion11.moniker)
				assert(pipe)
				assert.strictEqual(treeVersionRequested, '1.1')
			})
		})
	})
})

function registerCommonServices(container: GlobalBrokeredServiceContainer) {
	return container.register([
		{
			moniker: Descriptors.calculator.moniker,
			registration: new ServiceRegistration(ServiceAudience.local, false),
		},
	])
}
