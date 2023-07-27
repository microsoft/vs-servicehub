import { MarshaledObjectLifetime, RpcMarshalable } from './MarshalableObject'

/**
 * An observer of some value production.
 */
export interface IObserver<T> {
	/**
	 * Notifies the observer of the next object in the sequence.
	 * @param value The next value in the observable sequence.
	 */
	onNext(value: T): void

	/**
	 * Notifies the observer that the end of the sequence has been reached, and that no more values will be produced.
	 */
	onCompleted(): void

	/**
	 * Notifies the observer that an error occurred at the value source, and that no more values will be produced.
	 * @param reason The error that occurred at the value source.
	 */
	onError(reason: any): void
}

export interface IObservable<T> {
	/**
	 * Adds an observer to an observable object.
	 * @param observer The observer to receive values.
	 * @returns A function to call to cancel the subscription.
	 */
	subscribe(observer: IObserver<T>): () => void
}

export class Observer<T> implements IObserver<T>, RpcMarshalable {
	readonly _jsonRpcMarshalableLifetime: MarshaledObjectLifetime = 'explicit'
	error: any

	get completed() {
		return this.error !== undefined
	}

	constructor(
		private readonly next: (value: T) => void,
		private readonly completion?: (error?: any) => void
	) {}

	onNext(value: T): void {
		this.next(value)
	}

	onCompleted(): void {
		this.error = null
		if (this.completion) {
			this.completion(null)
		}
	}

	onError(reason: any): void {
		this.error = reason
		if (this.completion) {
			this.completion(reason)
		}
	}
}
