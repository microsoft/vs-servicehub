import { ServiceMoniker } from '../ServiceMoniker'
import { IBrokeredServiceContainer } from './IBrokeredServiceContainer'
import { ServiceAudience } from './ServiceAudience'

export class ServiceRegistration {
	/**
	 * Initializes a new instance of the ServiceRegistration class.
	 * @param audience the intended audiences for this service.
	 * @param allowGuestClients a value indicating whether this service is exposed to non-Owner clients.
	 * @param profferCallback an optional callback that should be invoked the first time this service is activated in order to proffer the service to the container, or a promise that resolves when the service is proffered.
	 */
	constructor(
		public readonly audience: ServiceAudience,
		public readonly allowGuestClients: boolean,
		public profferCallback?: ((container: IBrokeredServiceContainer, moniker: ServiceMoniker) => Promise<void> | void) | Promise<void>
	) {}

	/** Gets a value indicating whether this service is exposed to local clients relative to itself. */
	get isExposedLocally() {
		return (this.audience & ServiceAudience.local) !== ServiceAudience.none
	}

	/** Gets a value indicating whether this service is exposed to remote clients relative to itself. */
	get isExposedRemotely() {
		return (this.audience & ServiceAudience.liveShareGuest) !== ServiceAudience.none
	}

	isExposedTo(consumingAudience: ServiceAudience) {
		return (consumingAudience & this.audience) === consumingAudience
	}
}
