import assert from 'assert'
import CancellationToken from 'cancellationtoken'
import { FrameworkServices } from '../src/FrameworkServices'
import { MultiplexingStream } from 'nerdbank-streams'
import { RemoteServiceBroker } from '../src/RemoteServiceBroker'
import { ServiceActivationOptions } from '../src/ServiceActivationOptions'
import { MockAuthService } from './testAssets/mockAuthService'
import { MultiplexingRemoteServiceBroker } from './testAssets/multiplexingRemoteServiceBroker'
import { calcDescriptorUtf8Http, startCP } from './testAssets/testUtilities'

describe('Activation Options tests', function () {
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

	it('Should update activation options', async function () {
		let mx: MultiplexingStream | null = null
		try {
			mx = await startCP(defaultToken)
			const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
			const s = new MultiplexingRemoteServiceBroker(channel)
			const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
			const authService = new MockAuthService({ user1: 'authorized!' })
			broker.setAuthorizationService(authService)

			await broker.getProxy(calcDescriptorUtf8Http, undefined, defaultToken)
			assert.strictEqual(s.lastReceivedOptions?.clientCulture, 'en-US', 'clientCulture should be set to "en-US" by default')
			assert.strictEqual(s.lastReceivedOptions?.clientUICulture, 'en-US', 'clientUICulture should be set to "en-US" by default')
			assert.strictEqual(s.lastReceivedOptions?.clientCredentials!['user1'], 'authorized!', 'clientCredentials should be set to auth service credentials')

			authService.updateCredentials({ user2: 'no good' })
			await broker.getProxy(calcDescriptorUtf8Http, undefined, defaultToken)
			assert.strictEqual(
				s.lastReceivedOptions?.clientCredentials!['user2'],
				'no good',
				'clientCredentials should be updated when auth service is updated'
			)
			broker.dispose()
			mx.dispose()
			await channel.completion
		} finally {
			mx?.dispose()
		}
	})

	it('Should respect activation options when requesting service', async function () {
		let mx: MultiplexingStream | null = null
		try {
			mx = await startCP(defaultToken)
			const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
			const s = new MultiplexingRemoteServiceBroker(channel)
			const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
			const authService = new MockAuthService({ user1: 'authorized!' })
			broker.setAuthorizationService(authService)
			const activationOptions: ServiceActivationOptions = {
				clientCredentials: { user2: 'definitely authorized' },
				clientUICulture: 'es-ES',
				clientCulture: 'es-ES',
			}

			await broker.getProxy(calcDescriptorUtf8Http, activationOptions, defaultToken)
			assert.strictEqual(s.lastReceivedOptions?.clientCulture, 'es-ES', 'Should have the clientCulture set on service request')
			assert.strictEqual(s.lastReceivedOptions?.clientUICulture, 'es-ES', 'Should have the clientCulture set on service request')
			assert.strictEqual(
				s.lastReceivedOptions?.clientCredentials!['user2'],
				'definitely authorized',
				'Client credentials should contain appropriate entry for auth options from activation'
			)
			assert(!s.lastReceivedOptions?.clientCredentials!['user1'], 'Should not have auth received service credentials')

			broker.dispose()
			await channel.completion
		} finally {
			mx?.dispose()
		}
	})

	it.skip('Should still set activation options even if not explicit', async function () {
		let mx: MultiplexingStream | null = null
		try {
			mx = await startCP(defaultToken)
			const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
			const s = new MultiplexingRemoteServiceBroker(channel)
			FrameworkServices.remoteServiceBroker.constructRpc(s, channel)
			const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
			const authService = new MockAuthService({ user1: 'authorized!' })
			broker.setAuthorizationService(authService)

			await broker.getPipe(calcDescriptorUtf8Http.moniker, undefined, defaultToken)
			assert.strictEqual(s.lastReceivedOptions?.clientCulture, 'en-US', 'clientCulture should be set to "en-US" by default')
			assert.strictEqual(s.lastReceivedOptions?.clientUICulture, 'en-US', 'clientUICulture should be set to "en-US" by default')
			assert.strictEqual(s.lastReceivedOptions?.clientCredentials!['user1'], 'authorized!', 'clientCredentials should be set to auth service credentials')

			authService.updateCredentials({ user2: 'no good' })
			await broker.getPipe(calcDescriptorUtf8Http.moniker, undefined, defaultToken)
			assert.strictEqual(s.lastReceivedOptions?.clientCulture, 'en-US', 'clientCulture should be set to "en-US" by default')
			assert.strictEqual(s.lastReceivedOptions?.clientUICulture, 'en-US', 'clientUICulture should be set to "en-US" by default')
			assert.strictEqual(
				s.lastReceivedOptions?.clientCredentials!['user2'],
				'no good',
				'clientCredentials should have been updated to newest credentials'
			)
			assert(
				!Object.keys(s.lastReceivedOptions?.clientCredentials!).includes('user1'),
				'clientCredentials should have been updated and the first credential removed'
			)
			broker.dispose()
			await channel.completion
		} finally {
			mx?.dispose()
		}
	})

	it.skip("Should use broker's activation options", async function () {
		let mx: MultiplexingStream | null = null
		try {
			mx = await startCP(defaultToken)
			const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
			const s = new MultiplexingRemoteServiceBroker(channel)
			FrameworkServices.remoteServiceBroker.constructRpc(s, channel)
			const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
			const authService = new MockAuthService({ user1: 'authorized!' })
			broker.setAuthorizationService(authService)

			const activationOptions: ServiceActivationOptions = {
				clientCredentials: undefined,
			}
			await broker.getPipe(calcDescriptorUtf8Http.moniker, activationOptions, defaultToken)
			assert.strictEqual(
				s.lastReceivedOptions?.clientCredentials!['user1'],
				'authorized!',
				'clientCredentials should be set to auth service credentials if they were undefined'
			)

			activationOptions.clientCredentials = {}
			await broker.getPipe(calcDescriptorUtf8Http.moniker, activationOptions, defaultToken)
			assert.strictEqual(
				s.lastReceivedOptions?.clientCredentials,
				activationOptions.clientCredentials,
				'clientCredentials should be set to auth service credentials if they were an empty set'
			)
			broker.dispose()
			await channel.completion
		} finally {
			mx?.dispose()
		}
	})

	it.skip('Should use the activation options present in the service request', async function () {
		let mx: MultiplexingStream | null = null
		try {
			mx = await startCP(defaultToken)
			const channel = await mx.acceptChannelAsync('', undefined, defaultToken)
			const s = new MultiplexingRemoteServiceBroker(channel)
			const broker = await RemoteServiceBroker.connectToMultiplexingRemoteServiceBroker(s, mx, defaultToken)
			const authService = new MockAuthService({ user1: 'authorized!' })
			broker.setAuthorizationService(authService)
			const activationOptions: ServiceActivationOptions = {
				clientCredentials: { user2: 'definitely authorized' },
				clientUICulture: 'es-ES',
				clientCulture: 'es-ES',
			}

			await broker.getPipe(calcDescriptorUtf8Http.moniker, activationOptions, defaultToken)
			assert.strictEqual(s.lastReceivedOptions?.clientCulture, 'es-ES', 'Should have the clientCulture set on service request')
			assert.strictEqual(s.lastReceivedOptions?.clientUICulture, 'es-ES', 'Should have the clientCulture set on service request')
			assert.strictEqual(
				s.lastReceivedOptions?.clientCredentials!['user2'],
				'definitely authorized',
				'Client credentials should contain appropriate entry for auth options from activation'
			)
			assert(!Object.keys(s.lastReceivedOptions?.clientCredentials!).includes('user1'), 'Should not have auth received service credentials')
			broker.dispose()
			await channel.completion
		} finally {
			mx?.dispose()
		}
	})
})
