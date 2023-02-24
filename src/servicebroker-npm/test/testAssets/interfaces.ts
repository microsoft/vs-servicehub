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
	weight: number
}

export interface AppleTreeEvents {
	/** An event with one argument that contains two properties. */
	picked: (args: ApplePickedEventArgs) => void
	/** An event with two arguments. */
	grown: (seeds: number, weight: number) => void
}

export type AppleTreeEmitter = StrictEventEmitter<EventEmitter, AppleTreeEvents>

export interface IAppleTreeService extends AppleTreeEmitter {
	pick(args: ApplePickedEventArgs, cancellationToken?: CancellationToken): Promise<void>
	grow(seeds: number, weight: number, cancellationToken?: CancellationToken): Promise<void>
}
