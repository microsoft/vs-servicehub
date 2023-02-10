import { EventEmitter } from 'events'
import StrictEventEmitter from 'strict-event-emitter-types'
import { ServiceBrokerEvents } from '../IServiceBroker'

export type ServiceBrokerEmitter = StrictEventEmitter<EventEmitter, ServiceBrokerEvents>
