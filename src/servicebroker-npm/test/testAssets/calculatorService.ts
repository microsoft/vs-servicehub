import { ICalculatorService } from './interfaces'
import CancellationToken from 'cancellationtoken'
import { IDisposable, IObserver } from '../../src'

export class Calculator implements ICalculatorService {
	public isDisposed: boolean = false
	public disposed: Promise<void>
	private notifyDisposed: (() => void) | undefined

	constructor() {
		this.disposed = new Promise<void>(resolve => (this.notifyDisposed = resolve))
	}

	add(a: number, b: number, cancellationToken?: CancellationToken): Promise<number> {
		return Promise.resolve(a + b)
	}

	add5(a: number, cancellationToken?: CancellationToken): Promise<number> {
		return Promise.resolve(a + 5)
	}

	async observeNumbers(observer: IObserver<number> & IDisposable, length: number, failAtEnd: boolean = false): Promise<void> {
		for (let i = 0; i <= length; i++) {
			await Promise.resolve()
			observer.onNext(i)
		}

		if (failAtEnd) {
			observer.onError('Requested failure.')
		} else {
			observer.onCompleted()
		}

		if ('dispose' in observer) {
			observer.dispose()
		}
	}

	dispose(): void {
		this.isDisposed = true
		this.notifyDisposed!()
	}
}
