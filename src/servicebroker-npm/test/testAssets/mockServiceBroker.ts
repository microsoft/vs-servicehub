import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import { ServiceRpcDescriptor } from '../../src/ServiceRpcDescriptor'
import { IServiceBroker, ServiceBrokerEmitter } from '../../src/IServiceBroker'
import { FullDuplexStream } from 'nerdbank-streams'
import { Calculator } from './calculatorService'
import { calcDescriptorUtf8Http } from './testUtilities'
import { ServiceActivationOptions } from '../../src/ServiceActivationOptions'
import { IDisposable } from '../../src/IDisposable'
import { ServiceMoniker } from '../../src/ServiceMoniker'

export class MockServiceBroker extends (EventEmitter as new () => ServiceBrokerEmitter) implements IServiceBroker {
	getProxy<T extends object>(
		serviceDescriptor: ServiceRpcDescriptor,
		options?: ServiceActivationOptions,
		cancellationToken?: CancellationToken
	): Promise<(T & IDisposable) | null> {
		throw new Error('Method not implemented.')
	}
	getPipe(serviceMoniker: ServiceMoniker, options?: ServiceActivationOptions, cancellationToken?: CancellationToken): Promise<NodeJS.ReadWriteStream | null> {
		if (ServiceMoniker.equals(calcDescriptorUtf8Http.moniker, serviceMoniker)) {
			const pair = FullDuplexStream.CreatePair()
			calcDescriptorUtf8Http.constructRpc(new Calculator(), pair.first)
			return Promise.resolve(pair.second)
		}

		if (serviceMoniker.name === 'throws') {
			throw new Error('Throws on demand')
		}

		return Promise.resolve(null)
	}
}
