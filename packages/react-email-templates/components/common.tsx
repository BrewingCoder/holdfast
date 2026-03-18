import {
	Body,
	Container,
	Head,
	Html,
	Img,
	Preview,
} from '@react-email/components'
import * as React from 'react'

export const STATIC_ASSETS_BASE_URL = process.env.HOLDFAST_STATIC_ASSETS_URL || 'http://localhost:3000'

interface EmailHtmlProps extends React.PropsWithChildren {
	previewText: string
}

export const EmailHtml: React.FC<EmailHtmlProps> = ({
	children,
	previewText,
}) => {
	return (
		<Html>
			<Head>
				<style>{css}</style>
			</Head>
			<Preview>{previewText}</Preview>
			<Body style={main}>
				<Container width={400} style={container}>
					{children}
				</Container>
			</Body>
		</Html>
	)
}

const css = `
    a {
        color: unset;
		text-decoration: none;
    }
`

const main = {
	backgroundColor: '#0d0225',
	fontFamily: 'Helvetica, sans-serif',
}

const container = {
	margin: '0 auto',
	padding: '0 16px',
	textAlign: 'center' as const,
	width: '400px',
}

export const HighlightLogo = () => {
	return (
		<Img
			alt="Highlight logo"
			height="32"
			src={`${STATIC_ASSETS_BASE_URL}/assets/digest/logo-on-dark.png`}
			style={logo}
			width="32"
		/>
	)
}

const logo = {
	margin: '0 auto',
	paddingTop: '32px',
}
