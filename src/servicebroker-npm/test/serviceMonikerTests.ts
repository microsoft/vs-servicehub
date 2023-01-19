import assert from 'assert'
import { ServiceMoniker } from '../src/ServiceMoniker'

describe('ServiceMoniker tests', function () {
	it('Should throw on empty constructor', () => {
		assert.throws(() => ServiceMoniker.create(''))
		assert.doesNotThrow(() => ServiceMoniker.create('something', undefined))
	})

	it('Should set properties it is initialized with', function () {
		const name = 'Something'
		const moniker1 = ServiceMoniker.create(name)
		assert.equal(moniker1.name, name, 'The name should match that passed into the constructor')
		assert.equal(moniker1.version, null, 'The version should be null if not initialized')

		const moniker2 = ServiceMoniker.create(name, undefined)
		assert.equal(moniker2.name, name, 'The name should match that passed into the constructor')
		assert.equal(moniker2.version, null, 'The version should be null if initialized as null')

		const version = '1.0'
		const moniker3 = ServiceMoniker.create(name, version)
		assert.equal(moniker3.name, name, 'The name should match that passed into the constructor')
		assert.equal(moniker3.version, version, 'The version should match that passed into the constructor')
	})

	it('Should have logical equality', function () {
		const moniker1 = ServiceMoniker.create('a')
		const moniker2 = ServiceMoniker.create('a')
		const moniker3 = ServiceMoniker.create('a', '1.0')
		const moniker4 = ServiceMoniker.create('a', '1.0')
		const monikerA = ServiceMoniker.create('A')
		const monikerB = ServiceMoniker.create('B')

		assert(ServiceMoniker.equals(moniker1, moniker2), 'Monikers with the same name should be equal')
		assert(!ServiceMoniker.equals(moniker1, monikerA), 'Moniker equality should be case sensitive')
		assert(!ServiceMoniker.equals(monikerA, monikerB), 'Differently named monikers should not be equal')

		assert(!ServiceMoniker.equals(moniker1, moniker3), 'Monikers with different version should not be equal')
		assert(ServiceMoniker.equals(moniker3, moniker4), 'Monikers with the same name and version should be equal')
	})

	it('Should implement toString()', () => {
		expect(`${ServiceMoniker.create('a')}`).toEqual('a')
		expect(`${ServiceMoniker.create('a', '1.0')}`).toEqual('a (1.0)')
	})

	it('toString() formats moniker', () => {
		expect(`${ServiceMoniker.toString(ServiceMoniker.create('a'))}`).toEqual('a')
		expect(`${ServiceMoniker.toString(ServiceMoniker.create('a', '1.0'))}`).toEqual('a (1.0)')
	})
})
