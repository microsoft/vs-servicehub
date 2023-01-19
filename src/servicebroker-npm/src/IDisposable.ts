/**
 * Describes an object that can destroy itself and its resources
 */
export interface IDisposable {
	/**
	 * Destroys the object and its underlying resources
	 */
	dispose(): void
}

export module IDisposable {
	/**
	 * Tests whether a given object is disposable.
	 * @param value the value to be tested.
	 * @returns true if the object is disposable
	 */
	export function is(value: {}): value is IDisposable {
		return typeof (value as any).dispose === 'function'
	}
}
