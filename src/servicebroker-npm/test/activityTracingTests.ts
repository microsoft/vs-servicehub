import assert from 'assert'
import { FullDuplexStream } from 'nerdbank-streams'
import { Message } from 'vscode-jsonrpc'
import { Formatters, MessageDelimiters, ServiceJsonRpcDescriptor, ServiceMoniker, IDisposable } from '../src'
import { Calculator } from './testAssets/calculatorService'
import { ICalculatorService } from './testAssets/interfaces'

const sampleTraceparent = '00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01'
const sampleTracestate = 'congo=t61rcWkgMzE'

describe('Message filters', function () {
	describe('outgoingMessageFilter', function () {
		it('is invoked for outgoing MessagePack requests', async function () {
			const outgoingMessages: Message[] = []

			const descriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						outgoingMessages.push({ ...msg })
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			descriptor.constructRpc(new Calculator(), pipes.first)
			const client = descriptor.constructRpc<ICalculatorService>(pipes.second)

			const result = await client.add(2, 3)
			assert.strictEqual(result, 5)
			client.dispose()

			assert(outgoingMessages.length > 0, 'Should have intercepted outgoing messages')
		})

		it('can add traceparent to outgoing requests (W3C Trace Context)', async function () {
			const descriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						if ('method' in msg) {
							;(msg as any).traceparent = sampleTraceparent
							;(msg as any).tracestate = sampleTracestate
						}
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			descriptor.constructRpc(new Calculator(), pipes.first)
			const client = descriptor.constructRpc<ICalculatorService>(pipes.second)

			const result = await client.add(2, 3)
			assert.strictEqual(result, 5)
			client.dispose()
		})

		it('can add traceparent to outgoing UTF8 BE32 requests', async function () {
			const descriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.Utf8,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						if ('method' in msg) {
							;(msg as any).traceparent = sampleTraceparent
						}
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			descriptor.constructRpc(new Calculator(), pipes.first)
			const client = descriptor.constructRpc<ICalculatorService>(pipes.second)

			const result = await client.add(2, 3)
			assert.strictEqual(result, 5)
			client.dispose()
		})

		it('mutation is visible in encoded message', async function () {
			const receivedTraceparents: string[] = []

			const sendingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						if ('method' in msg) {
							;(msg as any).traceparent = sampleTraceparent
							;(msg as any).tracestate = sampleTracestate
						}
					},
				}
			)

			const receivingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					incomingMessageFilter: (msg: Message) => {
						const tp = (msg as any).traceparent
						if (typeof tp === 'string') {
							receivedTraceparents.push(tp)
						}
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			receivingDescriptor.constructRpc(new Calculator(), pipes.first)
			const client = sendingDescriptor.constructRpc<ICalculatorService>(pipes.second)

			await client.add(2, 3)
			client.dispose()

			assert(receivedTraceparents.length > 0, 'Receiver should have seen traceparent')
			assert.strictEqual(receivedTraceparents[0], sampleTraceparent)
		})
	})

	describe('incomingMessageFilter', function () {
		it('is invoked for incoming messages on the receiver side', async function () {
			const receivedMessages: Message[] = []

			const sendingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader
			)

			const receivingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					incomingMessageFilter: (msg: Message) => {
						receivedMessages.push({ ...msg })
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			receivingDescriptor.constructRpc(new Calculator(), pipes.first)
			const client = sendingDescriptor.constructRpc<ICalculatorService>(pipes.second)

			await client.add(2, 3)
			client.dispose()

			assert(receivedMessages.length > 0, 'Should have intercepted incoming messages')
		})

		it('extracts traceparent and tracestate from incoming request', async function () {
			const receivedContexts: { traceparent: string; tracestate?: string }[] = []

			const sendingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						if ('method' in msg) {
							;(msg as any).traceparent = sampleTraceparent
							;(msg as any).tracestate = sampleTracestate
						}
					},
				}
			)

			const receivingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					incomingMessageFilter: (msg: Message) => {
						const tp = (msg as any).traceparent
						if (typeof tp === 'string') {
							const ts = (msg as any).tracestate
							receivedContexts.push({ traceparent: tp, tracestate: typeof ts === 'string' ? ts : undefined })
						}
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			receivingDescriptor.constructRpc(new Calculator(), pipes.first)
			const client = sendingDescriptor.constructRpc<ICalculatorService>(pipes.second)

			await client.add(2, 3)
			client.dispose()

			assert(receivedContexts.length > 0, 'Should have received at least one trace context')
			assert.strictEqual(receivedContexts[0].traceparent, sampleTraceparent)
			assert.strictEqual(receivedContexts[0].tracestate, sampleTracestate)
		})

		it('extracts traceparent without tracestate', async function () {
			const receivedContexts: { traceparent: string; tracestate?: string }[] = []

			const sendingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						if ('method' in msg) {
							;(msg as any).traceparent = sampleTraceparent
						}
					},
				}
			)

			const receivingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					incomingMessageFilter: (msg: Message) => {
						const tp = (msg as any).traceparent
						if (typeof tp === 'string') {
							const ts = (msg as any).tracestate
							receivedContexts.push({ traceparent: tp, tracestate: typeof ts === 'string' ? ts : undefined })
						}
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			receivingDescriptor.constructRpc(new Calculator(), pipes.first)
			const client = sendingDescriptor.constructRpc<ICalculatorService>(pipes.second)

			await client.add(2, 3)
			client.dispose()

			assert(receivedContexts.length > 0, 'Should have received at least one trace context')
			assert.strictEqual(receivedContexts[0].traceparent, sampleTraceparent)
			assert.strictEqual(receivedContexts[0].tracestate, undefined)
		})
	})

	describe('bidirectional filters', function () {
		it('both outgoing and incoming filters work on the same descriptor', async function () {
			const serverReceivedTraceparents: string[] = []
			const clientTraceparent = '00-aaaabbbbccccddddeeeeffffaaaabbbb-1111222233334444-01'

			const clientDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						if ('method' in msg) {
							;(msg as any).traceparent = clientTraceparent
						}
					},
				}
			)

			const serverDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					incomingMessageFilter: (msg: Message) => {
						const tp = (msg as any).traceparent
						if (typeof tp === 'string') {
							serverReceivedTraceparents.push(tp)
						}
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			serverDescriptor.constructRpc(new Calculator(), pipes.first)
			const client = clientDescriptor.constructRpc<ICalculatorService>(pipes.second)

			await client.add(2, 3)
			client.dispose()

			assert(serverReceivedTraceparents.length > 0, 'Server should have received trace context')
			assert.strictEqual(serverReceivedTraceparents[0], clientTraceparent)
		})
	})

	describe('can add arbitrary metadata', function () {
		it('adds custom top-level fields to outgoing messages', async function () {
			const receivedCorrelationIds: string[] = []
			const correlationId = 'req-12345'

			const sendingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						;(msg as any)['x-correlation-id'] = correlationId
					},
				}
			)

			const receivingDescriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					incomingMessageFilter: (msg: Message) => {
						const cid = (msg as any)['x-correlation-id']
						if (typeof cid === 'string') {
							receivedCorrelationIds.push(cid)
						}
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			receivingDescriptor.constructRpc(new Calculator(), pipes.first)
			const client = sendingDescriptor.constructRpc<ICalculatorService>(pipes.second)

			await client.add(2, 3)
			client.dispose()

			assert(receivedCorrelationIds.length > 0, 'Receiver should have seen correlation ID')
			assert.strictEqual(receivedCorrelationIds[0], correlationId)
		})
	})

	describe('backward compatibility', function () {
		it('works without any options', async function () {
			const descriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader
			)

			const pipes = FullDuplexStream.CreatePair()
			descriptor.constructRpc(new Calculator(), pipes.first)
			const client = descriptor.constructRpc<ICalculatorService>(pipes.second)

			const result = await client.add(2, 3)
			assert.strictEqual(result, 5)
			client.dispose()
		})

		it('works with multiplexingStreamOptions as 4th parameter (legacy)', async function () {
			const descriptor = new ServiceJsonRpcDescriptor(ServiceMoniker.create('FilterTest'), Formatters.Utf8, MessageDelimiters.HttpLikeHeaders)
			assert.strictEqual(descriptor.formatter, Formatters.Utf8)
			assert.strictEqual(descriptor.messageDelimiter, MessageDelimiters.HttpLikeHeaders)
		})

		it('works with ServiceJsonRpcDescriptorOptions containing only multiplexingStreamOptions', async function () {
			const descriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.MessagePack,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{ multiplexingStreamOptions: { protocolMajorVersion: 3 } }
			)
			assert.strictEqual(descriptor.formatter, Formatters.MessagePack)
		})
	})

	describe('UTF8 formatter with BE32 delimiter', function () {
		it('round-trips trace context through UTF8 encoding', async function () {
			const receivedContexts: { traceparent: string; tracestate?: string }[] = []

			const descriptor = new ServiceJsonRpcDescriptor(
				ServiceMoniker.create('FilterTest'),
				Formatters.Utf8,
				MessageDelimiters.BigEndianInt32LengthHeader,
				{
					outgoingMessageFilter: (msg: Message) => {
						if ('method' in msg) {
							;(msg as any).traceparent = sampleTraceparent
							;(msg as any).tracestate = sampleTracestate
						}
					},
					incomingMessageFilter: (msg: Message) => {
						const tp = (msg as any).traceparent
						if (typeof tp === 'string') {
							const ts = (msg as any).tracestate
							receivedContexts.push({ traceparent: tp, tracestate: typeof ts === 'string' ? ts : undefined })
						}
					},
				}
			)

			const pipes = FullDuplexStream.CreatePair()
			descriptor.constructRpc(new Calculator(), pipes.first)
			const client = descriptor.constructRpc<ICalculatorService>(pipes.second)

			await client.add(2, 3)
			client.dispose()

			assert(receivedContexts.length > 0, 'Should have received trace context')
			assert.strictEqual(receivedContexts[0].traceparent, sampleTraceparent)
			assert.strictEqual(receivedContexts[0].tracestate, sampleTracestate)
		})
	})
})
