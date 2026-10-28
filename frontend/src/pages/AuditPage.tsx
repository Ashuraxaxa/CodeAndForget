import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { formatInstant } from '../lib/format'
import { api } from '../api/client'
import type { AuditEntry, AuditOperation } from '../api/types'
import { useMe } from '../lib/useMe'

const OPS: Record<AuditOperation, string> = {
  Create: 'Alta', Update: 'Edición', Delete: 'Baja', Import: 'Importación', Links: 'Vínculos N:M',
}
const PAGE_SIZE = 50

/** Auditoría de cambios (P-044): filtros en la URL, así "Historial" de un registro llega ya filtrado. Requiere ViewAudit (D-068). */
export function AuditPage() {
  const me = useMe()
  const allowed = me.data?.permissions.includes('ViewAudit') === true
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? '1') || 1
  const connections = useQuery({ queryKey: ['connections'], queryFn: api.connections, enabled: allowed })
  const audit = useQuery({
    queryKey: ['audit', params.toString()],
    queryFn: () => {
      const q = new URLSearchParams(params)
      q.set('pageSize', String(PAGE_SIZE))
      // Las fechas del filtro son del día local completo.
      const from = q.get('from'); if (from) q.set('from', new Date(`${from}T00:00:00`).toISOString())
      const to = q.get('to'); if (to) { const d = new Date(`${to}T00:00:00`); d.setDate(d.getDate() + 1); q.set('to', d.toISOString()) }
      return api.audit(q)
    },
    enabled: allowed,
  })

  const set = (name: string, value: string) => {
    const next = new URLSearchParams(params)
    if (value) next.set(name, value); else next.delete(name)
    if (name !== 'page') next.delete('page')
    setParams(next, { replace: true })
  }
  const keyFilter = [...params.entries()].filter(([k]) => k.startsWith('k.'))

  if (me.isLoading) return <p className="muted">Cargando…</p>
  if (!allowed) return <div className="page"><div className="alert">Necesita el permiso para ver la auditoría.</div></div>

  const total = audit.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="page">
      <div className="page-header">
        <h1>Auditoría</h1>
        <span className="muted small">{total} cambios</span>
      </div>

      <div className="card audit-filters">
        <select value={params.get('conn') ?? ''} onChange={e => set('conn', e.target.value)} aria-label="Conexión">
          <option value="">Todas las conexiones</option>
          {connections.data?.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        <FilterText label="Esquema" value={params.get('schema') ?? ''} onChange={v => set('schema', v)} />
        <FilterText label="Tabla" value={params.get('table') ?? ''} onChange={v => set('table', v)} />
        <FilterText label="Usuario" value={params.get('user') ?? ''} onChange={v => set('user', v)} />
        <select value={params.get('op') ?? ''} onChange={e => set('op', e.target.value)} aria-label="Operación">
          <option value="">Todas las operaciones</option>
          {Object.entries(OPS).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
        </select>
        <label className="check">Desde <input type="date" value={params.get('from') ?? ''} onChange={e => set('from', e.target.value)} /></label>
        <label className="check">Hasta <input type="date" value={params.get('to') ?? ''} onChange={e => set('to', e.target.value)} /></label>
        {keyFilter.length > 0 && (
          <span className="chip">
            Registro {keyFilter.map(([k, v]) => `${k.slice(2)}=${v}`).join(', ')}{' '}
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
            <tr><th>Fecha</th><th>Usuario</th><th>Operación</th><th>Tabla</th><th>Registro</th><th>Cambios</th></tr>
          </thead>
          <tbody className={audit.isFetching ? 'loading' : undefined}>
            {audit.data?.entries.map(e => <Entry key={e.id} entry={e} />)}
            {audit.data?.entries.length === 0 && <tr><td colSpan={6} className="empty">Sin cambios registrados</td></tr>}
          </tbody>
        </table>
      </div>

      <div className="pager">
        <button disabled={page <= 1} onClick={() => set('page', String(page - 1))}>‹ Anterior</button>
        <span>Página {page} de {pages}</span>
        <button disabled={page >= pages} onClick={() => set('page', String(page + 1))}>Siguiente ›</button>
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
              ? <><span className="old">{show(before[f])}</span> → <span>{show(after[f])}</span></>
              : <span className={e.operation === 'Delete' ? 'old' : undefined}>{show(e.operation === 'Delete' ? before[f] : after[f])}</span>}
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

function show(v: unknown): string {
  if (v === null || v === undefined) return '∅'
  if (typeof v === 'object') return JSON.stringify(v)
  return String(v)
}

function decodeKey(key: string): string {
  return [...new URLSearchParams(key)].map(([k, v]) => `${k}=${v}`).join(', ')
}
