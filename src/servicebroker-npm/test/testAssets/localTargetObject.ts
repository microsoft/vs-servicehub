export class LocalTargetObject {
	public callbackInvocations: number = 0

	public callback(): void {
		this.callbackInvocations++
	}
}
