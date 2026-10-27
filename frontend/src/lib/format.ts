import type { Column, Row, TableMeta } from '../api/types'

/** Query string con los valores de la PK de una fila: "Id=5" o "PedidoId=1&Linea=2". */
export function keyQuery(meta: TableMeta, row: Row): string {
  const qs = new URLSearchParams()
  for (const k of meta.primaryKey) qs.set(k, String(row[k] ?? ''))
  return qs.toString()
}

/** Texto de una celda en la grilla. */
export function displayValue(col: Column, value: unknown): string {
  if (value === null || value === undefined) return ''
  switch (col.type) {
    case 'Boolean':
      return value ? 'Sí' : 'No'
    case 'Date':
      return String(value).slice(0, 10)
    case 'DateTime':
      return col.hasTimeZone
        ? new Date(String(value)).toLocaleString()
        : String(value).replace('T', ' ').slice(0, 19)
    case 'Json':
      return typeof value === 'string' ? value : JSON.stringify(value)
    case 'Array':
      return Array.isArray(value) ? value.map(v => (v === null ? '∅' : String(v))).join(', ') : String(value)
    default:
      return String(value)
  }
}

/** Tamaño legible de un archivo: "12 KB". */
export function fileSize(bytes: unknown): string {
  const n = Number(bytes)
  if (bytes === null || bytes === undefined || Number.isNaN(n)) return ''
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(n < 10 * 1024 ? 1 : 0)} KB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}

/** Valor del servidor → valor para el control del formulario. */
export function toInputValue(col: Column, value: unknown): string | boolean {
  if (value === null || value === undefined) return col.type === 'Boolean' && !col.isNullable ? false : ''
  switch (col.type) {
    case 'Boolean':
      return col.isNullable ? String(value) : Boolean(value)
    case 'Date':
      return String(value).slice(0, 10)
    case 'DateTime':
      return col.hasTimeZone ? toLocalInput(new Date(String(value))) : String(value).slice(0, 16)
    case 'Time':
      return String(value).slice(0, 8)
    case 'Json':
      return typeof value === 'string' ? value : JSON.stringify(value, null, 2)
    case 'Array':
      // Un valor por línea (P-018).
      return Array.isArray(value) ? value.map(v => (v === null ? '' : String(v))).join('\n') : String(value)
    default:
      return String(value)
  }
}

/** Valor del control → valor que se envía a la API (los textos los convierte el servidor). */
export function fromInputValue(col: Column, value: unknown): unknown {
  if (col.type === 'Boolean') {
    if (typeof value === 'boolean') return value
    return value === '' ? null : value === 'true'
  }
  if (col.type === 'Array') {
    // Un valor por línea; las líneas vacías se ignoran. Sin líneas = arreglo vacío (o nulo si se permite).
    const items = String(value ?? '').split('\n').map(s => s.trim()).filter(s => s.length > 0)
    return items.length === 0 && col.isNullable ? null : items
  }
  if (value === '' || value === undefined) {
    // Un texto vacío en una columna que acepta nulos se guarda como nulo.
    if (col.type === 'String' || col.type === 'Text') return col.isNullable ? null : ''
    return null
  }
  if (col.type === 'DateTime' && col.hasTimeZone) return new Date(String(value)).toISOString()
  return value
}

function toLocalInput(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

/** Parámetros de la URL que no son la clave del registro (P-014): `_return` (a dónde volver) y `_fk.<col>` (valores fijos). */
export const RETURN_PARAM = '_return'
export const FIXED_PREFIX = '_fk.'

/** Separa la query string en clave del registro, valores fijos y a dónde volver. */
export function parseRecordSearch(search: string) {
  const all = new URLSearchParams(search)
  const key = new URLSearchParams()
  const fixed: Record<string, string> = {}
  for (const [k, v] of all) {
    if (k === RETURN_PARAM) continue
    if (k.startsWith(FIXED_PREFIX)) fixed[k.slice(FIXED_PREFIX.length)] = v
    else key.append(k, v)
  }
  return { key: key.toString(), fixed, returnTo: all.get(RETURN_PARAM) }
}
