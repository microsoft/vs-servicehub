import { createHook } from 'async_hooks'

const resources = new Map<number, { type: string; stack: string }>()
createHook({
	init(id, type) {
		if (type === 'Timeout' || type === 'PIPEWRAP' || type === 'PROCESSWRAP' || type === 'TCPSERVERWRAP' || type === 'PIPESERVERWRAP') {
			resources.set(id, { type, stack: new Error().stack?.split('\n').slice(2, 9).join('\n') ?? '' })
		}
	},
	destroy(id) {
		resources.delete(id)
	},
}).enable()

afterAll(() => {
	const testPath = expect.getState().testPath
	setTimeout(() => {
		process.stderr.write(`Open resources after ${testPath}: ${JSON.stringify([...resources.values()])}\n`)
	}, 2000).unref()
})
