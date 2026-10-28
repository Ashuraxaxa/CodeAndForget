import type {
  ImportResult, TransferFormat, TreeNode,
  Connection, ConnectionInput, LookupItem, OverridePreview, PagedResult, Row, SchemaDiff, SchemaSummary, TableMeta, TableOverride,
} from './types'

/** Error de la API con el formato ProblemDetails de ASP.NET. */
export class ApiError extends Error {
  status: number
  fieldErrors: Record<string, string>

  constructor(status: number, title: string, detail?: string, errors?: Record<string, string[]>) {
    super(detail || title)
    this.status = status
    this.fieldErrors = Object.fromEntries(
      Object.entries(errors ?? {}).map(([k, v]) => [k, v.join(' ')]),
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

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {}
  const token = tokenProvider()
  if (token) headers.Authorization = `Bearer ${token}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const res = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })

  if (res.status === 401 && !url.endsWith('auth/login')) {
    onUnauthorized()
    throw new ApiError(401, 'Sesión expirada')
  }
  if (res.status === 204) return undefined as T

  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined
  if (!res.ok) throw new ApiError(res.status, data?.title ?? res.statusText, data?.detail, data?.errors)
  return data as T
}

/** Descarga un archivo de la API con el token (un enlace directo no lo llevaría). */
async function download(url: string): Promise<void> {
  const token = tokenProvider()
  const res = await fetch(url, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
  if (res.status === 401) { onUnauthorized(); throw new ApiError(401, 'Sesión expirada') }
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
async function upload(url: string, file: File, mapping?: Record<string, string | null>): Promise<ImportResult> {
  const form = new FormData()
  form.append('file', file)
  if (mapping) form.append('mapping', JSON.stringify(mapping))
  const token = tokenProvider()
  const res = await fetch(url, { method: 'POST', body: form, headers: token ? { Authorization: `Bearer ${token}` } : {} })
  if (res.status === 401) { onUnauthorized(); throw new ApiError(401, 'Sesión expirada') }
  const data = await res.json().catch(() => undefined)
  if (res.ok || (res.status === 400 && data && Array.isArray(data.headers))) return data as ImportResult
  throw new ApiError(res.status, data?.title ?? res.statusText, data?.detail, data?.errors)
}

const enc = encodeURIComponent
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
}

/** Permisos de la app (D-053): cada uno se otorga a roles por configuración. */
export type Permission = 'ConfigureTables' | 'ManageConnections'

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
  me: () => request<{ name: string | null; roles: string[]; isAdmin: boolean; permissions: Permission[] }>('GET', 'api/auth/me'),
  login: (username: string, password: string) =>
    request<{ token: string; expiresAt: string; username: string }>('POST', 'api/auth/login', { username, password }),

  connections: () => request<Connection[]>('GET', 'api/connections'),
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
    return request<PagedResult>('GET', `${tablePath(conn, schema, table)}?${qs}`)
  },
  get: (conn: string, schema: string, table: string, key: string) =>
    request<Row>('GET', `${tablePath(conn, schema, table)}/row?${key}`),
  create: (conn: string, schema: string, table: string, body: Row) =>
    request<Row>('POST', tablePath(conn, schema, table), body),
  update: (conn: string, schema: string, table: string, key: string, body: Row) =>
    request<Row>('PUT', `${tablePath(conn, schema, table)}/row?${key}`, body),
  remove: (conn: string, schema: string, table: string, key: string) =>
    request<void>('DELETE', `${tablePath(conn, schema, table)}/row?${key}`),
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
  previewImport: (conn: string, schema: string, table: string, file: File, mapping?: Record<string, string | null>) =>
    upload(`${tablePath(conn, schema, table)}/import/preview`, file, mapping),
  importFile: (conn: string, schema: string, table: string, file: File, mapping?: Record<string, string | null>) =>
    upload(`${tablePath(conn, schema, table)}/import`, file, mapping),
  tree: (conn: string, schema: string, table: string, parent?: unknown) =>
    request<TreeNode[]>('GET', `${tablePath(conn, schema, table)}/tree${parent == null ? '' : `?${new URLSearchParams({ parent: String(parent) })}`}`),
  lookup: (conn: string, schema: string, table: string, q: string) =>
    request<LookupItem[]>('GET', `${tablePath(conn, schema, table)}/lookup?${new URLSearchParams({ q, take: '20' })}`),
}
