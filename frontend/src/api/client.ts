import type {
  ImportMode, ImportResult, TransferFormat, TreeNode,
  AuditPage, SecurityInfo, SecurityProfile, SecurityUser, ActionResult, Routine,
  Connection, ConnectionInput, LookupItem, OverridePreview, PagedResult, Row, SchemaDiff, SchemaSummary, TableMeta, TableOverride, EngineInfo, OverrideCopyRequest, OverrideCopyResult, Suggestion, AccessTokenInfo, OverrideProposal, PlanInfo, FreeTables,
} from './types'
import { t, translateServer } from '../i18n'

/** Error de la API con el formato ProblemDetails de ASP.NET. */
export class ApiError extends Error {
  status: number
  fieldErrors: Record<string, string>
  /** Tipo del problema; "mantenedores:concurrencia" = otra persona cambió el registro (D-069). */
  type?: string
  /** En un conflicto de concurrencia, cómo está el registro ahora. */
  current?: Record<string, unknown>

  constructor(status: number, title: string, detail?: string, errors?: Record<string, string[]>,
    type?: string, current?: Record<string, unknown>) {
    // El servidor responde en español; se muestra en el idioma elegido (P-101).
    super(translateServer(detail || title))
    this.status = status
    this.type = type
    this.current = current
    this.fieldErrors = Object.fromEntries(
      Object.entries(errors ?? {}).map(([k, v]) => [k, v.map(m => translateServer(m)).join(' ')]),
    )
  }
}

// Las rutas de la API son relativas: el navegador las resuelve contra <base href>,
// así funcionan igual en "/" o en una subruta como "/mantenedores/" (P-007).
const TOKEN_KEY = 'mantenedores.token'

export const tokenStore = {
  get(): string | null {
    try { return localStorage.getItem(TOKEN_KEY) } catch { return null }
  },
  set(token: string | null) {
    try {
      if (token) localStorage.setItem(TOKEN_KEY, token)
      else localStorage.removeItem(TOKEN_KEY)
    } catch { /* almacenamiento no disponible: la sesión dura lo que la pestaña */ }
  },
}

let onUnauthorized: () => void = () => {}
export function setUnauthorizedHandler(fn: () => void) { onUnauthorized = fn }

// De dónde sale el token: del login local (localStorage) o del proveedor OIDC (en memoria, lo maneja oidc-client-ts).
let tokenProvider: () => string | null = () => tokenStore.get()
export function setTokenProvider(fn: () => string | null) { tokenProvider = fn }

