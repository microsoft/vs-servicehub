import { IWaitToBeCanceled } from './interfaces'
import CancellationToken from 'cancellationtoken'
import { Deferred } from 'nerdbank-streams/js/Deferred'

export class WaitToBeCanceledService implements IWaitToBeCanceled {
	private readonly methodReachedSource: Deferred<void>
	readonly methodReached: Promise<void>

	constructor() {
		this.methodReachedSource = new Deferred<void>()
		this.methodReached = this.methodReachedSource.promise
	}

	WaitForCancellation(cancellationToken: CancellationToken): Promise<void> {
		return new Promise<void>((resolve, reject) => {
			cancellationToken.onCancelled(() => resolve())

			// Inform the test client that we've reached this point
			// so it can cancel the token.
			this.methodReachedSource.resolve()
		})
	}
}
