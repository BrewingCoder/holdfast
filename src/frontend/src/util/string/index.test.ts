import { validateEmail } from './index'

describe('validateEmail', () => {
	const CASES = [
		['', false],
		['.@@holdfast-io/browser', false],
		['foo@bar.', false],
		['foo', false],
		['foo@Æ.run', false],
		['¥@@holdfast-io/browser', true],
		['foo@@holdfast-io/browser', true],
	]

	it.each(CASES)('should validate %s as %s', (email, expected) => {
		expect(validateEmail(email as string)).toBe(expected as Boolean)
	})
})
