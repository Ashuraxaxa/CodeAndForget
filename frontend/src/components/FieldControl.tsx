import type { Column, ForeignKey } from '../api/types'
import { ForeignKeySelect } from './ForeignKeySelect'
import { SearchSelect } from './Combo'
import { useT } from '../i18n'

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
  const t = useT()
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

  // Lista fija de valores (ajuste Options).
  if (c.options && c.options.length > 0) {
    return (
      <SearchSelect id={id} value={str} disabled={disabled} required={required} onChange={onChange}
        options={[
          { value: '', label: required ? t('Seleccionar…') : t('(vacío)') },
          // Un valor guardado antes del ajuste se sigue mostrando.
          ...(str && !c.options.includes(str) ? [{ value: str, label: str }] : []),
          ...c.options.map(v => ({ value: v, label: v })),
        ]} />
    )
  }

  if (c.widget === 'textarea' && (c.type === 'String' || c.type === 'Text')) {
    return (
      <textarea id={id} rows={4} value={str} disabled={disabled} required={required}
        maxLength={c.maxLength ?? undefined} onChange={e => onChange(e.target.value)} />
    )
  }

  switch (c.type) {
    case 'Boolean':
      return c.isNullable ? (
        <SearchSelect id={id} value={value == null ? '' : String(value)} disabled={disabled} onChange={onChange}
          options={[{ value: '', label: t('(vacío)') }, { value: 'true', label: t('Sí') }, { value: 'false', label: t('No') }]} />
      ) : (
        <input id={id} type="checkbox" checked={value === true} disabled={disabled} onChange={e => onChange(e.target.checked)} />
      )

    case 'Enum':
      return (
        <SearchSelect id={id} value={str} disabled={disabled} required={required} onChange={onChange}
          options={[
            { value: '', label: c.isNullable || c.hasDefault ? t('(vacío)') : t('Seleccionar…') },
            ...(c.enumValues ?? []).map(v => ({ value: v, label: v })),
          ]} />
      )

    case 'Spatial':
      // Geometría o geografía como WKT (P-090): longitud y latitud, en ese orden.
      return (
        <input id={id} className="mono" value={str} disabled={disabled} required={required}
          placeholder={t('WKT, p. ej. POINT (-70.66 -33.45): longitud latitud')} onChange={e => onChange(e.target.value)} />
      )

    case 'Array':
      return (
        <textarea id={id} rows={4} value={str} disabled={disabled} required={required}
          placeholder={t('Un valor por línea')} onChange={e => onChange(e.target.value)} />
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
      // Los números de más de 28 dígitos van en un campo de texto: el control numérico del navegador los redondea.
      const widgetType: Record<string, string> = { email: 'email', url: 'url', color: 'color', password: 'password' }
      const type =
        c.widget && widgetType[c.widget] && c.type === 'String' ? widgetType[c.widget]
        : c.isLargeNumber ? 'text'
        : c.type === 'Integer' || c.type === 'Decimal' ? 'number'
          : c.type === 'Date' ? 'date'
            : c.type === 'DateTime' ? 'datetime-local'
              : c.type === 'Time' ? 'time'
                : 'text'
      return (
        <input
          id={id}
          type={type}
          step={c.widget === 'money' ? '0.01' : c.type === 'Decimal' ? 'any' : c.type === 'Time' ? 1 : undefined}
          min={c.min ?? undefined}
          max={c.max ?? undefined}
          inputMode={c.isLargeNumber ? 'decimal' : undefined}
          value={str}
          disabled={disabled}
          required={required}
          maxLength={c.maxLength ?? undefined}
          placeholder={c.type === 'Uuid' ? (c.hasDefault ? t('Se genera automáticamente') : '00000000-0000-0000-0000-000000000000') : undefined}
          onChange={e => onChange(e.target.value)}
        />
      )
    }
  }
}
