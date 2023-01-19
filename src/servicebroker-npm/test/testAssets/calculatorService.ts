import { ICalculatorService } from './interfaces'
import CancellationToken from 'cancellationtoken'

export class Calculator implements ICalculatorService {
	public isDisposed: boolean = false

	public add(a: number, b: number, cancellationToken?: CancellationToken): Promise<number> {
		return Promise.resolve(a + b)
	}

	public add5(a: number, cancellationToken?: CancellationToken): Promise<number> {
		return Promise.resolve(a + 5)
	}

	public dispose(): void {
		this.isDisposed = true
	}
}
