import CancellationToken from 'cancellationtoken'
import { CancellationToken as vscodeCancellationToken, CancellationTokenSource as vscodeCancellationTokenSource } from 'vscode-jsonrpc'

function isProxy(value: any): boolean {
	// It turns out that it's really hard to detect a proxy in general:
	// https://stackoverflow.com/questions/36372611/how-to-test-if-an-object-is-a-proxy
	// Our strategy here is to at least detect pass-through proxies that claim that everything exists.
	// So we make up a property name that should never exist. If it does, then we know it must be a proxy.
	return value && value.ImAProxy !== undefined
}

export class CancellationTokenAdapters {
	/** Tests whether an object satisfies the {@link vscodeCancellationToken} interface. */
	static isVSCode(value: any): value is vscodeCancellationToken {
		return vscodeCancellationToken.is(value)
	}

	/** Tests whether an object satisfies the {@link CancellationToken} interface. */
	static isCancellationToken(value: any): value is CancellationToken {
		return (
			value &&
			!isProxy(value) &&
			typeof value.throwIfCancelled === 'function' &&
			typeof value.onCancelled === 'function' &&
			value.isCancelled !== undefined &&
			value.canBeCancelled !== undefined
		)
	}

	/** Returns a {@link CancellationToken} that is linked to the given {@link vscodeCancellationToken}. */
	static vscodeToCancellationToken(cancellationToken: vscodeCancellationToken): CancellationToken {
		if (cancellationToken.isCancellationRequested) {
			return CancellationToken.CANCELLED
		} else if (cancellationToken === vscodeCancellationToken.None) {
			return CancellationToken.CONTINUE
		} else {
			const result = CancellationToken.create()
			cancellationToken.onCancellationRequested(_ => result.cancel())
			return result.token
		}
	}

	/** Returns a {@link vscodeCancellationToken} that is linked to the given {@link CancellationToken}. */
	static cancellationTokenToVSCode(cancellationToken: CancellationToken): vscodeCancellationToken {
		if (cancellationToken.isCancelled) {
			return vscodeCancellationToken.Cancelled
		} else if (cancellationToken.canBeCancelled) {
			const cts = new vscodeCancellationTokenSource()
			cancellationToken.onCancelled(_ => cts.cancel())
			return cts.token
		} else {
			return vscodeCancellationToken.None
		}
	}
}
