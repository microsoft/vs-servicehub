import assert from 'assert'
import CancellationToken from 'cancellationtoken'
import { AuthorizationServiceClient } from '../src/AuthorizationServiceClient'
import { ProtectedOperation } from '../src/ProtectedOperation'
import { MockAuthService } from './testAssets/mockAuthService'
import { Deferred } from 'nerdbank-streams/js/Deferred'

describe('Authorization Service Client tests', function () {
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

	it("Should dispose auth service if it owns, otherwise doesn't", function () {
		const mockAuthService = new MockAuthService({ user1: 'authorized!' })
		let authClient = new AuthorizationServiceClient(mockAuthService, false)
		authClient.dispose()
		assert(!mockAuthService.isDisposed, "Should not dispose underlying auth service if client doesn't own it")
		authClient = new AuthorizationServiceClient(mockAuthService)
		authClient.dispose()
		assert(mockAuthService.isDisposed, 'Should dispose underlying auth service it client owns it')
	})

	it('Should be able to get credentials', async function () {
		const mockAuthService = new MockAuthService({ user1: 'authorized!' })
		const authClient = new AuthorizationServiceClient(mockAuthService)
		const creds = await authClient.getCredentials(defaultToken)
		assert.equal(creds['user1'], 'authorized!', 'Should get the credentials for the given user')
		authClient.dispose()
		assert.rejects(async () => authClient.getCredentials(defaultToken), 'Should throw if attempting to get credentials after disposed')
	})

	it('Should use underlying authorization service to check operation authorization', async function () {
		const mockAuthService = new MockAuthService({ user1: 'authorized!' })
		const authClient = new AuthorizationServiceClient(mockAuthService)
		assert(!mockAuthService.authChecked, 'Should not have checked authorization yet')
		await authClient.checkAuthorization(ProtectedOperation.create('op'), defaultToken)
		assert(mockAuthService.authChecked, 'Should have used the auth service to check the authorization')
	})

	it('Should listen for events being raised', async function () {
		const mockAuthService = new MockAuthService({ user1: 'authorized!' })
		const credentialsChanged: Deferred<void> = new Deferred<void>()
		let haveCredsChanged = false
		const authorizationChanged: Deferred<void> = new Deferred<void>()
		let hasAuthChanged = false

		// Listen for credentialsChanged and authorizationChanged events
		mockAuthService.on('credentialsChanged', () => {
			haveCredsChanged = true
			credentialsChanged.resolve()
		})
		mockAuthService.on('authorizationChanged', () => {
			hasAuthChanged = true
			authorizationChanged.resolve()
		})

		// Fire credentialsChanged and authorizationChanged events
		mockAuthService.emit('credentialsChanged')
		mockAuthService.emit('authorizationChanged')

		// Assert events have fired
		await credentialsChanged
		await authorizationChanged
		assert(haveCredsChanged, 'credentialsChanged event should have been fired')
		assert(hasAuthChanged, 'authorizationChanged event should have been fired')
	})
})
