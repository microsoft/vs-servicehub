import { ICallMeBackClient } from './interfaces'

export class CallMeBackClient implements ICallMeBackClient {
	public lastMessage?: string

	public YouPhonedAsync(message: string): Promise<void> {
		this.lastMessage = message
		return Promise.resolve()
	}
}
