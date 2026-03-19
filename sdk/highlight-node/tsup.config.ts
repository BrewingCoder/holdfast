import { defineConfig } from 'tsup'

export default defineConfig({
	entry: ['src/index.ts'],
	format: ['cjs', 'esm'],
	dts: true,
	splitting: false,
	sourcemap: true,
	clean: true,
	noExternal: [/^(?!require-in-the-middle).*/],
	external: ['require-in-the-middle'],
})
