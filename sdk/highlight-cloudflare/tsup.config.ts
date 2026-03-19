import { defineConfig } from 'tsup'

export default defineConfig({
	entry: ['src/index.ts'],
	format: ['cjs', 'esm'],
	dts: true,
	sourcemap: true,
	treeshake: 'smallest',
	platform: 'browser',
	external: [
		'pino-abstract-transport',
		'pino',
		'pino-pretty',
		'diagnostics_channel',
	],
})
