import { defineConfig } from 'tsup'

export default defineConfig({
	entry: [
		'src/next-client.tsx',
		'src/config.ts',
		'src/server.edge.ts',
		'src/server.ts',
		'src/ssr.tsx',
	],
	format: ['cjs', 'esm'],
	dts: true,
	splitting: false,
	sourcemap: true,
	clean: true,
	external: [
		'@prisma/instrumentation',
		'next',
		'react',
		'require-in-the-middle',
	],
})
