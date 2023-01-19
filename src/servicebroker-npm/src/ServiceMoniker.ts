/**
 * Describes the service that is being requested
 */
export interface ServiceMoniker {
	/** The well-known name of the service being requested. */
	name: string

	/** The version of the service being requested. */
	version?: string
}

export namespace ServiceMoniker {
	/**
	 * Creates a new, immutable ServiceMoniker.
	 * @param name The name of the service.
	 * @param version An optional version for the service.
	 */
	export function create(name: string, version?: string): Readonly<ServiceMoniker> {
		if (!name || name === '') {
			throw new Error('name cannot be empty.')
		}

		return Object.freeze({ name, version, toString: toStringHelper })
	}

	/**
	 * Tests value equality of two monikers.
	 * @param moniker1 The first moniker.
	 * @param moniker2 The second moniker.
	 */
	export function equals(moniker1?: ServiceMoniker | null, moniker2?: ServiceMoniker | null): boolean {
		if (moniker1 === moniker2) {
			return true
		}

		if (!moniker1 || !moniker2) {
			return false
		}

		return moniker1.name === moniker2.name && moniker1.version === moniker2.version
	}

	/**
	 * Formats a ServiceMoniker as a string.
	 * @param moniker The moniker to format as a string.
	 */
	export function toString(moniker: ServiceMoniker) {
		if (moniker.version) {
			return `${moniker.name} (${moniker.version})`
		} else {
			return moniker.name
		}
	}

	function toStringHelper(this: ServiceMoniker) {
		return toString(this)
	}
}
