import React from 'react'
import { createContext } from '@/util/context/context'

type LaunchDarklyContextType = {
	setUserContext: (userContext: Record<string, unknown>) => void
	setWorkspaceContext: (workspaceContext: Record<string, unknown>) => void
}

export const [useLaunchDarklyContext, LaunchDarklyContextProvider] =
	createContext<LaunchDarklyContextType>('LaunchDarklyContext')

type LaunchDarklyProviderProps = {
	clientSideID?: string
	email?: string
}

export const LaunchDarklyProvider: React.FC<
	React.PropsWithChildren<LaunchDarklyProviderProps>
> = ({ children }) => {
	return (
		<LaunchDarklyContextProvider
			value={{
				setUserContext: () => {},
				setWorkspaceContext: () => {},
			}}
		>
			{children}
		</LaunchDarklyContextProvider>
	)
}
