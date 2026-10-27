import type { Column, ForeignKey } from '../api/types'
import { ForeignKeySelect } from './ForeignKeySelect'

interface Props {
  conn: string
  column: Column
  fk?: ForeignKey
  value: string | boolean
  fkLabel?: string | null
  disabled: boolean
  required: boolean
  onChange: (value: string | boolean, label?: string | null) => void
}

/** Dibuja el control adecuado según el tipo lógico de la columna. */
export function FieldControl({ conn, column: c, fk, value, fkLabel, disabled, required, onChange }: Props) {
  const id = `f-${c.name}`
  const str = typeof value === 'string' ? value : ''

  if (fk) {
    return (
      <ForeignKeySelect
        id={id}
        conn={conn}
        fk={fk}
        value={str}
        label={fkLabel ?? null}
        nullable={c.isNullable}
        disabled={disabled}
        onChange={(v, l) => onChange(v == null ? '' : String(v), l)}
      />
    )
  }

  switch (c.type) {
    case 'Boolean':
      return c.isNullable ? (
        <select id={id} value={String(value)} disabled={disabled} onChange={e => onChange(e.target.value)}>
          <option value="">(vacío)</option>
          <option value="true">Sí</option>
          <option value="false">No</option>
        </select>
      ) : (
        <input id={id} type="checkbox" checked={value === true} disabled={disabled} onChange={e => onChange(e.target.checked)} />
      )

    case 'Enum':
      return (
        <select id={id} value={str} disabled={disabled} required={required} onChange={e => onChange(e.target.value)}>
          <option value="">{c.isNullable || c.hasDefault ? '(vacío)' : 'Seleccionar…'}</option>
          {c.enumValues?.map(v => <option key={v} value={v}>{v}</option>)}
        </select>
      )

    case 'Text':
    case 'Json':
      return (
        <textarea
          id={id}
          rows={c.type === 'Json' ? 5 : 3}
          className={c.type === 'Json' ? 'mono' : undefined}
          value={str}
          disabled={disabled}
          required={required}
          maxLength={c.maxLength ?? undefined}
          onChange={e => onChange(e.target.value)}
        />
      )

    default: {
      const type =
        c.type === 'Integer' || c.type === 'Decimal' ? 'number'
          : c.type === 'Date' ? 'date'
            : c.type === 'DateTime' ? 'datetime-local'
              : c.type === 'Time' ? 'time'
                : 'text'
      return (
        <input
          id={id}
          type={type}
          step={c.type === 'Decimal' ? 'any' : c.type === 'Time' ? 1 : undefined}
          value={str}
          disabled={disabled}
          required={required}
          maxLength={c.maxLength ?? undefined}
          placeholder={c.type === 'Uuid' ? (c.hasDefault ? 'Se genera automáticamente' : '00000000-0000-0000-0000-000000000000') : undefined}
          onChange={e => onChange(e.target.value)}
        />
      )
    }
  }
}
