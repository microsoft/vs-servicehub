import CancellationToken from 'cancellationtoken'
import * as child_process from 'child_process'
import * as fs from 'fs'
import { FullDuplexStream, MultiplexingStream } from 'nerdbank-streams'
import * as path from 'path'
import { Formatters, MessageDelimiters } from '../../src/constants'
import { FrameworkServices } from '../../src/FrameworkServices'
import { IRemoteServiceBroker } from '../../src/IRemoteServiceBroker'
import { ServiceJsonRpcDescriptor } from '../../src/ServiceJsonRpcDescriptor'
import { ServiceMoniker } from '../../src/ServiceMoniker'

export async function hostMultiplexingServer(
	stream: NodeJS.ReadWriteStream,
	serverFunc: (mx: MultiplexingStream) => IRemoteServiceBroker,
	cancellationToken: CancellationToken
): Promise<void> {
	const multiplexingStream = await MultiplexingStream.CreateAsync(stream, undefined, cancellationToken)
	const channel = await multiplexingStream.offerChannelAsync('', undefined, cancellationToken)
	FrameworkServices.remoteServiceBroker.constructRpc<IRemoteServiceBroker>(serverFunc(multiplexingStream), channel)

	// unconventional, but this allows us to associate the cancellation token with the channel's completion
	cancellationToken.onCancelled(() => channel.dispose())
	await channel.completion
}

export async function startCP(cancellationToken: CancellationToken, args?: string[]): Promise<MultiplexingStream> {
	const cp = child_process.spawn(findTestExe(), args)
	cp.stderr.on('data', (chunk: Buffer) => {
		console.error('server STDERR: ' + chunk.toString('utf8'))
	})
	cp.on('exit', code => {
		if (code !== 0) {
			console.log('server exited with: ' + code)
		}
	})
	const stream = FullDuplexStream.Splice(cp.stdout, cp.stdin)
	return await MultiplexingStream.CreateAsync(stream, undefined, cancellationToken)

	function findTestExe(): string {
		const configurations = ['Debug', 'Release']
		const fileName = process.platform === 'win32' ? 'ServiceBrokerTest.exe' : 'ServiceBrokerTest'
		let testBrokerPathsAttempted: string[] = []
		for (const config of configurations) {
			const testBrokerPath = path.join(__dirname, `../../../../bin/ServiceBrokerTest/${config}/net7.0/${fileName}`)
			if (fs.existsSync(testBrokerPath)) {
				return testBrokerPath
			}

			testBrokerPathsAttempted.push(testBrokerPath)
		}

		throw new Error('Could not find path to ServiceBrokerTest at: ' + testBrokerPathsAttempted)
	}
}

export const calcDescriptorUtf8Http: ServiceJsonRpcDescriptor = new ServiceJsonRpcDescriptor(
	ServiceMoniker.create('Calculator'),
	Formatters.Utf8,
	MessageDelimiters.HttpLikeHeaders
)
export const calcDescriptorUtf8BE32: ServiceJsonRpcDescriptor = new ServiceJsonRpcDescriptor(
	ServiceMoniker.create('CalculatorUtf8BE32'),
	Formatters.Utf8,
	MessageDelimiters.BigEndianInt32LengthHeader
)
export const calcDescriptorMsgPackBE32: ServiceJsonRpcDescriptor = new ServiceJsonRpcDescriptor(
	ServiceMoniker.create('CalculatorMsgPackBE32'),
	Formatters.MessagePack,
	MessageDelimiters.BigEndianInt32LengthHeader
)
export const callBackDescriptor = new ServiceJsonRpcDescriptor(ServiceMoniker.create('Callback'), Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
export const activationDescriptor = new ServiceJsonRpcDescriptor(ServiceMoniker.create('ActivationService'), Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
export const cancellationWaiter: ServiceJsonRpcDescriptor = new ServiceJsonRpcDescriptor(
	ServiceMoniker.create('CancellationWaiter'),
	Formatters.Utf8,
	MessageDelimiters.HttpLikeHeaders
)
export const appleTreeDescriptor: ServiceJsonRpcDescriptor = new ServiceJsonRpcDescriptor(
	ServiceMoniker.create('AppleTree'),
	Formatters.Utf8,
	MessageDelimiters.HttpLikeHeaders
)

export function delay(ms: number): Promise<void> {
	return new Promise(resolve => setTimeout(resolve, ms))
}
