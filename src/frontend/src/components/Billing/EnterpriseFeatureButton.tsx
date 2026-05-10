import { PropsWithChildren } from 'react'

/**
 * EnterpriseFeatureButton historically gated features behind paid SaaS
 * tiers in upstream Highlight, popping a plan-comparison modal + Calendly
 * widget for any feature the workspace's plan didn't include.
 *
 * HoldFast is single-tenant and has no plan gating — every feature is
 * always available — so this component now passes through unconditionally.
 * The component is kept as a shim so the existing call sites still type-
 * check; HOL-49 removed the modal/Calendly internals and the related
 * Billing pages.
 */

interface Props {
	setting?: unknown
	name?: string
	fn: () => any
	onShowModal?: () => void
	onClose?: () => void
	className?: string
	variant?: 'basic'
	shown?: true
	disabled?: boolean
}

export default function EnterpriseFeatureButton({
	fn,
	children,
	className,
	variant,
	disabled,
}: PropsWithChildren<Props>) {
	const handleClick = async () => {
		if (disabled) return
		await fn()
	}

	if (variant === 'basic') {
		return (
			<div className={className} onClick={handleClick}>
				{children}
			</div>
		)
	}
	return (
		<button className={className} onClick={handleClick} disabled={disabled}>
			{children}
		</button>
	)
}
