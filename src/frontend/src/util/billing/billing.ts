import { PlanType } from '@graph/schemas'

export const isProjectWithinTrial = (_project: any) => {
	// HoldFast: no trials — always return false
	return false
}

export const getPlanChangeEmail = ({
	workspaceID,
	planType,
}: {
	workspaceID?: string | undefined
	planType: PlanType
}) => {
	let href =
		`mailto:sales@@holdfast-io/browser?subject=Highlight Subscription Update` +
		`&body=I would like to change my subscription to the following plan: ${planType}.`
	if (workspaceID) {
		href = href + ` My workspace ID is ${workspaceID}.`
	}
	return href
}
