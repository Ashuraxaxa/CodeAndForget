import { BrowserRouter, Link, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { LoginPage } from './pages/LoginPage'
import { ConnectionsPage } from './pages/ConnectionsPage'
import { ConnectionLayout, PickTable } from './pages/ConnectionLayout'
import { TablePage } from './pages/TablePage'
import { RecordPage } from './pages/RecordPage'

function Shell() {
  const { isAuthenticated, logout } = useAuth()
  if (!isAuthenticated) return <Navigate to="/login" replace />
  return (
    <div className="app">
      <header className="topbar">
        <Link to="/" className="brand">Mantenedores</Link>
        <button className="link" onClick={logout}>Salir</button>
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
          <Route element={<Shell />}>
            <Route index element={<ConnectionsPage />} />
            <Route path="c/:conn" element={<ConnectionLayout />}>
              <Route index element={<PickTable />} />
              <Route path="t/:schema/:table" element={<TablePage />} />
              <Route path="t/:schema/:table/new" element={<RecordPage key="new" mode="new" />} />
              <Route path="t/:schema/:table/edit" element={<RecordPage key="edit" mode="edit" />} />
            </Route>
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}
