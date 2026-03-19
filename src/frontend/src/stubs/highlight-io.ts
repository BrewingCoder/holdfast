/**
 * Stub for the removed highlight.io marketing site package.
 * The Connect pages imported quickstart content from the marketing site.
 * This provides empty defaults so the pages still compile.
 */

export interface QuickStartContent {
	title: string
	subtitle: string
	logoUrl?: string
	entries: Array<{
		title: string
		content: string
		code?: Array<{
			text: string
			language: string
		}>
	}>
}

export const quickStartContentReorganized: Record<string, QuickStartContent[]> = {}
