import CancellationToken from 'cancellationtoken'
import { EventEmitter } from 'events'
import StrictEventEmitter from 'strict-event-emitter-types'

export interface IFakeService {
	fakeProperty: string
	fakeMethod(fakeParam: number): void
}

export interface ICalculatorService {
	add(a: number, b: number, cancellationToken?: CancellationToken): Promise<number>
	add5(a: number, cancellationToken?: CancellationToken): Promise<number>
}

export interface ICallMeBackService {
	callMeBack(message: string, cancellationToken?: CancellationToken): Promise<void>
}

export interface ICallMeBackClient {
	youPhoned(message: string): Promise<void>
}

export interface IActivationService {
	getClientCredentials(): Promise<{ [key: string]: string }>
	getActivationArguments(): Promise<{ [key: string]: string }>
}

export interface IWaitToBeCanceled {
	waitForCancellation(cancellationToken: CancellationToken): Promise<void>
}

export interface ApplePickedEventArgs {
	color: 'red' | 'green' | 'yellow'
}

export interface AppleGrownEventArgs {
	seeds: number
}

export interface AppleTreeEvents {
	picked: (args: ApplePickedEventArgs) => void
	grown: (args: AppleGrownEventArgs) => void
}

export type AppleTreeEmitter = StrictEventEmitter<EventEmitter, AppleTreeEvents>

export interface IAppleTreeService extends AppleTreeEmitter {
	pick(args: ApplePickedEventArgs, cancellationToken?: CancellationToken): Promise<void>
	grow(args: AppleGrownEventArgs, cancellationToken?: CancellationToken): Promise<void>
}
