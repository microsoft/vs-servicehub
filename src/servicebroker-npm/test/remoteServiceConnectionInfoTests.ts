import assert from 'assert'
import { RemoteServiceConnectionInfo } from '../src/RemoteServiceConnectionInfo'

describe('Remote Service Connection Info tests', function () {
	it('Should be empty by default', function () {
		const info: RemoteServiceConnectionInfo = {}
		assert(RemoteServiceConnectionInfo.isEmpty(info), 'Connection info should be empty by default')
	})

	it('Should be activated when muliplexing channel ID is set', function () {
		const info: RemoteServiceConnectionInfo = {}
		info.multiplexingChannelId = 4
		assert(!RemoteServiceConnectionInfo.isEmpty(info), 'Should not be empty once multiplexing channel ID is set')
	})

	it('Should have null pipe and null CLR instructions', function () {
		const info: RemoteServiceConnectionInfo = {}
		info.multiplexingChannelId = 4
		assert(!info.pipeName, 'Pipe name should be empty after multiplexing channel ID is set')
		assert.equal(info.multiplexingChannelId, 4, 'Should set only multiplexing channel ID')
	})

	it('Should be activated when request ID is set', function () {
		const info: RemoteServiceConnectionInfo = {}
		info.requestId = 'c107d7e6-7952-4463-b217-e0ac7a62ca28'
		assert(!RemoteServiceConnectionInfo.isEmpty(info), 'Should not be empty once request ID is set')
	})

	it('Should not be one of any connection type as default', function () {
		const info: RemoteServiceConnectionInfo = {}
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'clrActivation,ipcPipe,multiplexing'), 'Should not be any connection type by default')
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'ipcPipe'), 'Should not be icpPipe connection type by default')
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'none'), 'Should not be none connection type by default')
	})

	it('Should be one of multiplexing', function () {
		const info: RemoteServiceConnectionInfo = {}
		info.multiplexingChannelId = 4
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'ipcPipe'), 'Should not be one of pipe if just multiplexing channel ID is set')
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'ipcPipe,clrActivation'), 'Should not be one of pipe or clr if just multiplexing channel ID is set')
		assert(RemoteServiceConnectionInfo.isOneOf(info, 'icpPipe,multiplexing'), 'Should be one of multiplexing if multiplexing channel ID is set')
		assert(RemoteServiceConnectionInfo.isOneOf(info, 'multiplexing'), 'Should be one of multiplexing if multiplexing channel ID is set')
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'none'), 'Should not be one of none if just multiplexing channel ID is set')
	})

	it('Should only support multiplexing channel ID', function () {
		const info: RemoteServiceConnectionInfo = {}
		info.multiplexingChannelId = 3
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'ipcPipe'), 'Should recognize it is not a pipe connection type')
		assert(RemoteServiceConnectionInfo.isOneOf(info, 'multiplexing'), 'Should recognize it is a multiplexing connection type')
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'clrActivation'), 'Should not be a clrActivation connection type')
		assert(RemoteServiceConnectionInfo.isOneOf(info, 'ipcPipe,multiplexing'), "Should recognize it's a pipe and multiplexing connection type")
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'ipcPipe,clrActivation'), 'Should recognize it is not a pipe connection type')
		assert(!RemoteServiceConnectionInfo.isOneOf(info, 'none'), 'Should not be a none connection type')
	})
})
