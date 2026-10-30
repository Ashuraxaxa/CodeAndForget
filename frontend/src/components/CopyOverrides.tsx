import { Copy, X } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Connection, OverrideCopyItem, OverrideCopyStatus } from '../api/types'
import { translateServer, useT } from '../i18n'
import { SearchSelect } from './Combo'

const PARTS = ['relations', 'table', 'columns', 'actions', 'notify'] as const

/**
 * Copiar los ajustes de otra conexión a esta (P-111, D-098), p. ej. la misma base en otro servidor. Solo entre
 * conexiones del mismo motor. Primero muestra qué pasaría con cada tabla; nada se guarda hasta confirmar.
 */
export function CopyOverridesPanel({ target, connections, onClose, onDone }: {
  target: Connection
  connections: Connection[]
  onClose: () => void
  onDone: (message: string) => void
}) {
  const t = useT()
  const qc = useQueryClient()
  const sources = connections.filter(c => c.id !== target.id && c.engine === target.engine)
  const [from, setFrom] = useState(sources.length === 1 ? sources[0].id : '')
  const [parts, setParts] = useState<Set<string>>(new Set(PARTS))
  const [replaceAll, setReplaceAll] = useState(false)
  const [schemaMap, setSchemaMap] = useState<Record<string, string>>({})
  const [skip, setSkip] = useState<Set<string>>(new Set())
  const allParts = parts.size === PARTS.length

  const partLabel: Record<string, string> = {
    relations: t('Relaciones (FK y PK virtuales, filtros de combos, pestañas, árbol, N:M)'),
    table: t('Tabla (nombre, grupo, columna a mostrar, orden, oculta, solo lectura)'),
    columns: t('Columnas'),
    actions: t('Acciones'),
    notify: t('Avisos por correo'),
  }
  const statusLabel: Record<OverrideCopyStatus, string> = {
    New: t('Nuevo'), Replace: t('Reemplaza'), MissingTable: t('La tabla no existe en el destino'),
    Invalid: t('No válido en el destino'), Removed: t('Se borra'),
  }

  const body = {
    from, schemaMap: Object.keys(schemaMap).length ? schemaMap : undefined,
    replaceAll: replaceAll && allParts, parts: allParts ? undefined : [...parts],
  }
  const preview = useQuery({
    queryKey: ['override-copy', target.id, body],
    queryFn: () => api.copyOverrides(target.id, { ...body, apply: false }),
    enabled: from !== '' && parts.size > 0,
  })
  useEffect(() => setSkip(new Set()), [from])
  const targetSchemas = useQuery({ queryKey: ['tables', target.id], queryFn: () => api.tables(target.id), enabled: from !== '' })
  const schemaOptions = [...new Set(targetSchemas.data?.tables.map(x => x.schema) ?? [])].sort().map(s => ({ value: s, label: s }))

  const key = (i: OverrideCopyItem) => `${i.schema}.${i.table}`
  const copyable = (i: OverrideCopyItem) => i.status === 'New' || i.status === 'Replace'
  const items = useMemo(() => preview.data?.items ?? [], [preview.data])
  // Esquemas del origen que no están en el destino: se puede indicar a cuál corresponden (en MySQL el esquema es la base).
  const missingSchemas = [...new Set(items.filter(i => i.status === 'MissingTable').map(i => i.schema))]
  const chosen = items.filter(i => copyable(i) && !skip.has(key(i)))
  const removed = items.filter(i => i.status === 'Removed')

  const apply = useMutation({
    mutationFn: () => api.copyOverrides(target.id, {
      ...body, apply: true,
      // Si se desmarcó alguna tabla, solo las marcadas (y entonces no se borra nada).
      tables: skip.size ? chosen.map(key) : undefined,
      replaceAll: body.replaceAll && skip.size === 0,
    }),
    onSuccess: r => {
      qc.invalidateQueries({ queryKey: ['meta', target.id] })
      qc.invalidateQueries({ queryKey: ['tables', target.id] })
      qc.invalidateQueries({ queryKey: ['override-copy'] })
      const gone = r.items.filter(i => i.status === 'Removed').length
      onDone(t('Se copiaron {n} ajustes a {name}.', { n: r.copied, name: target.name }) + (gone ? ' ' + t('{n} ajustes del destino se borraron.', { n: gone }) : ''))
      onClose()
    },
  })

  return (
    <div className="card form copy-overrides">
      <div className="page-header">
        <h2>{t('Copiar ajustes a {name}', { name: target.name })}</h2>
        <button type="button" className="icon-button" title={t('Cerrar')} aria-label={t('Cerrar')} onClick={onClose}><X size={16} aria-hidden /></button>
      </div>
      <p className="muted small">{t('Para la misma base en otro servidor o instancia: se copian los ajustes de cada tabla que exista en las dos. Solo entre conexiones del mismo motor.')}</p>

      {sources.length === 0 ? (
        <div className="alert">{t('No hay otra conexión del mismo motor ({engine}) de donde copiar.', { engine: target.engine })}</div>
      ) : (
        <>
          <div className="grid-2">
            <label>
              {t('Copiar desde')}
              <SearchSelect value={from} onChange={setFrom} placeholder={t('Conexión de origen…')}
                options={sources.map(c => ({ value: c.id, label: c.name, hint: `${c.server ?? ''} / ${c.database ?? ''}` }))} />
            </label>
            <div className="field">
              <span className="field-label">{t('Qué copiar')}</span>
              <div className="checks column">
                {PARTS.map(p => (
                  <label key={p} className="check">
                    <input type="checkbox" checked={parts.has(p)}
                      onChange={e => setParts(s => { const n = new Set(s); if (e.target.checked) n.add(p); else n.delete(p); return n })} />
                    {partLabel[p]}
                  </label>
                ))}
              </div>
              {!allParts && <span className="hint">{t('Lo que no se marca queda como está en el destino.')}</span>}
            </div>
          </div>
          {allParts && (
            <label className="check">
              <input type="checkbox" checked={replaceAll} onChange={e => setReplaceAll(e.target.checked)} />
              {t('Reemplazar todo: borrar también los ajustes del destino en tablas que el origen no tiene')}
            </label>
          )}

          {missingSchemas.length > 0 && (
            <div className="field">
              <span className="field-label">{t('Esquemas con otro nombre en el destino')}</span>
              {missingSchemas.map(s => (
                <div key={s} className="row">
                  <code>{s}</code><span>→</span>
                  <SearchSelect className="narrow" value={schemaMap[s] ?? ''} placeholder={t('(igual)')}
                    onChange={v => setSchemaMap(m => ({ ...m, [s]: v }))} options={schemaOptions} />
                </div>
              ))}
            </div>
          )}

          {preview.error && <div className="alert">{(preview.error as Error).message}</div>}
          {preview.isFetching && <p className="muted with-icon"><span className="spinner sm" />{t('Revisando el destino…')}</p>}
          {preview.data && items.length === 0 && <p className="muted">{t('La conexión de origen no tiene ajustes.')}</p>}
          {items.length > 0 && (
            <div className="table-wrap">
              <table className="grid">
                <thead><tr><th className="no-sort" /><th className="no-sort">{t('Tabla')}</th><th className="no-sort">{t('Qué pasa')}</th></tr></thead>
                <tbody>
                  {items.map(i => (
                    <tr key={key(i) + i.status}>
                      <td className="actions-col">
                        {copyable(i) && (
                          <input type="checkbox" aria-label={key(i)} checked={!skip.has(key(i))}
                            onChange={e => setSkip(s => { const n = new Set(s); if (e.target.checked) n.delete(key(i)); else n.add(key(i)); return n })} />
                        )}
                      </td>
                      <td>{i.schema}.{i.table}{i.targetSchema.toLowerCase() !== i.schema.toLowerCase() && <span className="muted small"> → {i.targetSchema}</span>}</td>
                      <td>
                        <span className={`badge copy-${i.status.toLowerCase()}`}>{statusLabel[i.status]}</span>
                        {i.errors && <span className="muted small"> {Object.values(i.errors).map(m => translateServer(m)).join(' · ')}</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {apply.error && <div className="alert">{(apply.error as Error).message}</div>}
          <div className="actions">
            <button type="button" className="primary" disabled={!from || chosen.length + (skip.size ? 0 : removed.length) === 0 || apply.isPending || preview.isFetching}
              onClick={() => apply.mutate()}>
              <Copy size={16} aria-hidden />
              {apply.isPending ? t('Copiando…') : t('Copiar {n} ajustes', { n: chosen.length })}
            </button>
            <button type="button" onClick={onClose}>{t('Cancelar')}</button>
          </div>
        </>
      )}
    </div>
  )
}
