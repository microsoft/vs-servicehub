import assert from 'assert'
import CancellationToken from 'cancellationtoken'
import { IAuthorizationService } from './IAuthorizationService'
import { IDisposable } from './IDisposable'
import { ProtectedOperation } from './ProtectedOperation'

/**
 * A caching client of the [IAuthorizationService](#IAuthorizationService)
 */
export class AuthorizationServiceClient implements IDisposable {
	/**
	 * Indicates if the client has already been disposed
	 */
	private isDisposed: boolean = false

	/**
	 * Initializes a new instance of the [AuthorizationServiceClient](#AuthorizationServiceClient) class
	 * @param authService The authorization service to use with requests
	 * @param ownsAuthService Indicates if the client owns the authorization service
	 */
	public constructor(private authService: IAuthorizationService, private ownsAuthService: boolean = true) {
		assert(authService)

		this.authService = authService
	}

	/**
	 * Gets the current user's credentials
	 * @param cancellationToken A cancellation token
	 */
	public async getCredentials(cancellationToken: CancellationToken = CancellationToken.CONTINUE): Promise<{ [key: string]: string }> {
		if (this.isDisposed) {
			throw new Error('Object is disposed')
		}

		// TODO: add caching here.
		return await this.authService.getCredentials(cancellationToken)
	}

	/**
	 * Checks if a user is authorized to perform the operation
	 * @param operation The operation to be performed
	 * @param cancellationToken A cancellation token
	 */
	public async checkAuthorization(operation: ProtectedOperation, cancellationToken: CancellationToken = CancellationToken.CONTINUE): Promise<boolean> {
		assert(operation)
		if (this.isDisposed) {
			throw new Error('Object is disposed')
		}

		// TODO: add caching here.
		return await this.authService.checkAuthorization(operation, cancellationToken)
	}

	/**
	 * Disposes the authorization client and its underlying resources
	 */
	public dispose(): void {
		this.isDisposed = true
		if (this.ownsAuthService) {
			this.authService.dispose()
		}
	}
}
