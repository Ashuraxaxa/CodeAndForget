import { BrowserRouter, Outlet, Route, Routes } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Lock } from 'lucide-react'
import { api } from './api/client'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { CeldoMark } from './components/Brand'
import { LoginPage } from './pages/LoginPage'
import { TablePage } from './pages/TablePage'
import { RecordPage } from './pages/RecordPage'
import { ImportPage } from './pages/ImportPage'
import { DashboardPage } from './pages/DashboardsPage'
import { appBase, routerBase } from './lib/embed'
import { useT } from './i18n'

/**
 * Páginas embebidas (P-134, D-113): un dashboard o un mantenedor, sin menú ni barra superior. Con la sesión del
 * usuario (forma A, el login sale dentro del iframe) o con el token firmado de una aplicación (forma B, P-135).
 */
function EmbedShell() {
  const { config, isAuthenticated, embedPending, embedError } = useAuth()
  const t = useT()
  if (!config || embedPending) return <p className="muted pad">{t('Cargando…')}</p>
  // Con el token de una aplicación (forma B) no hay login: si falló, se dice por qué.
  if (embedError) return <div className="page"><div className="alert">{embedError}</div></div>
  if (!isAuthenticated) return <LoginPage />
  return <PlanGate />
}

/** Gratis no embebe; Pro lleva el sello "Hecho con Celdo"; Empresa va sin marca. */
function PlanGate() {
  const t = useT()
  const plan = useQuery({ queryKey: ['plan'], queryFn: api.plan, staleTime: 60_000 })
  if (plan.isLoading) return <p className="muted pad">{t('Cargando…')}</p>
  if (plan.data && !plan.data.features.embed) return (
    <div className="page">
      <div className="card empty-dashboard">
        <Lock size={28} aria-hidden />
        <p>{t('Embeber dashboards y mantenedores en otras aplicaciones está disponible desde el plan Pro.')}</p>
        <a className="button" href={`${appBase}/plan`} target="_blank" rel="noopener">{t('Ver planes')}</a>
      </div>
    </div>
  )
  return (
    <div className="embed">
      <Outlet />
      {plan.data?.plan === 'Pro' && (
        <footer className="app-footer">
          <a className="made-with" href="https://celdo.net" target="_blank" rel="noopener"><CeldoMark className="" />{t('Hecho con Celdo')}</a>
        </footer>
      )}
    </div>
  )
}

function NotAvailable() {
  const t = useT()
  return <div className="page"><p className="muted">{t('Esta página no se puede embeber. Se embeben dashboards y mantenedores.')}</p></div>
}

export default function EmbedApp() {
  return (
    <AuthProvider>
      <BrowserRouter basename={routerBase}>
        <Routes>
          <Route element={<EmbedShell />}>
            <Route path="dashboards/:id" element={<DashboardPage />} />
            <Route path="c/:conn/t/:schema/:table" element={<section className="content"><TablePage /></section>} />
            <Route path="c/:conn/t/:schema/:table/new" element={<RecordPage key="new" mode="new" />} />
            <Route path="c/:conn/t/:schema/:table/edit" element={<RecordPage key="edit" mode="edit" />} />
            <Route path="c/:conn/t/:schema/:table/import" element={<ImportPage />} />
            <Route path="*" element={<NotAvailable />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}
