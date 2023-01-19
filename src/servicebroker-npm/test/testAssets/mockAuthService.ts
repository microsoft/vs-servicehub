import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { IAuthorizationService, AuthorizationServiceEmitter } from '../../src/IAuthorizationService'
import { ProtectedOperation } from '../../src/ProtectedOperation'

export class MockAuthService extends (EventEmitter as new () => AuthorizationServiceEmitter) implements IAuthorizationService {
	public credentialsChanged: EventEmitter = new EventEmitter()
	public authorizationChanged: EventEmitter = new EventEmitter()
	public isDisposed: boolean = false
	public authChecked: boolean = false

	public constructor(private clientCredentials: { [key: string]: string }) {
		super()
	}

	public getCredentials(cancellationToken: CancellationToken): Promise<{ [key: string]: string }> {
		return Promise.resolve(this.clientCredentials)
	}

	public checkAuthorization(operation: ProtectedOperation, cancellationToken: CancellationToken): Promise<boolean> {
		this.authChecked = true
		return Promise.resolve(true)
	}

	public updateCredentials(clientCredentials: { [key: string]: string }) {
		this.clientCredentials = clientCredentials
	}

	public dispose() {
		this.isDisposed = true
	}
}
