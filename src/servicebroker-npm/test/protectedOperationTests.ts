import assert from 'assert'
import { ProtectedOperation } from '../src/ProtectedOperation'

describe('Protected Operation tests', function () {
	const moniker1: string = 'op1'
	const moniker2: string = 'op2'

	it('Should have logical equality', function () {
		let operation1: ProtectedOperation = { operationMoniker: moniker1 }
		let operation2: ProtectedOperation = { operationMoniker: moniker1 }
		assert(ProtectedOperation.equals(operation1, operation2), 'Should be equal with the same moniker')
		operation1.requiredTrustLevel = 4
		operation2.requiredTrustLevel = 4
		assert(ProtectedOperation.equals(operation1, operation2), 'Should be equal if moniker and required trust level are equal')

		operation1 = { operationMoniker: moniker1 }
		operation2 = { operationMoniker: moniker2 }
		assert(!ProtectedOperation.equals(operation1, operation2), 'Should not be equal if both have different monikers')
		operation1.requiredTrustLevel = 3
		operation2.operationMoniker = moniker1
		operation2.requiredTrustLevel = 4
		assert(!ProtectedOperation.equals(operation1, operation2), 'Should not be equal if different required trust levels')
	})

	it("Should know what it's a superset of", function () {
		const bigOp = ProtectedOperation.create(moniker1, 3)
		const bigOp2 = ProtectedOperation.create(moniker1, 3)
		const smallOp = ProtectedOperation.create(moniker1, 1)
		const unrelatedOp = ProtectedOperation.create(moniker2)

		assert(ProtectedOperation.isSupersetOf(bigOp, bigOp), 'Should be a superset of itself')
		assert(ProtectedOperation.isSupersetOf(unrelatedOp, unrelatedOp), 'Should be a superset of itself if no required trust level set')
		assert(ProtectedOperation.isSupersetOf(bigOp, smallOp), 'Higher trust level should be superset of lower trust level with same moniker')
		assert(!ProtectedOperation.isSupersetOf(smallOp, bigOp), 'Lower trust level should not be superset of higher trust level with same moniker')
		assert(ProtectedOperation.isSupersetOf(bigOp, bigOp2), 'Should be superset of operation with same trust level and required trust level')

		assert(!ProtectedOperation.isSupersetOf(bigOp, unrelatedOp), 'Should not be superset of operation with different moniker')
		assert(!ProtectedOperation.isSupersetOf(unrelatedOp, bigOp), 'Should not be superset of operation with different moniker')
	})
})
