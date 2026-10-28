import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { useTableLabels } from '../lib/useTableLabels'
import type { ForeignKey } from '../api/types'
import { useDebounce } from '../lib/useDebounce'

interface Props {
  conn: string
  fk: ForeignKey
  value: unknown
  label: string | null
  nullable: boolean
  disabled?: boolean
  id?: string
  onChange: (value: unknown, label: string | null) => void
}

/**
 * Combo con búsqueda para una FK: consulta /lookup de la tabla referenciada
 * y guarda el valor de su PK mostrando la columna descriptiva.
 */
export function ForeignKeySelect({ conn, fk, value, label, nullable, disabled, id, onChange }: Props) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const debounced = useDebounce(search, 250)

  const tableLabel = useTableLabels(conn)
  const { data, isFetching, error } = useQuery({
    queryKey: ['lookup', conn, fk.refSchema, fk.refTable, debounced],
    queryFn: () => api.lookup(conn, fk.refSchema, fk.refTable, debounced),
    enabled: open,
    staleTime: 30_000,
  })

  // Si la tabla referenciada no admite lista (p. ej. PK compuesta), se edita el valor directo.
  if (error) {
    return (
      <input
        id={id}
        value={value == null ? '' : String(value)}
        disabled={disabled}
        onChange={e => onChange(e.target.value, null)}
      />
    )
  }

  const current = value == null || value === '' ? '' : label ?? String(value)

  return (
    <div className="fk-select">
      <input
        id={id}
        value={open ? search : current}
        placeholder={open ? 'Escribe para buscar…' : `Seleccionar ${tableLabel(fk.refSchema, fk.refTable)}…`}
        disabled={disabled}
        onFocus={() => { setSearch(''); setOpen(true) }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        onChange={e => setSearch(e.target.value)}
        autoComplete="off"
      />
      {nullable && current && !disabled && (
        <button type="button" className="fk-clear" title="Quitar" onClick={() => onChange(null, null)}>×</button>
      )}
      {open && (
        <ul className="fk-options">
          {isFetching && !data && <li className="muted">Buscando…</li>}
          {data?.length === 0 && <li className="muted">Sin resultados</li>}
          {data?.map(item => (
            <li key={String(item.value)}>
              <button
                type="button"
                onMouseDown={e => e.preventDefault()}
                onClick={() => { onChange(item.value, item.label); setOpen(false) }}
              >
                {item.label ?? String(item.value)}
                <span className="muted small"> · {String(item.value)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

/**
 * Combo para una FK de varias columnas (P-030): /lookup devuelve la clave completa ({pais, codigo}) y al elegir se
 * llenan todas las columnas locales, cada una con su columna referenciada.
 */
export function CompositeForeignKeySelect({ conn, fk, values, label, nullable, disabled, onChange }: {
  conn: string
  fk: ForeignKey
  values: Record<string, unknown>
  label: string | null
  nullable: boolean
  disabled?: boolean
  onChange: (values: Record<string, string>, label: string | null) => void
}) {
  const [open, setOpen] = useState(false)
  const [search, setSearch] = useState('')
  const debounced = useDebounce(search, 250)
  const tableLabel = useTableLabels(conn)
  const { data, isFetching } = useQuery({
    queryKey: ['lookup', conn, fk.refSchema, fk.refTable, debounced],
    queryFn: () => api.lookup(conn, fk.refSchema, fk.refTable, debounced),
    enabled: open,
    staleTime: 30_000,
  })

  const keyText = fk.columns.map(c => values[c]).filter(v => v != null && v !== '').map(String).join(' · ')
  const current = keyText ? label ?? keyText : ''
  /** {pais: "CL", codigo: "VAL"} de la tabla referenciada → {pais: "CL", sucursal_codigo: "VAL"} de esta. */
  const toLocal = (key: unknown): Record<string, string> => {
    const k = (key ?? {}) as Record<string, unknown>
    const find = (ref: string) => Object.entries(k).find(([n]) => n.toLowerCase() === ref.toLowerCase())?.[1]
    return Object.fromEntries(fk.columns.map((c, i) => [c, String(find(fk.refColumns[i]) ?? '').trim()]))
  }

  return (
    <div className="fk-select">
      <input
        value={open ? search : current}
        placeholder={open ? 'Escribe para buscar…' : `Seleccionar ${tableLabel(fk.refSchema, fk.refTable)}…`}
        disabled={disabled}
        onFocus={() => { setSearch(''); setOpen(true) }}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        onChange={e => setSearch(e.target.value)}
        autoComplete="off"
        title={keyText}
      />
      {nullable && current && !disabled && (
        <button type="button" className="fk-clear" title="Quitar"
          onClick={() => onChange(Object.fromEntries(fk.columns.map(c => [c, ''])), null)}>×</button>
      )}
      {open && (
        <ul className="fk-options">
          {isFetching && !data && <li className="muted">Buscando…</li>}
          {data?.length === 0 && <li className="muted">Sin resultados</li>}
          {data?.map(item => {
            const local = toLocal(item.value)
            const text = Object.values(local).join(' · ')
            return (
              <li key={text}>
                <button type="button" onMouseDown={e => e.preventDefault()}
                  onClick={() => { onChange(local, item.label); setOpen(false) }}>
                  {item.label ?? text}<span className="muted small"> · {text}</span>
                </button>
              </li>
            )
          })}
        </ul>
      )}
      {keyText && !open && <span className="hint">{fk.columns.map(c => `${c}: ${values[c] ?? ''}`).join(' · ')}</span>}
    </div>
  )
}
