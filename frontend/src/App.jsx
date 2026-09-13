import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import AppShell from './components/AppShell'
import ErrorBoundary from './components/ErrorBoundary'
import { ToastProvider } from './components/Toast'
import { ErrorState } from './components/ui'
import { AppearanceProvider } from './context/AppContext'
import { AuthProvider, useAuth } from './context/AuthContext'
import Dashboard from './pages/Dashboard'
import Datasets from './pages/Datasets'
import DataExplorer from './pages/DataExplorer'
import Analytics from './pages/Analytics'
import AiAnalyst from './pages/AiAnalyst'
import History from './pages/History'
import { ReportReader, ReportsList } from './pages/Reports'
import Settings from './pages/Settings'
import SignIn from './pages/SignIn'
import SignUp from './pages/SignUp'
import NotFound from './pages/NotFound'

/**
 * Where to go once signed in: the page the visitor was sent away from, if it is
 * a path inside this app. Anything else — another origin, a protocol-relative
 * URL, the sign-in pages themselves — goes to the dashboard.
 */
function returnPath(state) {
  const from = state?.from
  const path = typeof from?.pathname === 'string' ? from.pathname : ''
  if (!path.startsWith('/') || path.startsWith('//') || path === '/login' || path === '/signup') return '/'
  return `${path}${from.search ?? ''}${from.hash ?? ''}`
}

function SessionCheck() {
  return (
    <div
      role="status"
      className="min-h-screen bg-surface flex items-center justify-center font-body-main text-body-main text-on-surface-variant"
    >
      Checking your session…
    </div>
  )
}

function SessionUnavailable() {
  const { error, refresh } = useAuth()
  return (
    <div className="min-h-screen bg-surface flex items-center justify-center p-md">
      <div className="w-full max-w-md">
        <ErrorState error={error} onRetry={refresh} />
      </div>
    </div>
  )
}

/** The app itself, for a signed-in account only. */
function RequireAccount({ children }) {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') return <SessionCheck />
  if (status === 'unavailable') return <SessionUnavailable />
  if (status === 'signed-out') return <Navigate to="/login" replace state={{ from: location }} />
  return children
}

/** Sign-in and sign-up. An account that is already signed in is sent on to the app. */
function GuestOnly({ children }) {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') return <SessionCheck />
  if (status === 'signed-in') return <Navigate to={returnPath(location.state)} replace />
  return children
}

export default function App() {
  return (
    <ErrorBoundary>
      <AppearanceProvider>
        <ToastProvider>
          <AuthProvider>
            <Routes>
              <Route path="login" element={<GuestOnly><SignIn /></GuestOnly>} />
              <Route path="signup" element={<GuestOnly><SignUp /></GuestOnly>} />

              <Route element={<RequireAccount><AppShell /></RequireAccount>}>
                <Route index element={<Dashboard />} />
                <Route path="datasets" element={<Datasets />} />
                <Route path="explorer" element={<DataExplorer />} />
                <Route path="analytics" element={<Analytics />} />
                <Route path="analyst" element={<AiAnalyst />} />
                <Route path="history" element={<History />} />
                <Route path="reports" element={<ReportsList />} />
                <Route path="reports/:id" element={<ReportReader />} />
                <Route path="settings" element={<Settings />} />
                <Route path="*" element={<NotFound />} />
              </Route>
            </Routes>
          </AuthProvider>
        </ToastProvider>
      </AppearanceProvider>
    </ErrorBoundary>
  )
}
