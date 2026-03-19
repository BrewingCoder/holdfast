import { useEffect } from 'react'

import type { HighlightOptions } from '@holdfast-io/browser'
import { H } from '@holdfast-io/browser'
import Cookies from 'js-cookie'
import { SESSION_SECURE_ID } from './constants'

export { H } from '@holdfast-io/browser'

interface Props extends HighlightOptions {
	excludedHostnames?: string[]
	projectId?: string
}

export function HighlightInit({
	excludedHostnames = [],
	projectId,
	...highlightOptions
}: Props) {
	useEffect(() => {
		const shouldRender =
			projectId &&
			excludedHostnames.every(
				(hostname) => !window.location.hostname.includes(hostname),
			)

		if (shouldRender) {
			const { sessionSecureID } = H.init(projectId, highlightOptions) || {
				sessionSecureID: '',
			}

			if (sessionSecureID) {
				Cookies.set(SESSION_SECURE_ID, sessionSecureID)
			}
		}
	}, [projectId, highlightOptions])

	return null
}
