import { ReportDialog as ReactReportDialog } from '@holdfast-io/react'
import React from 'react'

export function ReportDialog() {
	return typeof window === 'object' ? <ReactReportDialog /> : null
}
