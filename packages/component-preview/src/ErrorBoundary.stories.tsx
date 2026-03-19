import { ErrorBoundary, SampleBuggyButton } from '@holdfast-io/react'

import React from 'react'

export const Basic: React.FC = () => {
	return (
		<ErrorBoundary>
			<SampleBuggyButton />
		</ErrorBoundary>
	)
}
