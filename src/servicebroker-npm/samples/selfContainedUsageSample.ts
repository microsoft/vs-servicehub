// This file should be kept (mostly) in sync with the sample in the README.md doc
// with the exception of the `import` from '../src', which should come from the
// NPM package name in the doc.

import assert from 'assert'
import CancellationToken from 'cancellationtoken'
import {
	Formatters,
	MessageDelimiters,
	ServiceJsonRpcDescriptor,
	ServiceMoniker,
	ServiceRpcDescriptor,
	GlobalBrokeredServiceContainer,
	ServiceAudience,
	ServiceRegistration,
} from '../src'

interface IService {
	readonly moniker: ServiceMoniker
	readonly descriptor: ServiceRpcDescriptor
	readonly registration: ServiceRegistration
}

class Services {
	static calculator: Readonly<IService> = Services.defineLocal('calc')

	private static defineLocal(
		name: string,
		version?: string
	): Readonly<IService> {
		const moniker = { name, version }
		const descriptor = new ServiceJsonRpcDescriptor(
			moniker,
			Formatters.MessagePack,
			MessageDelimiters.BigEndianInt32LengthHeader
		)
		const registration = new ServiceRegistration(
			ServiceAudience.local,
			false
		)
		return Object.freeze({ moniker, descriptor, registration })
	}
}

interface ICalculator {
	add(
		a: number,
		b: number,
		cancellationToken?: CancellationToken
	): Promise<number>
}

class Calculator implements ICalculator {
	public add(
		a: number,
		b: number,
		cancellationToken?: CancellationToken
	): Promise<number> {
		return Promise.resolve(a + b)
	}
}

let container: GlobalBrokeredServiceContainer
beforeAll(function () {
	container = new GlobalBrokeredServiceContainer()
	container.register([Services.calculator])
	container.profferServiceFactory(
		Services.calculator.descriptor,
		(mk, options, sb, ct) => Promise.resolve(new Calculator())
	)
})

it('self-contained sample', async function () {
	const sb = container.getFullAccessServiceBroker()
	const calc = await sb.getProxy<ICalculator>(Services.calculator.descriptor)
	assert(calc)
	const sum = await calc.add(3, 5)
	assert(sum === 8)
})
