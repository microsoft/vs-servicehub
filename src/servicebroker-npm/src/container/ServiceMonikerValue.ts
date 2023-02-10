import stringHash from 'string-hash'
import { ServiceMoniker } from '../ServiceMoniker'

/**
 * A ServiceMoniker that includes by-value equality and hashing, so it can be used as a key in a map.
 */
export class ServiceMonikerValue implements ServiceMoniker {
	readonly name: string
	readonly version?: string
	private hash?: number
	constructor(name: string, version?: string)
	constructor(template: ServiceMoniker)
	constructor(templateOrName: ServiceMoniker | string, version?: string) {
		if (typeof templateOrName === 'string') {
			this.name = templateOrName
			this.version = version
		} else {
			this.name = templateOrName.name
			this.version = templateOrName.version
		}
	}

	equals(other: any) {
		return other && other.name === this.name && other.version === this.version
	}

	hashCode() {
		this.hash ??= stringHash(this.name) + stringHash(this.version ?? '')
		return this.hash
	}

	static from(moniker: ServiceMoniker) {
		return moniker instanceof ServiceMonikerValue ? moniker : new ServiceMonikerValue(moniker)
	}
}
