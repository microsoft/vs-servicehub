import assert from 'assert'
import { RemoteServiceConnections } from '../src/constants'

describe('RemoteServiceConnections', function () {
	describe('parse', function () {
		it('empty string', function () {
			assert.throws(() => RemoteServiceConnections.parse(''))
		})
		it('None element', function () {
			assert.strictEqual(RemoteServiceConnections.parse('None'), RemoteServiceConnections.None)
		})
		it('IpcPipe', function () {
			assert.strictEqual(RemoteServiceConnections.parse('IpcPipe'), RemoteServiceConnections.IpcPipe)
		})
		it('Multiplexing, IpcPipe', function () {
			assert.strictEqual(
				RemoteServiceConnections.parse('Multiplexing, IpcPipe'),
				RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing
			)
		})
	})
	describe('contains', function () {
		it('matches', function () {
			assert(RemoteServiceConnections.contains('IpcPipe, Multiplexing', RemoteServiceConnections.IpcPipe))
			assert(RemoteServiceConnections.contains('IpcPipe, Multiplexing', RemoteServiceConnections.IpcPipe | RemoteServiceConnections.Multiplexing))
			assert(!RemoteServiceConnections.contains('IpcPipe, Multiplexing', RemoteServiceConnections.ClrActivation))
			assert(!RemoteServiceConnections.contains('IpcPipe, Multiplexing', RemoteServiceConnections.IpcPipe | RemoteServiceConnections.ClrActivation))
		})
	})
})
