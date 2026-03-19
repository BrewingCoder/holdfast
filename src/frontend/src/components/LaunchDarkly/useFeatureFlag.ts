import { flags } from './flags'

export function useFeatureFlag(flag: keyof typeof flags) {
	const flagConfig = flags[flag] ?? {}
	return (flagConfig as { defaultValue?: string | boolean }).defaultValue
}