async function request<T>(method: string, url: string, body?: unknown, extraHeaders?: Record<string, string>): Promise<T> {
  // El idioma va al servidor para cuando traduzca sus mensajes (P-101); hoy responde en español.
  const headers: Record<string, string> = { 'Accept-Language': document.documentElement.lang || 'es', ...extraHeaders }
  const token = tokenProvider()
  if (token) headers.Authorization = `Bearer ${token}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const res = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })

  if (res.status === 401 && !url.endsWith('auth/login')) {
    onUnauthorized()
    throw new ApiError(401, t('Sesión expirada'))
  }
  if (res.status === 204) return undefined as T

  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined
  if (!res.ok) throw new ApiError(res.status, data?.title ?? res.statusText, data?.detail, data?.errors, data?.type, data?.current)
  return data as T
}

/** Descarga un archivo de la API con el token (un enlace directo no lo llevaría). */
async function download(url: string): Promise<void> {
  const token = tokenProvider()
  const headers: Record<string, string> = { 'Accept-Language': document.documentElement.lang || 'es' }
  if (token) headers.Authorization = `Bearer ${token}`
  const res = await fetch(url, { headers })
  if (res.status === 401) { onUnauthorized(); throw new ApiError(401, t('Sesión expirada')) }
  if (!res.ok) {
    const data = await res.json().catch(() => undefined)
    throw new ApiError(res.status, data?.title ?? res.statusText, data?.detail, data?.errors)
  }
  const name = /filename\*=UTF-8''([^;]+)/i.exec(res.headers.get('Content-Disposition') ?? '')?.[1]
  const blob = await res.blob()
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = name ? decodeURIComponent(name) : 'exportacion'
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(a.href), 10_000)
}

/** Sube un archivo para importar. Con errores (400) devuelve igual el detalle, que la pantalla muestra. */
async function upload(url: string, file: File, mapping?: Record<string, string | null>, mode?: ImportMode): Promise<ImportResult> {
  const form = new FormData()
  form.append('file', file)
  if (mapping) form.append('mapping', JSON.stringify(mapping))
  if (mode) form.append('mode', mode)
  const token = tokenProvider()
  const res = await fetch(url, { method: 'POST', body: form, headers: token ? { Authorization: `Bearer ${token}` } : {} })
  if (res.status === 401) { onUnauthorized(); throw new ApiError(401, t('Sesión expirada')) }
  const data = await res.json().catch(() => undefined)
  if (res.ok || (res.status === 400 && data && Array.isArray(data.headers))) return data as ImportResult
  throw new ApiError(res.status, data?.title ?? res.statusText, data?.detail, data?.errors)
}

const enc = encodeURIComponent
const ifMatch = (version: unknown): Record<string, string> | undefined =>
  typeof version === 'string' && version ? { 'If-Match': `"${version}"` } : undefined

/** ¿El error es porque otra persona cambió el registro? (D-069) */
export const isConcurrencyError = (e: unknown): e is ApiError =>
  e instanceof ApiError && e.status === 409 && e.type === 'mantenedores:concurrencia'
/** La tabla cambió en la base desde que se leyó su definición (P-086). */
export const isSchemaChangedError = (e: unknown): e is ApiError =>
  e instanceof ApiError && e.status === 409 && e.type === 'mantenedores:esquema'
const tablePath = (conn: string, schema: string, table: string) =>
  `api/data/${enc(conn)}/${enc(schema)}/${enc(table)}`

const overridePath = (conn: string, schema: string, table: string) =>
  `api/meta/${enc(conn)}/overrides/${enc(schema)}/${enc(table)}`

export interface ListParams {
  page: number
  pageSize: number
  sort?: string
  q?: string
  filters?: Record<string, string>
  /** Igualdad exacta (maestro-detalle): `?e.pedido_id=5`. */
  exact?: Record<string, string>
  /** Paginación por clave (P-098): la PK de la última fila de la página anterior. */
  after?: string
}

/** Permisos de la app (D-053, D-066): se otorgan a roles por configuración o a perfiles. */
export type Permission = 'ConfigureTables' | 'ManageConnections' | 'ManageSecurity' | 'ViewAudit'

/** Configuración pública de autenticación que expone la API (D-024). */
export interface AuthConfig {
  mode: 'Local' | 'Jwt'
  authority: string | null
  clientId: string | null
  scopes: string | null
  providerName: string | null
}

export const api = {
  authConfig: () => request<AuthConfig>('GET', 'api/auth/config'),
  me: () => request<{
    name: string | null
    roles: string[]
    isAdmin: boolean
    permissions: Permission[]
    /** No hay perfiles: todos ven y editan todos los datos (D-062). */
    securityOpen: boolean
    profiles: string[]
  }>('GET', 'api/auth/me'),
  login: (username: string, password: string) =>
    request<{ token: string; expiresAt: string; username: string }>('POST', 'api/auth/login', { username, password }),

  securityInfo: () => request<SecurityInfo>('GET', 'api/security/info'),
  profiles: () => request<SecurityProfile[]>('GET', 'api/security/profiles'),
  saveProfile: (p: SecurityProfile) => request<SecurityProfile>('PUT', `api/security/profiles/${enc(p.id)}`, p),
  deleteProfile: (id: string) => request<void>('DELETE', `api/security/profiles/${enc(id)}`),
  users: () => request<SecurityUser[]>('GET', 'api/security/users'),
  saveUser: (username: string, u: { displayName?: string | null; password?: string; active?: boolean }) =>
    request<SecurityUser>('PUT', `api/security/users/${enc(username)}`, u),
  deleteUser: (username: string) => request<void>('DELETE', `api/security/users/${enc(username)}`),

  /** Filtros: conn, schema, table, user, op, from, to, page, pageSize y la clave como k.<columna>. */
  audit: (params: URLSearchParams) => request<AuditPage>('GET', `api/audit?${params}`),
  /** Exporta lo que muestran los filtros de la auditoría (P-094). */
  exportAudit: (params: URLSearchParams, format: TransferFormat) => {
    const q = new URLSearchParams(params)
    q.delete('page'); q.delete('pageSize'); q.set('format', format)
    return download(`api/audit/export?${q}`)
  },

  connections: () => request<Connection[]>('GET', 'api/connections'),
  engines: () => request<EngineInfo[]>('GET', 'api/engines'),
  /** Tokens de acceso del usuario (P-113). */
  tokens: () => request<AccessTokenInfo[]>('GET', 'api/tokens'),
  createToken: (name: string, days?: number) =>
    request<{ token: AccessTokenInfo; secret: string }>('POST', 'api/tokens', { name, days }),
  deleteToken: (id: string) => request<void>('DELETE', `api/tokens/${enc(id)}`),
  mcpInfo: () => request<{ enabled: boolean; path: string }>('GET', 'api/tokens/mcp'),
  plan: () => request<PlanInfo>('GET', 'api/license'),
  setLicense: (key: string) => request<PlanInfo>('PUT', 'api/license', { key }),
  removeLicense: () => request<PlanInfo>('DELETE', 'api/license'),
  startTrial: () => request<PlanInfo>('POST', 'api/license/trial'),
  freeTables: () => request<FreeTables>('GET', 'api/license/tables'),
  addFreeTables: (tables: string[]) => request<PlanInfo>('POST', 'api/license/tables', { tables }),
  /** Propuestas pendientes de la IA para una conexión (P-113). */
  proposals: (conn: string) => request<OverrideProposal[]>('GET', `api/meta/${enc(conn)}/proposals`),
  resolveProposal: (conn: string, id: string, action: 'accept' | 'discard') =>
    request<void>('POST', `api/meta/${enc(conn)}/proposals/${enc(id)}/${action}`),
  /** Sugerencias de ajustes sin IA para una tabla (P-112). */
  suggestions: (conn: string, schema: string, table: string) =>
    request<Suggestion[]>('GET', `api/meta/${enc(conn)}/suggestions/${enc(schema)}/${enc(table)}`),
  /** Copia los ajustes de otra conexión a <c>to</c> (P-111); sin <c>apply</c> es una vista previa. */
  copyOverrides: (to: string, body: OverrideCopyRequest) =>
    request<OverrideCopyResult>('POST', `api/meta/${enc(to)}/overrides/copy`, body),
  saveConnection: (c: ConnectionInput) => request<Connection>('PUT', `api/connections/${enc(c.id)}`, c),
  deleteConnection: (id: string) => request<void>('DELETE', `api/connections/${enc(id)}`),
  testConnection: (c: ConnectionInput) =>
    request<{
      ok: boolean
      tables: number
      error: string | null
      server: string | null
      supported: boolean | null
      settings: Record<string, string> | null
    }>('POST', 'api/connections/test', c),

  notificationsStatus: () => request<{ enabled: boolean }>('GET', 'api/meta/notifications'),
  tables: (conn: string) => request<SchemaSummary>('GET', `api/meta/${enc(conn)}/tables`),
  refresh: (conn: string) => request<SchemaSummary>('POST', `api/meta/${enc(conn)}/refresh`),
  diff: (conn: string) => request<SchemaDiff>('GET', `api/meta/${enc(conn)}/diff`),
  acceptDiff: (conn: string) => request<SchemaDiff>('POST', `api/meta/${enc(conn)}/diff/accept`),
  table: (conn: string, schema: string, table: string) =>
    request<TableMeta>('GET', `api/meta/${enc(conn)}/tables/${enc(schema)}/${enc(table)}`),

  override: (conn: string, schema: string, table: string) =>
    request<TableOverride>('GET', overridePath(conn, schema, table)),
  saveOverride: (conn: string, schema: string, table: string, value: TableOverride) =>
    request<TableMeta>('PUT', overridePath(conn, schema, table), value),
  resetOverride: (conn: string, schema: string, table: string) =>
    request<void>('DELETE', overridePath(conn, schema, table)),
  previewOverride: (conn: string, schema: string, table: string, value: TableOverride) =>
    request<OverridePreview>('POST', `${overridePath(conn, schema, table)}/preview`, value),

  list(conn: string, schema: string, table: string, p: ListParams) {
    const qs = new URLSearchParams({ page: String(p.page), pageSize: String(p.pageSize) })
    if (p.sort) qs.set('sort', p.sort)
    if (p.q) qs.set('q', p.q)
    for (const [k, v] of Object.entries(p.filters ?? {})) if (v) qs.set(`f.${k}`, v)
    for (const [k, v] of Object.entries(p.exact ?? {})) qs.set(`e.${k}`, v)
    if (p.after) qs.set('after', p.after)
    return request<PagedResult>('GET', `${tablePath(conn, schema, table)}?${qs}`)
  },
  get: (conn: string, schema: string, table: string, key: string) =>
    request<Row>('GET', `${tablePath(conn, schema, table)}/row?${key}`),
  create: (conn: string, schema: string, table: string, body: Row) =>
    request<Row>('POST', tablePath(conn, schema, table), body),
  /** Con <c>version</c>, el servidor no pisa el registro si otra persona lo cambió (409, D-069). */
  update: (conn: string, schema: string, table: string, key: string, body: Row, version?: unknown) =>
    request<Row>('PUT', `${tablePath(conn, schema, table)}/row?${key}`, body, ifMatch(version)),
  remove: (conn: string, schema: string, table: string, key: string, version?: unknown) =>
    request<void>('DELETE', `${tablePath(conn, schema, table)}/row?${key}`, undefined, ifMatch(version)),
  routines: (conn: string) => request<Routine[]>('GET', `api/meta/${enc(conn)}/routines`),
  runAction: (conn: string, schema: string, table: string, key: string, action: string, asked: Record<string, unknown>) =>
    request<ActionResult>('POST', `${tablePath(conn, schema, table)}/row/actions/${enc(action)}?${key}`, asked),
  links: (conn: string, schema: string, table: string, key: string, link: string) =>
    request<LookupItem[]>('GET', `${tablePath(conn, schema, table)}/row/links/${enc(link)}?${key}`),
  setLinks: (conn: string, schema: string, table: string, key: string, link: string, values: unknown[]) =>
    request<LookupItem[]>('PUT', `${tablePath(conn, schema, table)}/row/links/${enc(link)}?${key}`, values),
  exportTable(conn: string, schema: string, table: string, format: TransferFormat, p: Omit<ListParams, 'page' | 'pageSize'>) {
    const qs = new URLSearchParams({ format })
    if (p.sort) qs.set('sort', p.sort)
    if (p.q) qs.set('q', p.q)
    for (const [k, v] of Object.entries(p.filters ?? {})) if (v) qs.set(`f.${k}`, v)
    for (const [k, v] of Object.entries(p.exact ?? {})) qs.set(`e.${k}`, v)
    return download(`${tablePath(conn, schema, table)}/export?${qs}`)
  },
  previewImport: (conn: string, schema: string, table: string, file: File, mapping?: Record<string, string | null>, mode?: ImportMode) =>
    upload(`${tablePath(conn, schema, table)}/import/preview`, file, mapping, mode),
  importFile: (conn: string, schema: string, table: string, file: File, mapping?: Record<string, string | null>, mode?: ImportMode) =>
    upload(`${tablePath(conn, schema, table)}/import`, file, mapping, mode),
  /** El archivo de un registro como URL local (blob:) si es una imagen; null si no lo es (P-090). */
  async imageUrl(conn: string, schema: string, table: string, key: string, column: string): Promise<string | null> {
    const token = tokenProvider()
    const res = await fetch(`${tablePath(conn, schema, table)}/row/file/${enc(column)}?${key}`,
      { headers: token ? { Authorization: `Bearer ${token}` } : {} })
    if (!res.ok || !(res.headers.get('Content-Type') ?? '').startsWith('image/')) return null
    return URL.createObjectURL(await res.blob())
  },
  downloadFile: (conn: string, schema: string, table: string, key: string, column: string) =>
    download(`${tablePath(conn, schema, table)}/row/file/${enc(column)}?${key}&download=1`),
  tree: (conn: string, schema: string, table: string, parent?: unknown) =>
    request<TreeNode[]>('GET', `${tablePath(conn, schema, table)}/tree${parent == null ? '' : `?${new URLSearchParams({ parent: String(parent) })}`}`),
  /** Opciones de un combo de FK. <c>equals</c> acota por columnas de la tabla referenciada (P-110). */
  lookup: (conn: string, schema: string, table: string, q: string, equals?: Record<string, string>) =>
    request<LookupItem[]>('GET', `${tablePath(conn, schema, table)}/lookup?${new URLSearchParams([
      ['q', q], ['take', '20'], ...Object.entries(equals ?? {}).map(([k, v]) => [`e.${k}`, v]),
    ])}`),
}
