import { H, Metadata } from '@holdfast-io/browser'

const initialize = () => {}

const track = (event: string, metadata?: Record<string, unknown>) => {
	H.track(event, metadata as Metadata)
}

const identify = (email: string, traits?: Record<string, unknown>) => {
	H.identify(email, traits as Metadata)
}

const page = (_name: string, _properties?: Record<string, unknown>) => {}

const trackGaEvent = (_event: string, _properties?: Record<string, unknown>) => {}

const analytics = {
	initialize,
	track,
	identify,
	page,
	trackGaEvent,
}

export default analytics
