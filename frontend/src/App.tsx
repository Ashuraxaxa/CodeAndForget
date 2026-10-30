import { BrowserRouter, Link, Navigate, NavLink, Outlet, Route, Routes } from 'react-router-dom'
import { History, LogOut, ShieldCheck } from 'lucide-react'
import { Brand } from './components/Brand'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { AuthCallbackPage, LoginPage } from './pages/LoginPage'
import { ConnectionsPage } from './pages/ConnectionsPage'
import { ConnectionLayout, PickTable } from './pages/ConnectionLayout'
import { TablePage } from './pages/TablePage'
import { RecordPage } from './pages/RecordPage'
import { ConfigPage } from './pages/ConfigPage'
import { ImportPage } from './pages/ImportPage'
import { SecurityPage } from './pages/SecurityPage'
import { AuditPage } from './pages/AuditPage'
import { TokensPage } from './pages/TokensPage'
import { useCan, useMe } from './lib/useMe'
import { ThemeToggle } from './components/ThemeToggle'
import { LanguageSelect, useT } from './i18n'

/** Solo se monta con sesión iniciada: así /me no se pide sin token. */
function AdminLinks() {
  const t = useT()
  const canAudit = useCan('ViewAudit')
  const canSecure = useCan('ManageSecurity')
  return <>
    {canAudit && <NavLink to="/auditoria" className="topnav-link"><History size={16} aria-hidden />{t('Auditoría')}</NavLink>}
    {canSecure && <NavLink to="/seguridad" className="topnav-link"><ShieldCheck size={16} aria-hidden />{t('Seguridad')}</NavLink>}
  </>
}

/** Nombre del usuario con su inicial, como en cualquier app. */
function UserChip() {
  const name = useMe().data?.name
  const t = useT()
  if (!name) return null
  // Al tocar el nombre: los tokens de acceso del usuario (P-113).
  return <Link to="/tokens" className="user-chip" title={t('{name} · tokens de acceso', { name })}><span className="avatar" aria-hidden>{name[0].toUpperCase()}</span><span className="user-name">{name}</span></Link>
}

function Shell() {
  const { config, isAuthenticated, logout } = useAuth()
  const t = useT()
  if (!config) return <p className="muted pad">{t('Cargando…')}</p>
  if (!isAuthenticated) return <Navigate to="/login" replace />
  return (
    <div className="app">
      <header className="topbar">
        <Link to="/" className="brand-link"><Brand /></Link>
        <nav className="topnav">
          <AdminLinks />
        </nav>
        <div className="topbar-tools">
          <ThemeToggle />
          <LanguageSelect />
          <UserChip />
          {/* Con un proxy que maneja la sesión no hay "salir" desde la app. */}
          {!(config.mode === 'Jwt' && !config.clientId) && (
            <button className="icon-button" onClick={logout} title={t('Salir')} aria-label={t('Salir')}><LogOut size={17} aria-hidden /></button>
          )}
        </div>
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
            <Route path="seguridad" element={<SecurityPage />} />
            <Route path="auditoria" element={<AuditPage />} />
            <Route path="tokens" element={<TokensPage />} />
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
