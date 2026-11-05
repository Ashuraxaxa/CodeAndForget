import { useState } from 'react'
import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { DashboardWidget } from '../api/types'
import { WidgetView } from '../components/WidgetView'
import { CeldoMark } from '../components/Brand'
import { ErrorText } from '../components/ErrorText'
import { rangeOf, type RangeId } from '../lib/dashboardRange'
import { useT } from '../i18n'

/**
 * Dashboard público (P-137, D-116): lo mira cualquiera con el enlace, sin login. Solo se elige el período; las tarjetas
 * corren con los permisos del dueño y no abren la grilla.
 */
export function PublicDashboardPage() {
  const t = useT()
  const { token = '' } = useParams()
  const view = useQuery({ queryKey: ['public-dashboard', token], queryFn: () => api.publicDashboard(token), retry: false })
  const [range, setRange] = useState<RangeId>('30d')

  if (view.isLoading) return <div className="page"><p className="muted">{t('Cargando…')}</p></div>
  if (view.error || !view.data) return <div className="page"><div className="alert"><ErrorText error={view.error} /></div></div>
  const d = view.data
  const ranges: [RangeId, string][] = [
    ['7d', t('Últimos 7 días')], ['30d', t('Últimos 30 días')], ['90d', t('Últimos 90 días')],
    ['month', t('Este mes')], ['year', t('Este año')], ['all', t('Todo')],
  ]
  return (
    <div className="embed">
      <div className="page dashboard-page">
        <div className="page-header">
          <div>
            <h1>{d.name}</h1>
            {d.description && <p className="muted">{d.description}</p>}
          </div>
          {d.widgets.some(w => w.query.dateColumn) && (
            <label className="dashboard-range">
              <span className="muted small">{t('Período')}</span>
              <select value={range} onChange={e => setRange(e.target.value as RangeId)}>
                {ranges.map(([v, label]) => <option key={v} value={v}>{label}</option>)}
              </select>
            </label>
          )}
        </div>
        <div className="dashboard-grid">
          {d.widgets.map(w => <PublicCard key={w.id} token={token} widget={w} range={w.query.dateColumn ? rangeOf(range) : {}} />)}
        </div>
      </div>
      {d.seal && (
        <footer className="app-footer">
          <a className="made-with" href="https://celdo.net" target="_blank" rel="noopener"><CeldoMark className="" />{t('Hecho con Celdo')}</a>
        </footer>
      )}
    </div>
  )
}

function PublicCard({ token, widget, range }: { token: string; widget: DashboardWidget; range: { from?: string; to?: string } }) {
  const data = useQuery({
    queryKey: ['public-dashboard', token, widget.id, range.from, range.to],
    queryFn: () => api.publicWidgetData(token, widget.id, range.from, range.to),
    staleTime: 60_000,
    retry: false,
  })
  return <WidgetView widget={widget} data={data.data} isLoading={data.isLoading} error={data.error} />
}
