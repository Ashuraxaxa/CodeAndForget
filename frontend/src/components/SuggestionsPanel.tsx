import { Sparkles, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Suggestion, TableOverride } from '../api/types'
import { useT } from '../i18n'

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
  useEffect(() => setOff(new Set()), [query.data])
  const list = query.data ?? []
  const chosen = list.filter(s => !off.has(s.id))

  const text = (s: Suggestion) => {
    switch (s.kind) {
      case 'Relation': return t('Relación: {column} → {table} ({key})', { column: s.column, table: `${s.refSchema}.${s.refTable}`, key: s.refColumn ?? '' })
      case 'LookupFilter': return t('Filtrar el combo de {column} por {local} ({table}.{ref})', { column: s.column, local: s.localColumn ?? '', table: s.refTable ?? '', ref: s.refColumn ?? '' })
      case 'Technical': return t('{column}: columna técnica, solo lectura y fuera de la grilla', { column: s.column })
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
      {query.error && <div className="alert">{(query.error as Error).message}</div>}
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
              </label>
            ))}
          </div>
          <div className="actions">
            <button type="button" className="primary" disabled={chosen.length === 0} onClick={() => { onApply(chosen.map(s => s.patch)); onClose() }}>
              {t('Aplicar {n} a la edición', { n: chosen.length })}
            </button>
          </div>
        </>
      )}
    </div>
  )
}
