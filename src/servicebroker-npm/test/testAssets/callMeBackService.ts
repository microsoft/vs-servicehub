import { ICallMeBackClient, ICallMeBackService } from './interfaces'

export class CallMeBackService implements ICallMeBackService {
	constructor(private readonly clientCallback: ICallMeBackClient) {}

	async CallMeBackAsync(message: string): Promise<void> {
		await this.clientCallback.YouPhonedAsync(message)
	}
}
