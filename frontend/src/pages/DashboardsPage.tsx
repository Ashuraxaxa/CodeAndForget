import { ArrowLeft, Check, Copy, Globe, LayoutDashboard, Lock, Pencil, Plus, Trash2, Users } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { DashboardEditor } from './DashboardEditor'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { DashboardWidget } from '../api/types'
import { ErrorText } from '../components/ErrorText'
import { WidgetView } from '../components/WidgetView'
import { formatInstant } from '../lib/format'
import { useT } from '../i18n'
import { rangeOf, type RangeId } from '../lib/dashboardRange'
import { appBase, isEmbed } from '../lib/embed'

/** Lista de dashboards (P-129, D-111): los propios y los compartidos con sus perfiles. */
export function DashboardsPage() {
  const t = useT()
  const qc = useQueryClient()
  const navigate = useNavigate()
  const list = useQuery({ queryKey: ['dashboards'], queryFn: api.dashboards })
  const [name, setName] = useState('')
  const create = useMutation({
    mutationFn: () => api.createDashboard({ name: name.trim() }),
    onSuccess: v => { qc.invalidateQueries({ queryKey: ['dashboards'] }); navigate(`/dashboards/${v.dashboard.id}?editar=1`) },
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
              {d.isPublic && <span className="badge" title={t('Cualquiera con el enlace puede mirarlo')}><Globe size={13} aria-hidden />{t('Público')}</span>}
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

const RANGE_KEY = 'celdo.dashboard.range'
const REFRESH_KEY = 'celdo.dashboard.refresh'

export function DashboardPage() {
  const t = useT()
  const { id = '' } = useParams()
  const view = useQuery({ queryKey: ['dashboard', id], queryFn: () => api.dashboard(id) })
  const plan = useQuery({ queryKey: ['plan'], queryFn: api.plan, staleTime: 60_000 })
  const settings = useQuery({ queryKey: ['embed-settings'], queryFn: api.embedSettings, staleTime: 60_000, enabled: !isEmbed })
  const [sharing, setSharing] = useState(false)
  const [params, setParams] = useSearchParams()
  // Embebido solo se mira: se edita en la app.
  const canEdit = view.data?.canEdit === true && !isEmbed
  const editing = params.get('editar') === '1' && canEdit
  const setEditing = (on: boolean) => setParams(p => { const n = new URLSearchParams(p); if (on) n.set('editar', '1'); else n.delete('editar'); return n }, { replace: true })
  const [range, setRange] = useState<RangeId>(() => {
    try { return (localStorage.getItem(RANGE_KEY) as RangeId) || '30d' } catch { return '30d' }
  })
  // Refresco automático (P-131): para una pantalla a la vista, p. ej. en una sala.
  const [refresh, setRefresh] = useState<number>(() => { try { return Number(localStorage.getItem(REFRESH_KEY)) || 0 } catch { return 0 } })
  const pickRefresh = (n: number) => { setRefresh(n); try { localStorage.setItem(REFRESH_KEY, String(n)) } catch { /* sin almacenamiento */ } }
  const pick = (r: RangeId) => { setRange(r); try { localStorage.setItem(RANGE_KEY, r) } catch { /* sin almacenamiento */ } }

  if (view.isLoading) return <div className="page"><p className="muted">{t('Cargando…')}</p></div>
  if (view.error || !view.data) return (
    <div className="page">
      {!isEmbed && <Link className="back-link" to="/dashboards"><ArrowLeft size={16} aria-hidden />{t('Dashboards')}</Link>}
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
          {!isEmbed && <Link className="back-link" to="/dashboards"><ArrowLeft size={16} aria-hidden />{t('Dashboards')}</Link>}
          <h1>{d.name}</h1>
          {d.description && <p className="muted">{d.description}</p>}
        </div>
        <div className="actions">
        {canEdit && !editing && <button type="button" onClick={() => setEditing(true)}><Pencil size={15} aria-hidden />{t('Editar')}</button>}
        {view.data.canShare && !editing && !isEmbed && (settings.data?.publicDashboards || view.data.isPublic) && (
          <button type="button" className={view.data.isPublic ? 'active' : ''} onClick={() => setSharing(s => !s)}><Globe size={15} aria-hidden />{t('Enlace público')}</button>
        )}
        {d.widgets.length > 0 && !editing && (
          <label className="dashboard-range">
            <span className="muted small">{t('Actualizar')}</span>
            <select value={refresh} onChange={e => pickRefresh(Number(e.target.value))}>
              <option value={0}>{t('Solo al abrir')}</option>
              <option value={60}>{t('Cada minuto')}</option>
              <option value={300}>{t('Cada 5 minutos')}</option>
              <option value={900}>{t('Cada 15 minutos')}</option>
            </select>
          </label>
        )}
        {usesDates && (
          <label className="dashboard-range">
            <span className="muted small">{t('Período')}</span>
            <select value={range} onChange={e => pick(e.target.value as RangeId)}>
              {ranges.map(([v, label]) => <option key={v} value={v}>{label}</option>)}
            </select>
          </label>
        )}
        </div>
      </div>

      {sharing && !editing && <PublicLinkCard id={d.id} token={d.public?.token ?? null} enabled={settings.data?.publicDashboards === true} />}

      {editing && <DashboardEditor view={view.data} widgetLimit={plan.data?.limits.widgets} range={range} onDone={() => setEditing(false)} />}

      {editing ? null : d.widgets.length === 0 ? (
        <div className="card empty-dashboard">
          <LayoutDashboard size={28} aria-hidden />
          <p>{t('Este dashboard todavía no tiene tarjetas.')}</p>
          {canEdit && <button type="button" onClick={() => setEditing(true)}><Plus size={15} aria-hidden />{t('Agregar tarjeta')}</button>}
        </div>
      ) : (
        <div className="dashboard-grid">
          {d.widgets.map(w => <WidgetCard key={w.id} dashboard={d.id} widget={w} range={w.query.dateColumn ? rangeOf(range) : {}} refresh={refresh} />)}
        </div>
      )}
    </div>
  )
}

/** Enlace público del dashboard (P-137): crearlo, copiarlo, renovarlo o quitarlo. Solo el dueño o un administrador. */
function PublicLinkCard({ id, token, enabled }: { id: string; token: string | null; enabled: boolean }) {
  const t = useT()
  const qc = useQueryClient()
  const [copied, setCopied] = useState<string | null>(null)
  const done = () => { qc.invalidateQueries({ queryKey: ['dashboard', id] }); qc.invalidateQueries({ queryKey: ['dashboards'] }) }
  const publish = useMutation({ mutationFn: () => api.publishDashboard(id), onSuccess: done })
  const remove = useMutation({ mutationFn: () => api.unpublishDashboard(id), onSuccess: done })
  const url = token ? new URL(`${appBase}/embed/p/${token}`, location.origin).href : null
  const iframe = url ? `<iframe src="${url}" style="width:100%;height:900px;border:0" loading="lazy"></iframe>` : null
  const copy = (text: string) => { navigator.clipboard?.writeText(text); setCopied(text) }
  return (
    <div className="card public-link">
      <p className="muted small">{t('Cualquiera con el enlace mira este dashboard sin iniciar sesión, con tus permisos sobre los datos. Solo puede elegir el período: no edita ni abre las tablas.')}</p>
      {!enabled && <div className="alert">{t('Los dashboards públicos están apagados: un administrador los habilita en Seguridad → Aplicaciones.')}</div>}
      {url && iframe ? (
        <>
          <div className="row"><code className="token-secret">{url}</code>
            <button type="button" onClick={() => copy(url)}>{copied === url ? <Check size={16} aria-hidden /> : <Copy size={16} aria-hidden />}{t('Copiar enlace')}</button></div>
          <div className="row"><code className="token-secret">{iframe}</code>
            <button type="button" onClick={() => copy(iframe)}>{copied === iframe ? <Check size={16} aria-hidden /> : <Copy size={16} aria-hidden />}{t('Copiar iframe')}</button></div>
          <div className="actions">
            <button type="button" disabled={publish.isPending || !enabled}
              onClick={() => { if (confirm(t('¿Crear un enlace nuevo? El actual deja de funcionar.'))) publish.mutate() }}>{t('Nuevo enlace')}</button>
            <button type="button" className="danger" disabled={remove.isPending} onClick={() => remove.mutate()}>{t('Quitar enlace')}</button>
          </div>
        </>
      ) : (
        <div className="actions"><button type="button" className="primary" disabled={publish.isPending || !enabled} onClick={() => publish.mutate()}><Globe size={15} aria-hidden />{t('Crear enlace público')}</button></div>
      )}
      {(publish.error || remove.error) && <div className="alert"><ErrorText error={publish.error ?? remove.error} /></div>}
    </div>
  )
}

function WidgetCard({ dashboard, widget, range, refresh }: { dashboard: string; widget: DashboardWidget; range: { from?: string; to?: string }; refresh: number }) {
  const data = useQuery({
    queryKey: ['dashboard', dashboard, 'widget', widget.id, range.from, range.to],
    queryFn: () => api.widgetData(dashboard, widget.id, range.from, range.to),
    staleTime: refresh ? refresh * 1000 - 1000 : 60_000,
    refetchInterval: refresh ? refresh * 1000 : false,
    retry: false,
  })
  return <WidgetView widget={widget} data={data.data} isLoading={data.isLoading} error={data.error} interactive={!isEmbed} />
}
