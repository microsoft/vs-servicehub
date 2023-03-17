import assert from 'assert'
import { Observer } from '../src'

describe('Observer', function () {
	it('next callback is fired with values', function () {
		const values: number[] = []
		const observer = new Observer<number>(v => values.push(v))
		observer.onNext(1)
		observer.onNext(3)
		assert.deepEqual(values, [1, 3])
	})

	it('completion callback is fired on success', function () {
		let error
		const observer = new Observer<number>(
			v => {},
			reason => (error = reason)
		)
		observer.onCompleted()
		assert.strictEqual(error, null)
	})

	it('completion callback is fired on failure', function () {
		let error
		const observer = new Observer<number>(
			v => {},
			reason => (error = reason)
		)
		observer.onError('fail')
		assert.strictEqual(error, 'fail')
	})

	describe('completed', function () {
		it('is false at start', function () {
			assert.strictEqual(false, new Observer<number>(v => {}).completed)
		})

		it('is true at successful end', function () {
			const observer = new Observer<number>(v => {})
			observer.onCompleted()
			assert.strictEqual(true, observer.completed)
		})

		it('is true at failed end', function () {
			const observer = new Observer<number>(v => {})
			observer.onError('failed')
			assert.strictEqual(true, observer.completed)
		})
	})

	describe('error', function () {
		it('before completion', function () {
			assert.strictEqual(undefined, new Observer<number>(v => {}).error)
		})

		it('after successful completion', function () {
			const observer = new Observer<number>(v => {})
			observer.onCompleted()
			assert.strictEqual(null, observer.error)
		})

		it('after failed completion', function () {
			const observer = new Observer<number>(v => {})
			observer.onError('fail')
			assert.strictEqual('fail', observer.error)
		})
	})
})
