import { Sparkles, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Suggestion, TableOverride } from '../api/types'
import { useT } from '../i18n'
import { ErrorText } from './ErrorText'

/**
 * Sugerencias de ajustes sin IA (P-112, D-099): relaciones por nombre, filtros de combos, columnas técnicas y correos.
 * Las marcadas pasan a la edición; se guardan con "Guardar", después de verlas en la vista previa.
 */
export function SuggestionsPanel({ conn, schema, table, onApply, onClose }: {
  conn: string
  schema: string
  table: string
  onApply: (patches: TableOverride[]) => void
  onClose: () => void
}) {
  const t = useT()
  const query = useQuery({ queryKey: ['suggestions', conn, schema, table], queryFn: () => api.suggestions(conn, schema, table) })
  const [off, setOff] = useState<Set<string>>(new Set())
  // Columnas técnicas que se dejan a la vista en la grilla: solo se marcan de solo lectura.
  const [visible, setVisible] = useState<Set<string>>(new Set())
  useEffect(() => { setOff(new Set()); setVisible(new Set()) }, [query.data])
  const list = query.data ?? []
  const chosen = list.filter(s => !off.has(s.id))

  /** El ajuste de la sugerencia; si es técnica y se deja visible, sin sacarla de la grilla. */
  const patchOf = (s: Suggestion): TableOverride => {
    if (s.kind !== 'Technical' || !visible.has(s.id)) return s.patch
    const columns = Object.fromEntries(Object.entries(s.patch.columns ?? {}).map(([name, c]) => {
      const rest = { ...c }
      delete rest.showInGrid
      return [name, rest]
    }))
    return { ...s.patch, columns }
  }

  const text = (s: Suggestion) => {
    switch (s.kind) {
      case 'Relation': return t('Relación: {column} → {table} ({key})', { column: s.column, table: `${s.refSchema}.${s.refTable}`, key: s.refColumn ?? '' })
      case 'LookupFilter': return t('Filtrar el combo de {column} por {local} ({table}.{ref})', { column: s.column, local: s.localColumn ?? '', table: s.refTable ?? '', ref: s.refColumn ?? '' })
      case 'Technical': return t('{column}: columna técnica', { column: s.column })
      case 'Email': return t('{column}: validar como correo electrónico', { column: s.column })
    }
  }

  return (
    <div className="card suggestions">
      <div className="page-header">
        <h2 className="with-icon"><Sparkles size={18} aria-hidden />{t('Sugerencias')}</h2>
        <button type="button" className="icon-button" title={t('Cerrar')} aria-label={t('Cerrar')} onClick={onClose}><X size={16} aria-hidden /></button>
      </div>
      <p className="muted small">{t('Detectadas por los nombres y tipos de las columnas, sin mirar datos. Revise antes de guardar.')}</p>
      {query.isLoading && <p className="muted with-icon"><span className="spinner sm" />{t('Buscando sugerencias…')}</p>}
      {query.error && <div className="alert"><ErrorText error={query.error} /></div>}
      {query.data && list.length === 0 && <p className="muted">{t('No hay sugerencias para esta tabla.')}</p>}
      {list.length > 0 && (
        <>
          <div className="checks column">
            {list.map(s => (
              <label key={s.id} className="check">
                <input type="checkbox" checked={!off.has(s.id)}
                  onChange={e => setOff(o => {
                    const n = new Set(o)
                    if (e.target.checked) {
                      n.delete(s.id)
                      if (s.requires) n.delete(s.requires) // un filtro necesita su relación
                    } else {
                      n.add(s.id)
                      for (const d of list) if (d.requires === s.id) n.add(d.id) // sin la relación, su filtro tampoco
                    }
                    return n
                  })} />
                {text(s)}
                {s.kind === 'Technical' && (
                  <select className="inline-select" value={visible.has(s.id) ? 'visible' : 'hidden'} aria-label={t('Cómo queda {column}', { column: s.column })}
                    disabled={off.has(s.id)}
                    onChange={e => setVisible(v => {
                      const n = new Set(v)
                      if (e.target.value === 'visible') n.add(s.id)
                      else n.delete(s.id)
                      return n
                    })}>
                    <option value="hidden">{t('solo lectura y fuera de la grilla')}</option>
                    <option value="visible">{t('solo lectura, visible en la grilla')}</option>
                  </select>
                )}
              </label>
            ))}
          </div>
          <div className="actions">
            <button type="button" className="primary" disabled={chosen.length === 0} onClick={() => { onApply(chosen.map(patchOf)); onClose() }}>
              {t('Aplicar {n} a la edición', { n: chosen.length })}
            </button>
          </div>
        </>
      )}
    </div>
  )
}
