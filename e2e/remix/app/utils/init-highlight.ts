import { H } from '@holdfast-io/node'
import { CONSTANTS } from '~/constants'

export function initHighlight() {
	H.init({ projectID: CONSTANTS.HIGHLIGHT_PROJECT_ID })
}
