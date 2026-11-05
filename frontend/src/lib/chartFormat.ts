import type { DashboardWidget, DateBucket } from '../api/types'
import type { Translate } from '../i18n'

/** Formatos de los dashboards (P-129): números, claves de grupo y la métrica en palabras. */

export const lang = () => document.documentElement.lang || 'es'

export function formatNumber(v: unknown, compact = false): string {
  const n = typeof v === 'number' ? v : Number(v)
  if (v == null || v === '' || Number.isNaN(n)) return v == null ? '—' : String(v)
  return new Intl.NumberFormat(lang(), compact ? { notation: 'compact', maximumFractionDigits: 1 } : { maximumFractionDigits: 2 }).format(n)
}

/** Texto de una clave: el de la FK si lo hay; una fecha según su período; vacío = "(vacío)". */
export function keyLabel(t: Translate, key: unknown, label: string | null | undefined, bucket?: DateBucket | null): string {
  if (label) return label
  if (key == null || key === '') return t('(vacío)')
  if (bucket && typeof key === 'string') {
    const d = new Date(`${key.slice(0, 10)}T00:00:00`)
    if (!Number.isNaN(d.getTime())) {
      const f = (o: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat(lang(), o).format(d)
      switch (bucket) {
        case 'Day': return f({ day: 'numeric', month: 'short', year: 'numeric' })
        case 'Week': return t('Semana del {date}', { date: f({ day: 'numeric', month: 'short' }) })
        case 'Month': return f({ month: 'short', year: 'numeric' })
        case 'Quarter': return t('T{q} {year}', { q: Math.floor(d.getMonth() / 3) + 1, year: d.getFullYear() })
        default: return String(d.getFullYear())
      }
    }
  }
  if (typeof key === 'boolean') return key ? t('Sí') : t('No')
  return String(key)
}

/** Qué calcula la tarjeta, en palabras: "Suma de precio", "Cantidad". */
export function metricLabel(t: Translate, w: DashboardWidget): string {
  const col = w.query.column ?? ''
  switch (w.query.function) {
    case 'CountDistinct': return t('Distintos de {col}', { col })
    case 'Sum': return t('Suma de {col}', { col })
    case 'Avg': return t('Promedio de {col}', { col })
    case 'Min': return t('Mínimo de {col}', { col })
    case 'Max': return t('Máximo de {col}', { col })
    default: return t('Cantidad')
  }
}


/** Etiqueta corta para el eje de un gráfico de líneas: la semana es solo su fecha de inicio. */
export function axisLabel(t: Translate, key: unknown, label: string | null | undefined, bucket?: DateBucket | null): string {
  if (bucket === 'Week' && !label && typeof key === 'string') {
    const d = new Date(`${key.slice(0, 10)}T00:00:00`)
    if (!Number.isNaN(d.getTime())) return new Intl.DateTimeFormat(lang(), { day: 'numeric', month: 'short' }).format(d)
  }
  return keyLabel(t, key, label, bucket)
}
