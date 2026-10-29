import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { LookupItem, ManyToManyLink } from '../api/types'
import { useDebounce } from '../lib/useDebounce'
import { useT } from '../i18n'
import { ComboBox } from './Combo'

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
  const t = useT()
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
                <button type="button" className="link" title={t('Quitar')}
                  onClick={() => onChange(value.filter(x => String(x.value) !== String(v.value)))}>×</button>
              )}
            </span>
          ))}
        </div>
      )}
      {!disabled && (
        <ComboBox
          id={id}
          value=""
          placeholder={t('Agregar {table}…', { table: link.targetTable })}
          loading={isFetching}
          options={(open ? options ?? [] : []).map(item => ({ value: item, label: item.label ?? String(item.value), hint: String(item.value), key: String(item.value) }))}
          query={search}
          onQueryChange={q => { setSearch(q); setOpen(true) }}
          onPick={o => { onChange([...value, o.value]); setSearch('') }}
        />
      )}
    </div>
  )
}
