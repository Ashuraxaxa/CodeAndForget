import { BrowserRouter, Link, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { AuthCallbackPage, LoginPage } from './pages/LoginPage'
import { ConnectionsPage } from './pages/ConnectionsPage'
import { ConnectionLayout, PickTable } from './pages/ConnectionLayout'
import { TablePage } from './pages/TablePage'
import { RecordPage } from './pages/RecordPage'
import { ConfigPage } from './pages/ConfigPage'
import { ImportPage } from './pages/ImportPage'

function Shell() {
  const { config, isAuthenticated, logout } = useAuth()
  if (!config) return <p className="muted pad">Cargando…</p>
  if (!isAuthenticated) return <Navigate to="/login" replace />
  return (
    <div className="app">
      <header className="topbar">
        <Link to="/" className="brand">Mantenedores</Link>
        {/* Con un proxy que maneja la sesión no hay "salir" desde la app. */}
        {!(config.mode === 'Jwt' && !config.clientId) && <button className="link" onClick={logout}>Salir</button>}
      </header>
      <Outlet />
    </div>
  )
}

/** Subruta donde está publicada la app, tomada de <base href> (p. ej. "/mantenedores"). */
const basename = new URL(document.baseURI).pathname.replace(/\/$/, '')

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter basename={basename}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/auth/callback" element={<AuthCallbackPage />} />
          <Route element={<Shell />}>
            <Route index element={<ConnectionsPage />} />
            <Route path="c/:conn" element={<ConnectionLayout />}>
              <Route index element={<PickTable />} />
              <Route path="t/:schema/:table" element={<TablePage />} />
              <Route path="t/:schema/:table/new" element={<RecordPage key="new" mode="new" />} />
              <Route path="t/:schema/:table/edit" element={<RecordPage key="edit" mode="edit" />} />
              <Route path="t/:schema/:table/config" element={<ConfigPage />} />
              <Route path="t/:schema/:table/import" element={<ImportPage />} />
            </Route>
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}
