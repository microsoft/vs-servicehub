import eslintPluginPrettier from 'eslint-plugin-prettier'
import tseslint from 'typescript-eslint'
import prettierConfig from 'eslint-config-prettier'

// eslint-config-prettier is CJS; strip __esModule to avoid flat config errors
const { __esModule, ...prettier } = prettierConfig

export default tseslint.config(
	{
		ignores: ['out/**', 'dist/**', '**/*.d.ts', 'js/**', 'jest.config.ts', '.pnp.*', '.yarn/**', 'coverage/**', 'node_modules/**', 'samples/**'],
	},
	prettier,
	{
		files: ['src/**/*.ts', 'test/**/*.ts'],
		plugins: {
			'@typescript-eslint': tseslint.plugin,
			prettier: eslintPluginPrettier,
		},
		languageOptions: {
			parser: tseslint.parser,
			parserOptions: {
				project: './tsconfig.json',
			},
		},
		rules: {
			'@typescript-eslint/naming-convention': 'warn',
			'@typescript-eslint/semi': 'off',
			curly: 'warn',
			'object-curly-spacing': 'off',
			eqeqeq: 'warn',
			'no-throw-literal': 'error',
			'no-unexpected-multiline': 'error',
			'no-unreachable': 'error',
			semi: 'off',
			'prettier/prettier': [
				'error',
				{
					printWidth: 160,
					semi: false,
					singleQuote: true,
					trailingComma: 'es5',
					useTabs: true,
					endOfLine: 'auto',
					arrowParens: 'avoid',
				},
			],
		},
	}
)
