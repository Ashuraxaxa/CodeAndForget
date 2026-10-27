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
  /** Hijas que se muestran en pestañas al editar (ajuste `children`); null = todas las de referencedBy. */
  children: ForeignKey[] | null
  /** Relaciones N:M (P-015): se editan con un selector múltiple en el formulario. */
  manyToMany: ManyToManyLink[]
  /** Es la tabla puente de una relación N:M. */
  isBridge: boolean
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
  /** Hijas en pestañas, por nombre de FK y en orden. Sin definir = todas; [] = ninguna. */
  children?: string[]
  columns?: Record<string, ColumnOverride>
}

export interface OverridePreview {
  table: TableMeta
  errors: Record<string, string[]>
}

export const WIDGETS = ['textarea', 'email', 'url', 'color', 'money', 'password'] as const

// ----- Diferencias de esquema (P-013) -----

export type SchemaChangeKind =
  | 'TableAdded' | 'TableRemoved' | 'TableKindChanged'
  | 'ColumnAdded' | 'ColumnRemoved' | 'ColumnTypeChanged' | 'ColumnNullabilityChanged'
  | 'PrimaryKeyChanged' | 'ForeignKeyAdded' | 'ForeignKeyRemoved'

export interface SchemaChange {
  kind: SchemaChangeKind
  schema: string
  table: string
  column: string | null
  description: string
}

export interface SchemaDiff {
  since: string
  acceptedBy: string | null
  changes: SchemaChange[]
  overrideWarnings: string[]
}

// ----- Relaciones N:M (P-015) -----

export interface ManyToManyLink {
  name: string
  bridgeSchema: string
  bridgeTable: string
  localColumn: string
  foreignColumn: string
  targetSchema: string
  targetTable: string
  targetColumn: string
}

// ----- Exportar e importar (P-017) -----

export type TransferFormat = 'csv' | 'xlsx' | 'json' | 'xml'

export interface ImportError {
  /** Línea del archivo (0 = error general, p. ej. una columna obligatoria sin asociar). */
  line: number
  column: string | null
  message: string
}

export interface ImportResult {
  headers: string[]
  /** Encabezado del archivo → columna de la tabla (null = se ignora). */
  mapping: Record<string, string | null>
  targets: { name: string; label: string | null; type: LogicalType; required: boolean }[]
  totalRows: number
  validRows: number
  errors: ImportError[]
  sample: Record<string, string | null>[]
  imported: number
}
