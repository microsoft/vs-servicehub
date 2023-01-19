import type { Config } from 'jest'

const config: Config = {
	transform: {
		'^.+\\.tsx?$': 'ts-jest',
	},
	testRegex: '(test|samples)/[^/]+\\.ts$',
	testPathIgnorePatterns: [
		'/js/',
		'/node_modules/',
	],
	moduleFileExtensions: ['ts', 'tsx', 'js', 'jsx', 'json', 'node'],
	collectCoverage: true,
}

export default config
