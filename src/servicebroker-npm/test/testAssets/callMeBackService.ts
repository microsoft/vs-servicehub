import { ICallMeBackClient, ICallMeBackService } from './interfaces'

export class CallMeBackService implements ICallMeBackService {
	constructor(private readonly clientCallback: ICallMeBackClient) {}

	async callMeBack(message: string): Promise<void> {
		await this.clientCallback.youPhoned(message)
	}
}
