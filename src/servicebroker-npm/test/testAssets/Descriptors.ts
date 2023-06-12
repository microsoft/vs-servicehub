import { Formatters, MessageDelimiters } from '../../src/constants'
import { ServiceJsonRpcDescriptor } from '../../src/ServiceJsonRpcDescriptor'

export class Descriptors {
	static calculator = Descriptors.create('calc')

	static tree = Descriptors.create('tree')

	static treeWithVersion11 = Descriptors.create('tree', '1.1')

	static create(monikerName: string, version?: string) {
		return Object.freeze(new ServiceJsonRpcDescriptor({ name: monikerName, version }, Formatters.Utf8, MessageDelimiters.HttpLikeHeaders))
	}
}
