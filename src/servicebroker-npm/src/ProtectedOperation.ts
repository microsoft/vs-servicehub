import assert from 'assert'

/**
 * Describes an operation and who can perform it
 */
export interface ProtectedOperation {
	/** The moniker representing the operation. */
	operationMoniker: string

	/** The trust level required to perform this operation. */
	requiredTrustLevel?: number
}

export namespace ProtectedOperation {
	/**
	 * Creates a new, immutable [ProtectedOperation](#ProtectedOperation) object.
	 * @param operationMoniker The moniker representing the operation
	 * @param requiredTrustLevel The trust level required to perform this operation
	 */
	export function create(operationMoniker: string, requiredTrustLevel?: number): Readonly<ProtectedOperation> {
		return Object.freeze({ operationMoniker, requiredTrustLevel })
	}

	/**
	 * Checks if one operation is a superset of another operation.
	 * @param supersetCandidate The possibly superset operation.
	 * @param subsetCandidate The possibly subset operation.
	 */
	export function isSupersetOf(supersetCandidate: ProtectedOperation, subsetCandidate: ProtectedOperation): boolean {
		assert(supersetCandidate)
		assert(subsetCandidate)

		return (
			supersetCandidate.operationMoniker === subsetCandidate.operationMoniker &&
			(supersetCandidate.requiredTrustLevel === subsetCandidate.requiredTrustLevel ||
				(!!supersetCandidate.requiredTrustLevel &&
					!!subsetCandidate.requiredTrustLevel &&
					supersetCandidate.requiredTrustLevel > subsetCandidate.requiredTrustLevel))
		)
	}

	/**
	 * Tests equality between two protected operations.
	 * @param operation1 The first operation to compare.
	 * @param operation2 The second operation to compare.
	 */
	export function equals(operation1?: ProtectedOperation, operation2?: ProtectedOperation): boolean {
		if (operation1 === operation2) {
			return true
		}

		if (!operation1 || !operation2) {
			return false
		}

		return operation1.operationMoniker === operation2.operationMoniker && operation1.requiredTrustLevel === operation2.requiredTrustLevel
	}
}
