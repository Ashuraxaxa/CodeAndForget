import { currentLocale } from '../i18n'
// Tipos que espejan las respuestas de la API (.NET).

/** Los tres que trae la app y, si se instalaron, los de plugins (P-064): por eso es un texto abierto. */
export type Engine = 'SqlServer' | 'Postgres' | 'MySql' | (string & {})

/** Motor instalado en el servidor (GET /api/engines). */
export interface EngineInfo {
  name: Engine
  label: string
  example?: string | null
}

export type LogicalType =
  | 'String' | 'Text' | 'Integer' | 'Decimal' | 'Boolean' | 'Date' | 'DateTime'
  | 'Time' | 'Uuid' | 'Json' | 'Enum' | 'Binary' | 'Array' | 'ReadOnlyText' | 'Spatial' | 'Unsupported'

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
  /** Nombre visible por idioma (P-102). */
  labels?: Record<string, string> | null
  group: string | null
  hidden: boolean
  /** Operaciones que el usuario puede hacer (D-063): view, create, update, delete, export, import. */
  access: string[]
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
  /** Archivo (binario que no es rowversion): se sube en base64 y se descarga aparte (P-018). */
  isFile: boolean
  /** Tipo de cada elemento de un arreglo (P-018). */
  elementType: LogicalType | null
  // Ajustes (P-011)
  label: string | null
  /** Nombre visible por idioma (P-102). */
  labels?: Record<string, string> | null
  helpText: string | null
  helpTexts?: Record<string, string> | null
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
  /** Columna local que lleva la etiqueta (`<columna>__label`): la única, o la última de una FK compuesta (P-030). */
  labelColumn: string
  /** Filtros del combo (P-110): columna de la tabla referenciada = columna de este registro. */
  lookupFilters?: LookupFilter[]
}

export interface LookupFilter {
  refColumn: string
  localColumn: string
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
  /** Acciones que llaman a un procedimiento (P-061). */
  actions?: TableAction[]
  /** Es la tabla puente de una relación N:M. */
  isBridge: boolean
  /** FK hacia sí misma (p. ej. jefe_id): habilita la vista de árbol (P-016). */
  selfReference: ForeignKey | null
  displayColumn: string | null
  isEditable: boolean
  /** Lo que el usuario puede hacer según sus perfiles (D-063). La UI solo oculta: el servidor rechaza igual. */
  canCreate: boolean
  /** Puede ejecutar las acciones (permiso execute). */
  canExecute?: boolean
  canUpdate: boolean
  canDelete: boolean
  canExport: boolean
  canImport: boolean
  hasPrimaryKey: boolean
  // Ajustes (P-011)
  label: string | null
  /** Nombre visible por idioma (P-102). */
  labels?: Record<string, string> | null
  group: string | null
  hidden: boolean
  forcedReadOnly: boolean
  defaultSort: string | null
  hasVirtualPrimaryKey: boolean
}

/** Nombre visible: el del ajuste o, si no hay, el de la base. */
/** Nombre visible: el del idioma elegido (P-102), el del ajuste o el de la base. */
export const labelOf = (x: { name: string; label: string | null; labels?: Record<string, string> | null }) =>
  x.labels?.[currentLocale()] ?? x.label ?? x.name

/** Ayuda bajo el campo, en el idioma elegido si hay. */
export const helpTextOf = (c: { helpText: string | null; helpTexts?: Record<string, string> | null }) =>
  c.helpTexts?.[currentLocale()] ?? c.helpText

export type Row = Record<string, unknown>

export interface PagedResult {
  rows: Row[]
  total: number
  page: number
  pageSize: number
  /** El total sale de las estadísticas del motor (tabla grande sin filtros, D-072): se muestra con "≈". */
  totalIsEstimate?: boolean
  /** Hay al menos `total` filas: se dejó de contar ahí (tabla grande con filtros, D-072). */
  totalIsCapped?: boolean
}

export interface LookupItem {
  value: unknown
  label: string | null
  /** Nombre visible por idioma (P-102). */
  labels?: Record<string, string> | null
}

export const LABEL_SUFFIX = '__label'
/** Tamaño en bytes de un archivo en las filas (P-018). */
export const SIZE_SUFFIX = '__size'
/** Versión de la fila (concurrencia optimista, D-069): se devuelve en If-Match al editar o borrar. */
export const VERSION_FIELD = '__version'

// ----- Ajustes (overrides, P-011): mismo JSON que guarda la API -----

export interface ColumnOverride {
  label?: string
  helpText?: string
  /** En otros idiomas (P-102), por código: { en: 'Price' }. */
  labels?: Record<string, string>
  helpTexts?: Record<string, string>
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
  labels?: Record<string, string>
  hidden?: boolean
  readOnly?: boolean
  displayColumn?: string
  defaultSort?: string
  group?: string
  virtualPrimaryKey?: string[]
  virtualForeignKeys?: VirtualForeignKey[]
  /** Filtros de los combos de FK (P-110): columnas locales de la FK → (columna referenciada → columna local). */
  lookupFilters?: Record<string, Record<string, string>>
  /** Hijas en pestañas, por nombre de FK y en orden. Sin definir = todas; [] = ninguna. */
  children?: string[]
  columns?: Record<string, ColumnOverride>
  /** Avisar por correo cuando cambia (D-073). Solo se envía si la app tiene SMTP configurado. */
  notify?: NotifyOverride
  /** Autorreferencia que usa el árbol (P-089), por nombre de FK. */
  treeParent?: string
  /** Cómo se edita cada relación N:M (P-087), por su nombre. */
  links?: Record<string, LinkDisplay>
  actions?: TableAction[]
}

