import { H } from '@holdfast-io/node'
import pino from 'pino'
import { config } from './instrumentation'

export function startPino() {
	H.init(config)

	const logger = pino({
		transport: {
			targets: [
				{
					target: '@holdfast-io/pino',
					options: {
						...config,
						serviceName: 'e2e-express-pino',
					},
					level: 'info',
				},
				{
					target: 'pino-pretty',
					options: {
						colorize: true,
					},
					level: 'info',
				},
			],
		},
	})

	H.runWithHeaders(
		'custom-span',
		{ 'x-highlight-request': '987654/321654' },
		() => {
			logger.info('hello world')

			const child = logger.child({ a: 'property' })

			child.info('hello child!')
		},
	)
}
