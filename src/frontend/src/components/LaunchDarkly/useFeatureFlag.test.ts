import { describe, it, expect } from 'vitest'
import { renderHook } from '@testing-library/react-hooks'
import { useFeatureFlag } from './useFeatureFlag'
import { flags } from './flags'

describe('useFeatureFlag', () => {
	it('returns default value for a known flag', () => {
		const { result } = renderHook(() =>
			useFeatureFlag('enable-session-card-text'),
		)
		expect(result.current).toBe(
			flags['enable-session-card-text'].defaultValue,
		)
	})

	it('returns default value for another known flag', () => {
		const { result } = renderHook(() =>
			useFeatureFlag('enable-session-card-style'),
		)
		expect(result.current).toBe(
			flags['enable-session-card-style'].defaultValue,
		)
	})

	it('returns undefined when there is no flag config', () => {
		const { result } = renderHook(() => useFeatureFlag('non-existent-flag'))
		expect(result.current).toBeUndefined()
	})
})
