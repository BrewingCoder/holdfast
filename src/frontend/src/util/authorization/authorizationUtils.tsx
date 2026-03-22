import { Admin, AdminRole, Maybe } from '@graph/schemas'

export const onlyAllowAdminRole = (admin?: Admin, role?: string) =>
	role === AdminRole.Admin

export const onlyAllowHighlightStaff = (_admin?: Maybe<Admin>) =>
	import.meta.env.DEV
