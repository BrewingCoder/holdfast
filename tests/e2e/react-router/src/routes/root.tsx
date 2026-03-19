import { H } from '@holdfast-io/browser'

H.init('1', {
	// Get your project ID from the setup page in the dashboard
	networkRecording: {
		enabled: true,
		recordHeadersAndBody: true,
	},
})

export default function Root() {
	return (
		<div id="sidebar">
			<h1>Hello, world</h1>
		</div>
	)
}
