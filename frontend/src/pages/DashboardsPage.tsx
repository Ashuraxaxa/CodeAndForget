import { ArrowLeft, LayoutDashboard, Lock, Plus, Trash2, Users } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { DashboardWidget } from '../api/types'
import { ErrorText } from '../components/ErrorText'
import { BarChart, DonutChart, LineChart, NumberTile, PivotTable, TopTable } from '../components/charts'
import { formatInstant } from '../lib/format'
import { useT } from '../i18n'
import { metricLabel } from '../lib/chartFormat'

/** Lista de dashboards (P-129, D-111): los propios y los compartidos con sus perfiles. */
export function DashboardsPage() {
  const t = useT()
  const qc = useQueryClient()
  const navigate = useNavigate()
  const list = useQuery({ queryKey: ['dashboards'], queryFn: api.dashboards })
  const [name, setName] = useState('')
  const create = useMutation({
    mutationFn: () => api.createDashboard({ name: name.trim() }),
    onSuccess: v => { qc.invalidateQueries({ queryKey: ['dashboards'] }); navigate(`/dashboards/${v.dashboard.id}`) },
  })
  const remove = useMutation({ mutationFn: api.deleteDashboard, onSuccess: () => qc.invalidateQueries({ queryKey: ['dashboards'] }) })

  function submit(e: FormEvent) {
    e.preventDefault()
    if (name.trim()) create.mutate()
  }

  const data = list.data
  return (
    <div className="page dashboards-page">
      <div className="page-header">
        <h1 className="with-icon"><LayoutDashboard size={22} aria-hidden />{t('Dashboards')}</h1>
        {data && data.limit != null && <span className="badge">{t('{n} de {max} en tu plan', { n: data.used, max: data.limit })}</span>}
      </div>
      {list.error && <div className="alert"><ErrorText error={list.error} /></div>}

      {data?.canCreate && (
        <form className="card dashboard-new" onSubmit={submit}>
          <label>
            {t('Nuevo dashboard')}
            <input value={name} onChange={e => setName(e.target.value)} maxLength={100} placeholder={t('Ventas del mes')} />
          </label>
          <button className="primary" disabled={!name.trim() || create.isPending}><Plus size={16} aria-hidden />{t('Crear')}</button>
          {create.error && <div className="alert"><ErrorText error={create.error} /></div>}
        </form>
      )}

      {data && data.items.length === 0 && (
        <p className="muted">{data.canCreate ? t('Todavía no hay dashboards. Crea el primero arriba.') : t('No hay dashboards compartidos contigo.')}</p>
      )}
      {remove.error && <div className="alert"><ErrorText error={remove.error} /></div>}

      <ul className="dashboard-list">
        {data?.items.map(d => (
          <li key={d.id} className={`card dashboard-card ${d.locked ? 'locked' : ''}`}>
            <Link to={`/dashboards/${d.id}`} className="dashboard-card-main">
              <strong>{d.name}</strong>
              {d.description && <span className="muted small">{d.description}</span>}
              <span className="muted small">
                {t('{n} tarjetas', { n: d.widgets })} · {t('de {owner}', { owner: d.owner })} · {formatInstant(d.updatedAt)}
              </span>
            </Link>
            <div className="dashboard-card-tags">
              {d.shared && <span className="badge" title={t('Compartido con perfiles')}><Users size={13} aria-hidden />{t('Compartido')}</span>}
              {d.locked && <span className="badge full" title={t('Supera el tope de tu plan: vuelve al subir de plan.')}><Lock size={13} aria-hidden />{t('Requiere otro plan')}</span>}
              {d.canDelete && (
                <button type="button" className="icon-button danger-text" title={t('Borrar')} aria-label={t('Borrar {name}', { name: d.name })}
                  disabled={remove.isPending}
                  onClick={() => { if (confirm(t('¿Borrar el dashboard "{name}"? No se puede deshacer.', { name: d.name }))) remove.mutate(d.id) }}>
                  <Trash2 size={16} aria-hidden />
                </button>
              )}
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}

// ---------- Un dashboard ----------

type RangeId = 'all' | '7d' | '30d' | '90d' | 'month' | 'year'

/** Desde (incluido) y hasta (excluido), como fechas locales AAAA-MM-DD. */
function rangeOf(id: RangeId): { from?: string; to?: string } {
  if (id === 'all') return {}
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const today = new Date()
  const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1)
  const from = id === 'month' ? new Date(today.getFullYear(), today.getMonth(), 1)
    : id === 'year' ? new Date(today.getFullYear(), 0, 1)
    : new Date(today.getFullYear(), today.getMonth(), today.getDate() - (id === '7d' ? 6 : id === '30d' ? 29 : 89))
  return { from: iso(from), to: iso(tomorrow) }
}

const RANGE_KEY = 'celdo.dashboard.range'

export function DashboardPage() {
  const t = useT()
  const { id = '' } = useParams()
  const view = useQuery({ queryKey: ['dashboard', id], queryFn: () => api.dashboard(id) })
  const [range, setRange] = useState<RangeId>(() => {
    try { return (localStorage.getItem(RANGE_KEY) as RangeId) || '30d' } catch { return '30d' }
  })
  const pick = (r: RangeId) => { setRange(r); try { localStorage.setItem(RANGE_KEY, r) } catch { /* sin almacenamiento */ } }

  if (view.isLoading) return <div className="page"><p className="muted">{t('Cargando…')}</p></div>
  if (view.error || !view.data) return (
    <div className="page">
      <Link className="back-link" to="/dashboards"><ArrowLeft size={16} aria-hidden />{t('Dashboards')}</Link>
      <div className="alert"><ErrorText error={view.error} /></div>
    </div>
  )
  const d = view.data.dashboard
  const usesDates = d.widgets.some(w => w.query.dateColumn)
  const ranges: [RangeId, string][] = [
    ['7d', t('Últimos 7 días')], ['30d', t('Últimos 30 días')], ['90d', t('Últimos 90 días')],
    ['month', t('Este mes')], ['year', t('Este año')], ['all', t('Todo')],
  ]

  return (
    <div className="page dashboard-page">
      <div className="page-header">
        <div>
          <Link className="back-link" to="/dashboards"><ArrowLeft size={16} aria-hidden />{t('Dashboards')}</Link>
          <h1>{d.name}</h1>
          {d.description && <p className="muted">{d.description}</p>}
        </div>
        {usesDates && (
          <label className="dashboard-range">
            <span className="muted small">{t('Período')}</span>
            <select value={range} onChange={e => pick(e.target.value as RangeId)}>
              {ranges.map(([v, label]) => <option key={v} value={v}>{label}</option>)}
            </select>
          </label>
        )}
      </div>

      {d.widgets.length === 0 ? (
        <div className="card empty-dashboard">
          <LayoutDashboard size={28} aria-hidden />
          <p>{t('Este dashboard todavía no tiene tarjetas.')}</p>
          <p className="muted small">{t('El editor de tarjetas llega en la próxima versión.')}</p>
        </div>
      ) : (
        <div className="dashboard-grid">
          {d.widgets.map(w => <WidgetCard key={w.id} dashboard={d.id} widget={w} range={w.query.dateColumn ? rangeOf(range) : {}} />)}
        </div>
      )}
    </div>
  )
}

function WidgetCard({ dashboard, widget, range }: { dashboard: string; widget: DashboardWidget; range: { from?: string; to?: string } }) {
  const t = useT()
  const data = useQuery({
    queryKey: ['dashboard', dashboard, 'widget', widget.id, range.from, range.to],
    queryFn: () => api.widgetData(dashboard, widget.id, range.from, range.to),
    staleTime: 60_000,
    retry: false,
  })
  const bucket = widget.query.groupBy[0]?.bucket
  const metric = metricLabel(t, widget)
  const rows = data.data?.rows ?? []

  let body: React.ReactNode = null
  if (data.isLoading) body = <div className="chart-skeleton" aria-busy="true" />
  else if (data.error) body = <div className="widget-error"><ErrorText error={data.error} /></div>
  else if (widget.type !== 'Number' && rows.length === 0) body = <p className="muted small">{t('Sin datos en este período.')}</p>
  else switch (widget.type) {
    case 'Number': body = <NumberTile value={rows[0]?.value} caption={metric} />; break
    case 'Bar': body = <BarChart rows={rows} bucket={bucket} />; break
    case 'Line': body = <LineChart rows={rows} bucket={bucket} />; break
    case 'Pie': body = <DonutChart rows={rows} bucket={bucket} />; break
    case 'Top': body = <TopTable rows={rows} bucket={bucket} groupLabel={widget.query.groupBy[0]?.column ?? ''} valueLabel={metric} />; break
    case 'Pivot': body = <PivotTable rows={rows} bucket={bucket} bucket2={widget.query.groupBy[1]?.bucket} />; break
    default: body = <p className="muted small">{t('Este tipo de tarjeta llega en una próxima versión.')}</p>
  }

  return (
    <section className={`card widget widget-${widget.size.toLowerCase()}`}>
      <header className="widget-head">
        <h2 title={widget.title}>{widget.title || metric}</h2>
        {widget.type !== 'Number' && <span className="muted small">{metric}</span>}
      </header>
      {body}
      {data.data?.truncated && widget.type !== 'Top' && <p className="muted small">{t('Se muestran los primeros {n} grupos.', { n: rows.length })}</p>}
    </section>
  )
}