// ----- Acciones con procedimientos (P-061) -----

/** De dónde sale un parámetro: una columna del registro, un valor fijo o lo que escriba el usuario. */
export interface ActionParameter { column?: string; value?: string; ask?: boolean }

export interface TableAction {
  name: string
  label?: string | null
  labels?: Record<string, string> | null
  routine: string
  parameters?: Record<string, ActionParameter>
  confirm?: string | null
}

export interface RoutineParameter { name: string; ordinal: number; nativeType: string; type: LogicalType }
export interface Routine { schema: string; name: string; kind: 'Procedure' | 'Function'; parameters: RoutineParameter[]; fullName: string }
export interface ActionResult { rows: Row[]; truncated: boolean }

export type NotifyOperation = 'create' | 'update' | 'delete' | 'import' | 'links'

export interface NotifyOverride {
  emails?: string[]
  /** Vacío = altas, ediciones y bajas. */
  on?: NotifyOperation[]
  includeValues?: boolean
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
  /** Cómo se edita (P-087): selector múltiple, pestaña con el puente o nada. */
  display?: LinkDisplay
}

export type LinkDisplay = 'selector' | 'tab' | 'none'

/** Las relaciones N:M que se editan con el selector múltiple del formulario. */
export const selectorLinks = (meta: { manyToMany: ManyToManyLink[] }) =>
  meta.manyToMany.filter(l => (l.display ?? 'selector') === 'selector')

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
  /** `isKey`: identifica la fila a actualizar (modo upsert); si la genera la base, solo sirve para eso. */
  targets: { name: string; label: string | null; type: LogicalType; required: boolean; isKey: boolean }[]
  totalRows: number
  validRows: number
  errors: ImportError[]
  sample: Record<string, string | null>[]
  imported: number
  /** En la vista previa, lo que se haría; al importar, lo que se hizo. */
  created: number
  updated: number
}

/** insert: todas son altas. upsert: las filas con una clave que ya existe se actualizan (P-088). */
export type ImportMode = 'insert' | 'upsert'

// ----- Árboles (P-016) -----

export interface TreeNode {
  value: unknown
  label: string | null
  /** Nombre visible por idioma (P-102). */
  labels?: Record<string, string> | null
  hasChildren: boolean
}

// ---------- Seguridad (P-040, P-042; D-062, D-063) ----------

/** Operaciones sobre los datos. En las columnas solo cuentan "view" y "update". */
export type DataOperation = 'view' | 'create' | 'update' | 'delete' | 'export' | 'import' | 'execute'

export interface ColumnRule { operations: DataOperation[] }
export interface TableRule { operations: DataOperation[]; columns: Record<string, ColumnRule> }
export interface ConnectionRule { operations: DataOperation[]; tables: Record<string, TableRule> }

export interface SecurityProfile {
  id: string
  name: string
  description: string | null
  roles: string[]
  users: string[]
  appPermissions: string[]
  /** Por id de conexión; "*" = todas. */
  connections: Record<string, ConnectionRule>
}

export interface SecurityUser {
  username: string
  displayName: string | null
  active: boolean
  /** local = editable aquí; config = de Auth:Users (solo lectura). */
  source: 'local' | 'config'
}

export interface SecurityInfo {
  mode: 'Local' | 'Jwt'
  appPermissions: string[]
  operations: DataOperation[]
  adminRoles: string[]
  minPasswordLength: number
}

// ---------- Auditoría (P-044; D-065, D-068) ----------

export type AuditOperation = 'Create' | 'Update' | 'Delete' | 'Import' | 'Links' | 'Action'

export interface AuditEntry {
  id: number
  at: string
  user: string | null
  connection: string
  schema: string
  table: string
  operation: AuditOperation
  /** Clave del registro como "col=valor&col2=valor2"; null si no se conoce. */
  rowKey: string | null
  /** JSON con los valores antes y después (solo lo que cambió en una edición). */
  oldValues: string | null
  newValues: string | null
  /** Texto a mostrar de FK y vínculos N:M (P-094): labels[columna][valor] = texto. */
  labels?: Record<string, Record<string, string>> | null
}

export interface AuditPage {
  entries: AuditEntry[]
  total: number
  page: number
  pageSize: number
}

/** Copiar ajustes entre conexiones (P-111). */
export type OverrideCopyStatus = 'New' | 'Replace' | 'MissingTable' | 'Invalid' | 'Removed'
export interface OverrideCopyItem {
  schema: string
  table: string
  targetSchema: string
  status: OverrideCopyStatus
  errors?: Record<string, string> | null
}
export interface OverrideCopyResult {
  items: OverrideCopyItem[]
  applied: boolean
  copied: number
}
export interface OverrideCopyRequest {
  from: string
  schemaMap?: Record<string, string>
  replaceAll?: boolean
  apply?: boolean
  /** relations, table, columns, actions, notify; sin indicar = todo. */
  parts?: string[]
  /** "esquema.tabla" del origen; sin indicar = todas. */
  tables?: string[]
}
