// Tipos que espejan las respuestas de la API (.NET).

export type Engine = 'SqlServer' | 'Postgres' | 'MySql'

export type LogicalType =
  | 'String' | 'Text' | 'Integer' | 'Decimal' | 'Boolean' | 'Date' | 'DateTime'
  | 'Time' | 'Uuid' | 'Json' | 'Enum' | 'Binary' | 'Unsupported'

export interface Connection {
  id: string
  name: string
  engine: Engine
  schemas: string[]
  server?: string | null
  database?: string | null
}

export interface ConnectionInput {
  id: string
  name: string
  engine: Engine
  connectionString?: string
  schemas: string[]
}

export interface TableSummary {
  schema: string
  name: string
  kind: 'Table' | 'View'
  editable: boolean
  hasPrimaryKey: boolean
  displayColumn: string | null
  columns: number
  foreignKeys: number
  referencedBy: number
}

export interface SchemaSummary {
  hash: string
  readAt: string
  tables: TableSummary[]
}

export interface Column {
  name: string
  nativeType: string
  type: LogicalType
  ordinal: number
  isNullable: boolean
  isPrimaryKey: boolean
  isIdentity: boolean
  isComputed: boolean
  isRowVersion: boolean
  hasDefault: boolean
  hasTimeZone: boolean
  maxLength: number | null
  enumValues: string[] | null
  isSelectable: boolean
  /** El motor puede ordenar por ella (p. ej. no: xml, json). */
  isSortable: boolean
  /** Se ofrece filtrar por ella. */
  isFilterable: boolean
  /** Número de más de 28 dígitos: viaja como texto. */
  isLargeNumber: boolean
  isReadOnly: boolean
  isRequiredOnInsert: boolean
}

export interface ForeignKey {
  name: string
  schema: string
  table: string
  columns: string[]
  refSchema: string
  refTable: string
  refColumns: string[]
}

export interface TableMeta {
  schema: string
  name: string
  kind: 'Table' | 'View'
  columns: Column[]
  primaryKey: string[]
  foreignKeys: ForeignKey[]
  referencedBy: ForeignKey[]
  displayColumn: string | null
  isEditable: boolean
  hasPrimaryKey: boolean
}

export type Row = Record<string, unknown>

export interface PagedResult {
  rows: Row[]
  total: number
  page: number
  pageSize: number
}

export interface LookupItem {
  value: unknown
  label: string | null
}

export const LABEL_SUFFIX = '__label'
