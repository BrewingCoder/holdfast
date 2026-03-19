import { defineConfig } from 'tsup'

export default defineConfig({
	entry: ['src/index.ts'],
	format: ['cjs', 'esm'],
	dts: true,
	sourcemap: true,
	treeshake: 'smallest',
	noExternal: [/(.*)/],
	esbuildOptions(options) {
		options.external = [
			'pino-abstract-transport',
			'pino',
			'pino-pretty',
		]
	},
})
