import { useAuthContext } from '@authentication/AuthContext'
import React, { lazy, Suspense } from 'react'
import { Navigate, Route, Routes } from 'react-router-dom'

import { RelatedResourcePanel } from '@/components/RelatedResources/RelatedResourcePanel'
import { SignInRedirect } from '@/pages/Auth/SignInRedirect'

// Lazy-load all core route components so Vite splits them into separate chunks.
// Named exports need the .then(m => ({ default: m.X })) wrapper for React.lazy.
const PlayerPage = lazy(() =>
	import('@pages/Player/PlayerPage').then((m) => ({ default: m.PlayerPage })),
)
const ErrorsV2 = lazy(() => import('@pages/ErrorsV2/ErrorsV2'))
const TracesPage = lazy(() =>
	import('@/pages/Traces/TracesPage').then((m) => ({ default: m.TracesPage })),
)
const LogsPage = lazy(() => import('@pages/LogsPage/LogsPage'))
const SettingsRouter = lazy(() =>
	import('@/pages/SettingsRouter/SettingsRouter').then((m) => ({
		default: m.SettingsRouter,
	})),
)
const AlertsRouter = lazy(() => import('@pages/Alerts/AlertsRouter'))
const LogAlertsRouter = lazy(
	() => import('@pages/Alerts/LogAlert/LogAlertRouter'),
)
const ConnectRouter = lazy(() =>
	import('@/pages/Connect/ConnectRouter').then((m) => ({
		default: m.ConnectRouter,
	})),
)
const IntegrationsPage = lazy(
	() => import('@pages/IntegrationsPage/IntegrationsPage'),
)
const DashboardRouter = lazy(() => import('@/pages/Graphing/DashboardRouter'))

const BASE_PATH = 'sessions'

const ApplicationRouter: React.FC = () => {
	const { isLoggedIn } = useAuthContext()

	return (
		<>
			<Suspense fallback={null}>
				<Routes>
					<Route
						path="sessions/:session_secure_id?"
						element={<PlayerPage />}
					/>

					<Route
						path="errors/:error_secure_id?/:error_tab_key?/:error_object_id?"
						element={<ErrorsV2 />}
					/>

					{isLoggedIn ? (
						<>
							<Route
								path="traces/:trace_id?/:span_id?"
								element={<TracesPage />}
							/>
							<Route
								path="logs/:log_cursor?"
								element={<LogsPage />}
							/>
							<Route
								path="settings/*"
								element={<SettingsRouter />}
							/>
							<Route path="alerts/*" element={<AlertsRouter />} />
							<Route
								path="alerts/logs/*"
								element={<LogAlertsRouter />}
							/>
							<Route
								path="connect/*"
								element={<ConnectRouter />}
							/>
							<Route
								path="integrations/*"
								element={<IntegrationsPage />}
							/>
							<Route
								path="dashboards/*"
								element={<DashboardRouter />}
							/>
							<Route
								path="*"
								element={<Navigate to={BASE_PATH} replace />}
							/>
						</>
					) : (
						<Route path="*" element={<SignInRedirect />} />
					)}
				</Routes>
			</Suspense>
			<RelatedResourcePanel />
		</>
	)
}

export default ApplicationRouter
