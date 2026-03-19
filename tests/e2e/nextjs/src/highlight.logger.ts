import { highlightConfig } from '@/instrumentation'
import { isNodeJsRuntime } from '@holdfast-io/next/server'
import type { LoggerOptions } from 'pino'

const pinoConfig = {
	level: 'debug',
	transport: {
		targets: [
			// {
			// 	target: 'pino-pretty',
			// 	level: 'debug',
			// },
			// {
			// 	target: '@holdfast-io/pino',
			// 	options: highlightConfig,
			// 	level: 'debug',
			// },
		],
	},
} as LoggerOptions

if (isNodeJsRuntime()) {
	const { H } = require('@holdfast-io/node')
	H.init(highlightConfig)
}

const logger = require('pino')(pinoConfig)
export default logger
