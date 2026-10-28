import { ChevronLeft, ChevronRight } from 'lucide-react'
import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { formatInstant } from '../lib/format'
import { api } from '../api/client'
import type { AuditEntry, AuditOperation, TransferFormat } from '../api/types'
import { useMe } from '../lib/useMe'
import { useT, type Translate } from '../i18n'

const opLabels = (t: Translate): Record<AuditOperation, string> => ({
  Create: t('Alta'), Update: t('Edición'), Delete: t('Baja'), Import: t('Importación'), Links: t('Vínculos N:M'), Action: t('Acción'),
})
const PAGE_SIZE = 50

/** Auditoría de cambios (P-044): filtros en la URL, así "Historial" de un registro llega ya filtrado. Requiere ViewAudit (D-068). */
export function AuditPage() {
  const me = useMe()
  const t = useT()
  const OPS = opLabels(t)
  const allowed = me.data?.permissions.includes('ViewAudit') === true
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? '1') || 1
  const connections = useQuery({ queryKey: ['connections'], queryFn: api.connections, enabled: allowed })
  // Filtros para la API: las fechas del filtro son del día local completo.
  const apiParams = () => {
    const q = new URLSearchParams(params)
    q.set('pageSize', String(PAGE_SIZE))
    const from = q.get('from'); if (from) q.set('from', new Date(`${from}T00:00:00`).toISOString())
    const to = q.get('to'); if (to) { const d = new Date(`${to}T00:00:00`); d.setDate(d.getDate() + 1); q.set('to', d.toISOString()) }
    return q
  }
  const audit = useQuery({
    queryKey: ['audit', params.toString()],
    queryFn: () => api.audit(apiParams()),
    enabled: allowed,
  })
  const [exporting, setExporting] = useState(false)
  async function exportAs(format: TransferFormat) {
    setExporting(true)
    try { await api.exportAudit(apiParams(), format) }
    catch (e) { alert((e as Error).message) }
    finally { setExporting(false) }
  }

  const set = (name: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(name, value); else next.delete(name)
    if (name !== 'page') next.delete('page')
    setParams(next, { replace: true })
  }
  const keyFilter = [...params.entries()].filter(([k]) => k.startsWith('k.'))

  if (me.isLoading) return <p className="muted">{t('Cargando…')}</p>
  if (!allowed) return <div className="page"><div className="alert">{t('Necesita el permiso para ver la auditoría.')}</div></div>

  const total = audit.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="page">
      <div className="page-header">
        <h1>{t('Auditoría')}</h1>
        <div className="actions">
          <span className="muted small">{t('{n} cambios', { n: total })}</span>
          <select className="export" value="" title={t('Exportar lo que muestran los filtros')} disabled={exporting || total === 0}
            onChange={e => e.target.value && exportAs(e.target.value as TransferFormat)}>
            <option value="">{exporting ? t('Exportando…') : t('Exportar')}</option>
            <option value="csv">CSV</option>
            <option value="xlsx">Excel</option>
          </select>
        </div>
      </div>

      <div className="card audit-filters">
        <select value={params.get('conn') ?? ''} onChange={e => set('conn', e.target.value)} aria-label={t('Conexión')}>
          <option value="">{t('Todas las conexiones')}</option>
          {connections.data?.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <FilterText label={t('Esquema')} value={params.get('schema') ?? ''} onChange={v => set('schema', v)} />
        <FilterText label={t('Tabla')} value={params.get('table') ?? ''} onChange={v => set('table', v)} />
        <FilterText label={t('Usuario')} value={params.get('user') ?? ''} onChange={v => set('user', v)} />
        <select value={params.get('op') ?? ''} onChange={e => set('op', e.target.value)} aria-label={t('Operación')}>
          <option value="">{t('Todas las operaciones')}</option>
          {Object.entries(OPS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
        <label className="check">{t('Desde')} <input type="date" value={params.get('from') ?? ''} onChange={e => set('from', e.target.value)} /></label>
        <label className="check">{t('Hasta')} <input type="date" value={params.get('to') ?? ''} onChange={e => set('to', e.target.value)} /></label>
        {keyFilter.length > 0 && (
          <span className="chip">
            {t('Registro')} {keyFilter.map(([k, v]) => `${k.slice(2)}=${v}`).join(', ')}{' '}
            <button className="link" onClick={() => {
              const next = new URLSearchParams(params)
              for (const [k] of keyFilter) next.delete(k)
              setParams(next, { replace: true })
            }}>×</button>
          </span>
        )}
      </div>

      {audit.error && <div className="alert">{(audit.error as Error).message}</div>}

      <div className="table-wrap">
        <table className="grid audit">
          <thead>
            <tr><th>{t('Fecha')}</th><th>{t('Usuario')}</th><th>{t('Operación')}</th><th>{t('Tabla')}</th><th>{t('Registro')}</th><th>{t('Cambios')}</th></tr>
          </thead>
          <tbody className={audit.isFetching ? 'loading' : undefined}>
            {audit.data?.entries.map(e => <Entry key={e.id} entry={e} />)}
            {audit.data?.entries.length === 0 && <tr><td colSpan={6} className="empty">{t('Sin cambios registrados')}</td></tr>}
          </tbody>
        </table>
      </div>

      <div className="pager">
        <button disabled={page <= 1} onClick={() => set('page', String(page - 1))}><ChevronLeft size={16} aria-hidden />{t('Anterior')}</button>
        <span>{t('Página {page} de {pages}', { page, pages })}</span>
        <button disabled={page >= pages} onClick={() => set('page', String(page + 1))}>{t('Siguiente')}<ChevronRight size={16} aria-hidden /></button>
      </div>
    </div>
  )
}

function FilterText({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  // Se aplica al salir del campo o con Enter, para no consultar en cada tecla.
  const [text, setText] = useState(value)
  return (
    <input placeholder={label} aria-label={label} value={text} onChange={e => setText(e.target.value)}
      onBlur={() => text !== value && onChange(text.trim())}
      onKeyDown={e => e.key === 'Enter' && onChange(text.trim())} />
  )
}

function Entry({ entry: e }: { entry: AuditEntry }) {
  const t = useT()
  const OPS = opLabels(t)
  const before = parse(e.oldValues)
  const after = parse(e.newValues)
  const fields = [...new Set([...Object.keys(before), ...Object.keys(after)])]
  const record = e.rowKey && e.operation !== 'Delete'
    ? `/c/${e.connection}/t/${encodeURIComponent(e.schema)}/${encodeURIComponent(e.table)}/edit?${e.rowKey}`
    : null
  return (
    <tr>
      <td className="nowrap">{formatInstant(e.at)}</td>
      <td>{e.user ?? '—'}</td>
      <td><span className={`tag op-${e.operation.toLowerCase()}`}>{OPS[e.operation]}</span></td>
      <td>{e.connection} · {e.schema}.{e.table}</td>
      <td>{record ? <Link to={record}>{decodeKey(e.rowKey!)}</Link> : e.rowKey ? decodeKey(e.rowKey) : '—'}</td>
      <td className="audit-changes">
        {fields.map(f => (
          <div key={f}>
            <strong>{f}</strong>:{' '}
            {e.operation === 'Update' || e.operation === 'Links'
              ? <><span className="old">{show(before[f], e.labels?.[f])}</span> → <span>{show(after[f], e.labels?.[f])}</span></>
              : <span className={e.operation === 'Delete' ? 'old' : undefined}>{show(e.operation === 'Delete' ? before[f] : after[f], e.labels?.[f])}</span>}
          </div>
        ))}
      </td>
    </tr>
  )
}

function parse(json: string | null): Record<string, unknown> {
  if (!json) return {}
  try { return JSON.parse(json) as Record<string, unknown> } catch { return {} }
}

/** Un valor, con su texto si es una FK o un vínculo N:M (P-094): "Bebidas (2)". */
function show(v: unknown, labels?: Record<string, string>): string {
  if (v === null || v === undefined) return '∅'
  const one = (x: unknown) => {
    const text = typeof x === 'object' && x !== null ? JSON.stringify(x) : String(x)
    return labels?.[text] ? `${labels[text]} (${text})` : text
  }
  if (Array.isArray(v)) return '[' + v.map(one).join(', ') + ']'
  return one(v)
}

function decodeKey(key: string): string {
  return [...new URLSearchParams(key)].map(([k, v]) => `${k}=${v}`).join(', ')
}
