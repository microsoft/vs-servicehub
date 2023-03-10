import { ServiceMoniker } from './ServiceMoniker'

/**
 * The data associated with a brokered services changed event.
 */
export interface BrokeredServicesChangedArgs {
	/**
	 * The services monikers of the services that have changed
	 */
	impactedServices?: ServiceMoniker[]

	/**
	 * Indicates if other services are impacted by the changed services' availability
	 */
	otherServicesImpacted?: boolean
}
