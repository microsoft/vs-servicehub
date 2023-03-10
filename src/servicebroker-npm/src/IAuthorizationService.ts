import CancellationToken from 'cancellationtoken'
import { ProtectedOperation } from './ProtectedOperation'
import { IDisposable } from './IDisposable'
import StrictEventEmitter from 'strict-event-emitter-types'
import { EventEmitter } from 'events'

/**
 * Describes the events that can be fired from {@linkcode IAuthorizationService}
 */
export interface AuthorizationServiceEvents {
	credentialsChanged: () => void
	authorizationChanged: () => void
}

/**
 * The {@linkcode StrictEventEmitter} extended by {@linkcode IAuthorizationService}
 */
export type AuthorizationServiceEmitter = StrictEventEmitter<EventEmitter, AuthorizationServiceEvents>

/**
 * Defines an authorization service to determine user permissions.
 * Emits events on authorization and credential changes.
 */
export interface IAuthorizationService extends IDisposable, AuthorizationServiceEmitter {
	/**
	 * Gets the user's credentials
	 * @param cancellationToken A cancellation token
	 */
	getCredentials(cancellationToken?: CancellationToken): Promise<{ [key: string]: string }>

	/**
	 * Checks if a user is authorized to perform a certain operation
	 * @param operation The operation to perform
	 * @param cancellationToken A cancellation token
	 */
	checkAuthorization(operation: ProtectedOperation, cancellationToken?: CancellationToken): Promise<boolean>
}
