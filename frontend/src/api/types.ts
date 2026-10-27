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
  label: string | null
  group: string | null
  hidden: boolean
}

export interface SchemaSummary {
  hash: string
  readAt: string
  tables: TableSummary[]
  /** Ajustes que no se pudieron aplicar (p. ej. una columna que ya no existe). */
  warnings: string[]
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
  // Ajustes (P-011)
  label: string | null
  helpText: string | null
  hidden: boolean
  showInGrid: boolean
  order: number
  forcedReadOnly: boolean
  forcedRequired: boolean
  /** textarea, email, url, color, money, password */
  widget: string | null
  pattern: string | null
  min: number | null
  max: number | null
  options: string[] | null
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
  // Ajustes (P-011)
  label: string | null
  group: string | null
  hidden: boolean
  forcedReadOnly: boolean
  defaultSort: string | null
  hasVirtualPrimaryKey: boolean
}

/** Nombre visible: el del ajuste o, si no hay, el de la base. */
export const labelOf = (x: { name: string; label: string | null }) => x.label ?? x.name

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

// ----- Ajustes (overrides, P-011): mismo JSON que guarda la API -----

export interface ColumnOverride {
  label?: string
  helpText?: string
  hidden?: boolean
  showInGrid?: boolean
  order?: number
  readOnly?: boolean
  required?: boolean
  widget?: string
  pattern?: string
  min?: number
  max?: number
  maxLength?: number
  options?: string[]
}

export interface VirtualForeignKey {
  columns: string[]
  refSchema: string
  refTable: string
  refColumns: string[]
  name?: string
}

export interface TableOverride {
  label?: string
  hidden?: boolean
  readOnly?: boolean
  displayColumn?: string
  defaultSort?: string
  group?: string
  virtualPrimaryKey?: string[]
  virtualForeignKeys?: VirtualForeignKey[]
  columns?: Record<string, ColumnOverride>
}

export interface OverridePreview {
  table: TableMeta
  errors: Record<string, string[]>
}

export const WIDGETS = ['textarea', 'email', 'url', 'color', 'money', 'password'] as const
