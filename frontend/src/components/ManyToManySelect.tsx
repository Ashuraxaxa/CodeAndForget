import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { LookupItem, ManyToManyLink } from '../api/types'
import { useDebounce } from '../lib/useDebounce'

/**
 * Selector múltiple de una relación N:M (P-015), p. ej. las etiquetas de un producto:
 * los elegidos como chips y un buscador sobre la tabla del otro lado.
 */
export function ManyToManySelect({ conn, link, value, disabled, id, onChange }: {
  conn: string
  link: ManyToManyLink
  value: LookupItem[]
  disabled?: boolean
  id?: string
  onChange: (value: LookupItem[]) => void
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const debounced = useDebounce(search, 250)
  const { data, isFetching } = useQuery({
    queryKey: ['lookup', conn, link.targetSchema, link.targetTable, debounced],
    queryFn: () => api.lookup(conn, link.targetSchema, link.targetTable, debounced),
    enabled: open,
    staleTime: 30_000,
  })

  const chosen = new Set(value.map(v => String(v.value)))
  const options = data?.filter(o => !chosen.has(String(o.value)))

  return (
    <div className="m2m">
      {value.length > 0 && (
        <div className="m2m-chips">
          {value.map(v => (
            <span key={String(v.value)} className="chip">
              {v.label ?? String(v.value)}
              {!disabled && (
                <button type="button" className="link" title="Quitar"
                  onClick={() => onChange(value.filter(x => String(x.value) !== String(v.value)))}>×</button>
              )}
            </span>
          ))}
        </div>
      )}
      {!disabled && (
        <div className="fk-select">
          <input
            id={id}
            value={search}
            placeholder={`Agregar ${link.targetTable}…`}
            onFocus={() => setOpen(true)}
            onBlur={() => setTimeout(() => setOpen(false), 150)}
            onClick={() => setOpen(true)}
            onChange={e => { setSearch(e.target.value); setOpen(true) }}
            autoComplete="off"
          />
          {open && (
            <ul className="fk-options">
              {isFetching && !data && <li className="muted">Buscando…</li>}
              {options?.length === 0 && <li className="muted">Sin resultados</li>}
              {options?.map(item => (
                <li key={String(item.value)}>
                  <button type="button" onMouseDown={e => e.preventDefault()}
                    onClick={() => { onChange([...value, item]); setSearch(''); setOpen(false) }}>
                    {item.label ?? String(item.value)}
                    <span className="muted small"> · {String(item.value)}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}
