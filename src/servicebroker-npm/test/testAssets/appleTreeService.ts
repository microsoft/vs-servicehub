import { AppleGrownEventArgs, ApplePickedEventArgs, AppleTreeEmitter, IAppleTreeService } from './interfaces'
import { EventEmitter } from 'events'
import CancellationToken from 'cancellationtoken'
import { RpcEventServer } from '../../src/ServiceRpcDescriptor'

export class AppleTree extends (EventEmitter as new () => AppleTreeEmitter) implements IAppleTreeService, RpcEventServer {
	private static readonly _rpcEventNames = Object.freeze(['picked', 'grown'])
	public readonly rpcEventNames = AppleTree._rpcEventNames
	pick(args: ApplePickedEventArgs, cancellationToken?: CancellationToken): Promise<void> {
		this.emit('picked', args)
		return Promise.resolve()
	}
	grow(args: AppleGrownEventArgs, cancellationToken?: CancellationToken): Promise<void> {
		this.emit('grown', args)
		return Promise.resolve()
	}
}
