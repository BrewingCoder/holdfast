/*
Given a `BrowserWindow`, sets up event listeners for Highlight.
 */
export default function configureElectronHighlight(window: any) {
	if (window.on && window.webContents?.send) {
		window.on('focus', () => {
			window.webContents.send('@holdfast-io/browser', { visible: true })
		})

		window.on('blur', () => {
			window.webContents.send('@holdfast-io/browser', { visible: false })
		})

		window.on('close', () => {
			window.webContents.send('@holdfast-io/browser', { visible: false })
		})
	}
}
