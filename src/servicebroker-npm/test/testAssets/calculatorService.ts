import { ICalculatorService } from './interfaces'
import CancellationToken from 'cancellationtoken'

export class Calculator implements ICalculatorService {
	public isDisposed: boolean = false
	public disposed: Promise<void>
	private notifyDisposed: (() => void) | undefined

	constructor() {
		this.disposed = new Promise<void>(resolve => (this.notifyDisposed = resolve))
	}

	public add(a: number, b: number, cancellationToken?: CancellationToken): Promise<number> {
		return Promise.resolve(a + b)
	}

	public add5(a: number, cancellationToken?: CancellationToken): Promise<number> {
		return Promise.resolve(a + 5)
	}

	public dispose(): void {
		this.isDisposed = true
		this.notifyDisposed!()
	}
}
