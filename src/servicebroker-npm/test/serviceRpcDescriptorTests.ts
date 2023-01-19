import assert from 'assert'
import { Channel } from 'nerdbank-streams'
import { ServiceMoniker } from '../src/ServiceMoniker'
import { RpcConnection, ServiceRpcDescriptor } from '../src/ServiceRpcDescriptor'

const calcMoniker = ServiceMoniker.create('Calculator')

/**
 * A mock class to implement ServiceRpcDescriptor so tests can instantiate it.
 */
class MockServiceRpcDescriptor extends ServiceRpcDescriptor {
	public protocol = 'test-protocol'
	public constructor(moniker: ServiceMoniker) {
		super(moniker)
	}
	public constructRpcConnection(pipe: NodeJS.ReadWriteStream | Channel): RpcConnection {
		throw new Error('Method not implemented.')
	}
	public equals(descriptor: ServiceRpcDescriptor): boolean {
		throw new Error('Method not implemented.')
	}
}

describe('ServiceRpcDescriptor', function () {
	it('should have the same moniker it had on initialization', function () {
		const descriptor = new MockServiceRpcDescriptor(calcMoniker)
		assert.strictEqual(descriptor.moniker, calcMoniker, 'Should have the same moniker it had on instantiation')
	})
})
