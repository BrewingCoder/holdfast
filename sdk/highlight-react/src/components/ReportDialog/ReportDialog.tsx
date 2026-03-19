import React, { useMemo, useRef, useState } from 'react'

import classnames from 'classnames'
import styles from './styles.module.css'

export interface ReportDialogOptions {
	user?: {
		email?: string
		name?: string
	}
	title?: string
	subtitle?: string
	labelName?: string
	subtitle2?: string
	labelEmail?: string
	labelComments?: string
	placeholderComments?: string
	labelClose?: string
	labelSubmit?: string
	successMessage?: string
	successSubtitle?: string
	hideHighlightBranding?: boolean
	// if an error is passed, the ReportDialog will send it to highlight
	error?: Error
}

type ReportDialogProps = ReportDialogOptions & {
	onCloseHandler?: () => void
	onSubmitHandler?: () => void
}

export function ReportDialog({
	labelClose = 'Cancel',
	labelComments = 'Message',
	labelName = 'Name',
	labelEmail = 'Email',
	labelSubmit = 'Submit',
	subtitle2 = 'If you’d like to help, tell us what happened below.',
	subtitle = 'Our team has been notified.',
	successMessage = 'Your feedback has been sent. Thank you!',
	successSubtitle = "Thank you for sending us feedback. If you have any other concerns/questions, reach out to this application's support email.",
	title = 'It looks like we’re having issues.',
	placeholderComments = 'I typed in a name then clicked the button',
	user,
	onCloseHandler,
	onSubmitHandler,
	hideHighlightBranding = false,
	error,
}: ReportDialogProps) {
	const [name, setName] = useState(user?.name || '')
	const [email, setEmail] = useState(user?.email || '')
	const [verbatim, setVerbatim] = useState('')
	const [sendingReport, setSendingReport] = useState(false)
	const [sentReport, setSentReport] = useState(false)
	// We want to record when the feedback started, not when the feedback is submitted. If we record the latter, the timing could be way off.
	const reportDialogOpenTime = useRef(new Date().toISOString())
	const isValid = useMemo(() => {
		const isValidEmail = !!email.match(
			/^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,4}$/i,
		)

		return !!name && isValidEmail && !!verbatim
	}, [name, email, verbatim])

	React.useEffect(() => {
		if (window?.H?.consumeError && error) {
			window.H.consumeError(error)
		}
	}, [error])

	const handleSubmit = (event: React.SyntheticEvent) => {
		event.preventDefault()
		setSendingReport(true)

		if (window?.H?.addSessionFeedback) {
			window.H.addSessionFeedback({
				verbatim,
				userName: name,
				userEmail: email,
				timestampOverride: reportDialogOpenTime.current,
			})
		} else {
			console.warn(
				'HoldFast is not initialized. Make sure highlight.run is installed and running.',
			)
		}
		// fake a loading state because addSessionFeedback is async
		new Promise((r) => window.setTimeout(r, 300)).then(() => {
			setSendingReport(false)
			setSentReport(true)
			if (onSubmitHandler) {
				onSubmitHandler()
			}
		})
	}

	return (
		<>
			<style
				dangerouslySetInnerHTML={{
					__html: `
					@font-face {
						font-display: swap;
						font-family: 'Inter';
						font-style: normal;
						font-weight: normal;
						src: local('Inter Regular'), local('InterRegular'),
							url('/font/Inter-Regular.woff2')
								format('woff2');
					}
					@font-face {
						font-display: swap;
						font-family: 'Inter';
						font-style: normal;
						font-weight: 500;
						src: local('Inter Medium'), local('InterMedium'),
							url('/font/Inter-Medium.woff2')
								format('woff2');
					}

					::placeholder {
						color: var(--color-gray-300);
					}
			`,
				}}
			/>
			<main className={styles.container}>
				<div className={styles.card}>
					{sentReport ? (
						<div className={styles.cardContents}>
							<h1 className={styles.title}>{successMessage}</h1>
							<h4 className={styles.subtitle}>
								{successSubtitle}
							</h4>
							<div>
								<button
									className={classnames(
										styles.button,
										styles.confirmationButton,
									)}
									onClick={onCloseHandler}
								>
									Close
								</button>
							</div>
						</div>
					) : (
						<div className={styles.cardContents}>
							<div>
								<h1 className={styles.title}>{title}</h1>
								<h2 className={styles.subtitle}>
									{subtitle} {subtitle2}
								</h2>
							</div>
							<form
								className={styles.form}
								onSubmit={handleSubmit}
							>
								<label>
									{labelName}
									<input
										type="text"
										value={name}
										name="name"
										autoFocus
										onChange={(e) => {
											setName(e.target.value)
										}}
										placeholder="Tom Jerry"
									/>
								</label>

								<label>
									{labelEmail}
									<input
										type="email"
										value={email}
										name="email"
										onChange={(e) => {
											setEmail(e.target.value)
										}}
										placeholder="mail@mail.com"
									/>
								</label>

								<label className={styles.textareaLabel}>
									{labelComments}
									<textarea
										value={verbatim}
										placeholder={placeholderComments}
										name="verbatim"
										rows={3}
										onChange={(e) => {
											setVerbatim(e.target.value)
										}}
									/>
								</label>

								<div className={styles.formFooter}>
									<div
										className={styles.formActionsContainer}
									>
										<button
											type="submit"
											disabled={!isValid || sendingReport}
										>
											{labelSubmit}
										</button>

										<button
											className={styles.closeButton}
											onClick={onCloseHandler}
											type="button"
										>
											{labelClose}
										</button>
									</div>

									{!hideHighlightBranding && (
										<div className={styles.ad}>
											<div
											className={
												styles.logoContainer
											}
										>
											<span>
												Powered by HoldFast
											</span>
										</div>
										</div>
									)}
								</div>
							</form>
						</div>
					)}
				</div>
			</main>
		</>
	)
}

declare var window: any
