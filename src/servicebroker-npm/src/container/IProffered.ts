import { IDisposable } from '../IDisposable'
import { IRemoteServiceBroker } from '../IRemoteServiceBroker'
import { IServiceBroker } from '../IServiceBroker'
import { ServiceMonikerValue } from './ServiceMonikerValue'
import { ServiceSource } from './ServiceSource'

export interface IProffered extends IServiceBroker, IRemoteServiceBroker, IDisposable {
	readonly source: ServiceSource

	readonly monikers: readonly ServiceMonikerValue[]
}
