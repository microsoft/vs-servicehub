import { ICallMeBackClient } from './interfaces'

export class CallMeBackClient implements ICallMeBackClient {
	public lastMessage?: string

	public youPhoned(message: string): Promise<void> {
		this.lastMessage = message
		return Promise.resolve()
	}
}
