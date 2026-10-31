import { Plus, User } from 'lucide-react'
import { SearchSelect, SuggestInput, type ComboOption } from '../components/Combo'
import { useState, type FormEvent } from 'react'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import type { DataOperation, SecurityProfile, SecurityUser } from '../api/types'
import { useMe } from '../lib/useMe'
import { useT, type Translate } from '../i18n'

const opLabels = (t: Translate): Record<DataOperation, string> => ({
  view: t('Ver'), create: t('Crear'), update: t('Editar'), delete: t('Borrar'), export: t('Exportar'), import: t('Importar'),
  execute: t('Ejecutar acciones'),
})
const TABLE_OPS: DataOperation[] = ['view', 'create', 'update', 'delete', 'export', 'import', 'execute']
const COLUMN_OPS: DataOperation[] = ['view', 'update']

const permissionLabels = (t: Translate): Record<string, string> => ({
  ConfigureTables: t('Configurar tablas'),
  ManageConnections: t('Administrar conexiones'),
  ManageSecurity: t('Administrar seguridad'),
  ViewAudit: t('Ver la auditoría'),
})

/** Apartado de seguridad (P-040, P-042): perfiles con permisos por conexión, tabla y columna, y usuarios propios. Requiere ManageSecurity (D-066). */
export function SecurityPage() {
  const me = useMe()
  const t = useT()
  const allowed = me.data?.permissions.includes('ManageSecurity') === true
  const info = useQuery({ queryKey: ['security-info'], queryFn: api.securityInfo, enabled: allowed })
  const [tab, setTab] = useState<'profiles' | 'users'>('profiles')

  if (me.isLoading) return <p className="muted">{t('Cargando…')}</p>
  if (!allowed) return <div className="page"><div className="alert">{t('Necesita el permiso para administrar la seguridad.')}</div></div>

  return (
    <div className="page">
      <div className="page-header"><h1>{t('Seguridad')}</h1></div>
      {me.data?.securityOpen && (
        <div className="banner">
          <strong>{t('Seguridad abierta.')}</strong> {t('No hay perfiles: todos los usuarios ven y editan todos los datos. Al crear el primer perfil, solo los administradores y quienes tengan un perfil verán datos.')}
        </div>
      )}
      <div className="tabs">
        <button className={tab === 'profiles' ? 'tab active' : 'tab'} onClick={() => setTab('profiles')}>{t('Perfiles')}</button>
        <button className={tab === 'users' ? 'tab active' : 'tab'} onClick={() => setTab('users')}>{t('Usuarios')}</button>
      </div>
      {tab === 'profiles' ? <Profiles adminRoles={info.data?.adminRoles ?? []} permissions={info.data?.appPermissions ?? []} />
        : <Users local={info.data?.mode === 'Local'} minPassword={info.data?.minPasswordLength ?? 8} />}
    </div>
  )
}

// ---------- Perfiles ----------

function Profiles({ adminRoles, permissions }: { adminRoles: string[]; permissions: string[] }) {
  const qc = useQueryClient()
  const profiles = useQuery({ queryKey: ['profiles'], queryFn: api.profiles })
  const t = useT()
  const OP_LABELS = opLabels(t)
  const PERMISSION_LABELS = permissionLabels(t)
  const [editing, setEditing] = useState<SecurityProfile | 'new' | null>(null)
  // Copiar (P-093): el formulario parte de otro perfil, pero es uno nuevo.
  const [copying, setCopying] = useState(false)
  const remove = useMutation({
    mutationFn: api.deleteProfile,
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['profiles'] }); qc.invalidateQueries({ queryKey: ['me'] }) },
  })

  if (editing) return <ProfileForm initial={editing === 'new' ? null : editing} copy={copying} permissions={permissions}
    onClose={() => { setEditing(null); setCopying(false) }} />

  return (
    <>
      <div className="page-header">
        <p className="muted small">
          {t('Un perfil se aplica a los roles y usuarios que indique; si alguien tiene varios, se suman.')}
          {adminRoles.length > 0 && <> {t('Los roles {roles} siempre tienen acceso total.', { roles: adminRoles.join(', ') })}</>}
        </p>
        <button className="primary" onClick={() => setEditing('new')}><Plus size={16} aria-hidden />{t('Nuevo perfil')}</button>
      </div>
      {profiles.isLoading && <p className="muted">{t('Cargando…')}</p>}
      {profiles.error && <div className="alert">{(profiles.error as Error).message}</div>}
      {remove.error && <div className="alert">{(remove.error as Error).message}</div>}
      {profiles.data?.length === 0 && <div className="empty">{t('Aún no hay perfiles.')}</div>}
      <div className="cards">
        {profiles.data?.map(p => (
          <div key={p.id} className="card conn-card">
            <div>
              <div className="conn-title">{p.name} <span className="muted small">{p.id}</span></div>
              {p.description && <div className="muted small">{p.description}</div>}
              <div className="small">
                {p.roles.map(r => <span key={r} className="chip">{t('rol {role}', { role: r })}</span>)}
                {p.users.map(u => <span key={u} className="chip"><User size={12} aria-hidden /> {u}</span>)}
                {p.roles.length === 0 && p.users.length === 0 && <span className="muted">{t('Sin roles ni usuarios: no se aplica a nadie.')}</span>}
              </div>
              <div className="muted small">
                {Object.entries(p.connections).map(([c, r]) =>
                  `${c === '*' ? t('Todas') : c}: ${r.operations.map(o => OP_LABELS[o]).join(', ') || '—'}${Object.keys(r.tables).length ? ` (${t('+{n} tablas', { n: Object.keys(r.tables).length })})` : ''}`,
                ).join(' · ') || t('Sin permisos sobre datos')}
                {p.appPermissions.length > 0 && ` · ${p.appPermissions.map(x => PERMISSION_LABELS[x] ?? x).join(', ')}`}
              </div>
            </div>
            <div className="actions">
              <button onClick={() => setEditing(p)}>{t('Editar')}</button>
              <button onClick={() => { setCopying(true); setEditing(p) }}>{t('Copiar')}</button>
              <button className="danger" onClick={() => confirm(t('¿Eliminar el perfil "{name}"?', { name: p.name })) && remove.mutate(p.id)}>{t('Eliminar')}</button>
            </div>
          </div>
        ))}
      </div>
    </>
  )
}

/** Estado editable: listas en vez de diccionarios, para conservar el orden mientras se edita. */
interface ColumnDraft { name: string; operations: DataOperation[] }
interface TableDraft { key: string; operations: DataOperation[]; columns: ColumnDraft[] }
interface ConnectionDraft { conn: string; operations: DataOperation[]; tables: TableDraft[] }

const toDraft = (p: SecurityProfile): ConnectionDraft[] =>
  Object.entries(p.connections).map(([conn, r]) => ({
    conn,
    operations: r.operations,
    tables: Object.entries(r.tables).map(([key, t]) => ({
      key, operations: t.operations,
      columns: Object.entries(t.columns).map(([name, c]) => ({ name, operations: c.operations })),
    })),
  }))

const fromDraft = (rules: ConnectionDraft[]): SecurityProfile['connections'] =>
  Object.fromEntries(rules.filter(r => r.conn.trim()).map(r => [r.conn.trim(), {
    operations: r.operations,
    tables: Object.fromEntries(r.tables.filter(t => t.key.trim()).map(t => [t.key.trim(), {
      operations: t.operations,
      columns: Object.fromEntries(t.columns.filter(c => c.name.trim()).map(c => [c.name.trim(), { operations: c.operations }])),
    }])),
  }]))

const splitList = (s: string) => s.split(',').map(x => x.trim()).filter(Boolean)

function ProfileForm({ initial, copy = false, permissions, onClose }: {
  initial: SecurityProfile | null; copy?: boolean; permissions: string[]; onClose: () => void
}) {
  const qc = useQueryClient()
  const connections = useQuery({ queryKey: ['connections'], queryFn: api.connections })
  const t = useT()
  const PERMISSION_LABELS = permissionLabels(t)
  const isNew = initial === null || copy
  const [id, setId] = useState(initial ? (copy ? `${initial.id}-copia` : initial.id) : '')
  const [name, setName] = useState(initial ? (copy ? t('Copia de {name}', { name: initial.name }) : initial.name) : '')
  const [description, setDescription] = useState(initial?.description ?? '')
  const [roles, setRoles] = useState((initial?.roles ?? []).join(', '))
  const [users, setUsers] = useState((initial?.users ?? []).join(', '))
  const [appPermissions, setAppPermissions] = useState(initial?.appPermissions ?? [])
  const [rules, setRules] = useState<ConnectionDraft[]>(initial ? toDraft(initial) : [])

  const save = useMutation({
    mutationFn: () => api.saveProfile({
      id: id.trim().toLowerCase(), name, description: description || null,
      roles: splitList(roles), users: splitList(users), appPermissions, connections: fromDraft(rules),
    }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['profiles'] })
      // Cambia lo que ve cualquiera, incluido quien edita.
      qc.invalidateQueries({ queryKey: ['me'] })
      qc.invalidateQueries({ queryKey: ['connections'] })
      qc.invalidateQueries({ queryKey: ['tables'] })
      qc.invalidateQueries({ queryKey: ['meta'] })
      onClose()
    },
  })
  const errors = save.error instanceof ApiError ? save.error.fieldErrors : {}
  const generalError = save.error && Object.keys(errors).length === 0 ? (save.error as Error).message : null
  const setRule = (i: number, r: Partial<ConnectionDraft>) => setRules(s => s.map((x, j) => (j === i ? { ...x, ...r } : x)))
  const used = new Set(rules.map(r => r.conn))

  function submit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>{isNew ? t('Nuevo perfil') : t('Perfil · {name}', { name: initial!.name })}</h2>
      <div className="grid-2">
        <label>
          <span className="field-label">{t('Nombre')}</span>
          <input value={name} onChange={e => setName(e.target.value)} required />
          {errors.name && <span className="field-error">{errors.name}</span>}
        </label>
        <label>
          <span className="field-label">Id</span>
          <input value={id} onChange={e => setId(e.target.value)} disabled={!isNew} required placeholder="ventas-lectura" />
          {errors.id && <span className="field-error">{errors.id}</span>}
        </label>
        <label className="span-2">
          <span className="field-label">{t('Descripción')}</span>
          <input value={description} onChange={e => setDescription(e.target.value)} />
        </label>
        <label>
          <span className="field-label">{t('Roles')}</span>
          <input value={roles} onChange={e => setRoles(e.target.value)} placeholder="ventas, contabilidad" />
          <span className="hint">{t('Roles del proveedor de identidad o de Auth:Users, separados por coma.')}</span>
        </label>
        <label>
          <span className="field-label">{t('Usuarios')}</span>
          <input value={users} onChange={e => setUsers(e.target.value)} placeholder="ana, luis@empresa.cl" />
          <span className="hint">{t('Nombres de usuario, propios o del proveedor, separados por coma.')}</span>
        </label>
      </div>

      <div>
        <span className="field-label">{t('Permisos de la app')}</span>
        <div className="checks">
          {permissions.map(p => (
            <label key={p} className="check">
              <input type="checkbox" checked={appPermissions.includes(p)}
                onChange={e => setAppPermissions(s => (e.target.checked ? [...s, p] : s.filter(x => x !== p)))} />
              {PERMISSION_LABELS[p] ?? p}
            </label>
          ))}
        </div>
        {errors.appPermissions && <span className="field-error">{errors.appPermissions}</span>}
      </div>

      <div>
        <span className="field-label">{t('Datos')}</span>
        <p className="hint">
          {t('Lo que se marca en una conexión vale para todas sus tablas. Si agrega una tabla, sus operaciones reemplazan a las de la conexión; y lo mismo una columna respecto de su tabla (una columna sin "Ver" queda oculta).')}
        </p>
        {rules.map((r, i) => (
          <ConnectionRuleEditor key={i} rule={r} connections={(connections.data ?? []).map(c => ({ id: c.id, name: c.name }))}
            used={used} onChange={v => setRule(i, v)} onRemove={() => setRules(s => s.filter((_, j) => j !== i))} />
        ))}
        {errors.connections && <span className="field-error">{errors.connections}</span>}
        <button type="button" onClick={() => setRules(s => [...s, { conn: used.has('*') ? '' : '*', operations: ['view'], tables: [] }])}>
          <Plus size={14} aria-hidden />{t('Conexión')}
        </button>
      </div>

      {generalError && <div className="alert">{generalError}</div>}
      <div className="actions">
        <button className="primary" disabled={save.isPending}>{save.isPending ? t('Guardando…') : t('Guardar')}</button>
        <button type="button" onClick={onClose}>{t('Cancelar')}</button>
      </div>
    </form>
  )
}

function OpChecks({ ops, value, onChange }: { ops: DataOperation[]; value: DataOperation[]; onChange: (v: DataOperation[]) => void }) {
  const OP_LABELS = opLabels(useT())
  return (
    <div className="checks">
      {ops.map(o => (
        <label key={o} className="check">
          <input type="checkbox" checked={value.includes(o)}
            onChange={e => onChange(e.target.checked ? [...value, o] : value.filter(x => x !== o))} />
          {OP_LABELS[o]}
        </label>
      ))}
    </div>
  )
}

function ConnectionRuleEditor({ rule, connections, used, onChange, onRemove }: {
  rule: ConnectionDraft
  connections: { id: string; name: string }[]
  used: Set<string>
  onChange: (r: Partial<ConnectionDraft>) => void
  onRemove: () => void
}) {
  // Se sugieren las tablas de la conexión; con "*", las de todas (P-093). Una que no responde no aporta sugerencias.
  const sources = rule.conn === '*' ? connections.map(c => c.id) : rule.conn ? [rule.conn] : []
  const lists = useQueries({
    queries: sources.map(id => ({ queryKey: ['tables', id], queryFn: () => api.tables(id), retry: false })),
  })
  // De qué conexión se leen las columnas de cada tabla (con "*", la primera que la tiene).
  const owner = new Map<string, string>()
  lists.forEach((l, i) => l.data?.tables.forEach(x => {
    const key = `${x.schema}.${x.name}`
    if (!owner.has(key)) owner.set(key, sources[i])
  }))
  const t = useT()
  const tableKeys = [...owner.keys()].sort().map(key => ({ value: key, label: key }))
  const setTable = (i: number, v: Partial<TableDraft>) => onChange({ tables: rule.tables.map((x, j) => (j === i ? { ...x, ...v } : x)) })

  return (
    <div className="card rule">
      <div className="rule-head">
        <SearchSelect value={rule.conn} onChange={v => onChange({ conn: v })} placeholder={`— ${t('conexión')} —`} ariaLabel={t('conexión')}
          options={[
            { value: '*', label: t('Todas las conexiones'), disabled: used.has('*') && rule.conn !== '*' },
            ...connections.map(c => ({ value: c.id, label: c.name, hint: c.id, disabled: used.has(c.id) && rule.conn !== c.id })),
            ...(rule.conn && rule.conn !== '*' && !connections.some(c => c.id === rule.conn) ? [{ value: rule.conn, label: rule.conn }] : []),
          ]} />
        <OpChecks ops={TABLE_OPS} value={rule.operations} onChange={operations => onChange({ operations })} />
        <button type="button" className="link danger" onClick={onRemove}>{t('Quitar')}</button>
      </div>
      {rule.tables.map((x, i) => (
        <TableRuleEditor key={i} conn={owner.get(x.key.trim()) ?? rule.conn} tableKeys={tableKeys} rule={x} onChange={v => setTable(i, v)}
          onRemove={() => onChange({ tables: rule.tables.filter((_, j) => j !== i) })} />
      ))}
      <button type="button" className="link" onClick={() => onChange({ tables: [...rule.tables, { key: '', operations: ['view'], columns: [] }] })}>
        <Plus size={14} aria-hidden />{t('Tabla con permisos propios')}
      </button>
    </div>
  )
}

function TableRuleEditor({ conn, tableKeys, rule, onChange, onRemove }: {
  conn: string
  tableKeys: ComboOption[]
  rule: TableDraft
  onChange: (t: Partial<TableDraft>) => void
  onRemove: () => void
}) {
  const [schema, table] = splitKey(rule.key)
  const meta = useQuery({
    queryKey: ['meta', conn, schema, table],
    queryFn: () => api.table(conn, schema, table),
    enabled: conn !== '' && conn !== '*' && schema !== '' && table !== '',
    retry: false,
  })
  const t = useT()
  const setColumn = (i: number, c: Partial<ColumnDraft>) => onChange({ columns: rule.columns.map((x, j) => (j === i ? { ...x, ...c } : x)) })

  return (
    <div className="rule-table">
      <div className="rule-head">
        <SuggestInput value={rule.key} placeholder={t('esquema.tabla')} ariaLabel={t('esquema.tabla')} suggestions={tableKeys} onChange={key => onChange({ key })} />
        <OpChecks ops={TABLE_OPS} value={rule.operations} onChange={operations => onChange({ operations })} />
        <button type="button" className="link danger" onClick={onRemove}>{t('Quitar')}</button>
      </div>
      {rule.columns.map((c, i) => (
        <div key={i} className="rule-head rule-column">
          <SuggestInput value={c.name} placeholder={t('columna')} ariaLabel={t('columna')} onChange={name => setColumn(i, { name })}
            suggestions={(meta.data?.columns ?? []).map(col => ({ value: col.name, label: col.name, hint: col.nativeType }))} />
          <OpChecks ops={COLUMN_OPS} value={c.operations} onChange={operations => setColumn(i, { operations })} />
          <button type="button" className="link danger" onClick={() => onChange({ columns: rule.columns.filter((_, j) => j !== i) })}>{t('Quitar')}</button>
        </div>
      ))}
      <button type="button" className="link small" onClick={() => onChange({ columns: [...rule.columns, { name: '', operations: ['view'] }] })}>
        <Plus size={14} aria-hidden />{t('Columna')}
      </button>
    </div>
  )
}

function splitKey(key: string): [string, string] {
  const i = key.indexOf('.')
  return i > 0 ? [key.slice(0, i), key.slice(i + 1)] : ['', '']
}

// ---------- Usuarios propios ----------

function Users({ local, minPassword }: { local: boolean; minPassword: number }) {
  const qc = useQueryClient()
  const users = useQuery({ queryKey: ['security-users'], queryFn: api.users })
  const t = useT()
  const [editing, setEditing] = useState<SecurityUser | 'new' | null>(null)
  const done = () => qc.invalidateQueries({ queryKey: ['security-users'] })
  const remove = useMutation({ mutationFn: api.deleteUser, onSuccess: done })
  const toggle = useMutation({ mutationFn: (u: SecurityUser) => api.saveUser(u.username, { active: !u.active }), onSuccess: done })

  return (
    <>
      <div className="page-header">
        <p className="muted small">
          {local
            ? t('Usuarios propios de la app, con contraseña guardada en hash. Los de la configuración (Auth:Users) se editan ahí.')
            : t('En modo Jwt inician sesión los usuarios del proveedor de identidad: asócielos a un perfil por nombre o por rol.')}
        </p>
        {local && <button className="primary" onClick={() => setEditing('new')}><Plus size={16} aria-hidden />{t('Nuevo usuario')}</button>}
      </div>
      {editing && <UserForm initial={editing === 'new' ? null : editing} minPassword={minPassword} onClose={() => { setEditing(null); done() }} />}
      {users.error && <div className="alert">{(users.error as Error).message}</div>}
      {(remove.error ?? toggle.error) && <div className="alert">{((remove.error ?? toggle.error) as Error).message}</div>}
      <div className="table-wrap">
        <table className="grid">
          <thead><tr><th>{t('Usuario')}</th><th>{t('Nombre')}</th><th>{t('Origen')}</th><th>{t('Estado')}</th><th className="actions-col" /></tr></thead>
          <tbody>
            {users.data?.map(u => (
              <tr key={`${u.source}-${u.username}`}>
                <td>{u.username}</td>
                <td>{u.displayName}</td>
                <td>{u.source === 'config' ? t('configuración') : t('propio')}</td>
                <td>{u.active ? t('activo') : <span className="tag">{t('desactivado')}</span>}</td>
                <td className="actions-col">
                  {u.source === 'local' && (
                    <>
                      <button className="link" onClick={() => setEditing(u)}>{t('Editar')}</button>
                      <button className="link" onClick={() => toggle.mutate(u)}>{u.active ? t('Desactivar') : t('Activar')}</button>
                      <button className="link danger" onClick={() => confirm(t('¿Eliminar al usuario "{name}"?', { name: u.username })) && remove.mutate(u.username)}>{t('Eliminar')}</button>
                    </>
                  )}
                </td>
              </tr>
            ))}
            {users.data?.length === 0 && <tr><td colSpan={5} className="empty">{t('Sin usuarios')}</td></tr>}
          </tbody>
        </table>
      </div>
    </>
  )
}

function UserForm({ initial, minPassword, onClose }: { initial: SecurityUser | null; minPassword: number; onClose: () => void }) {
  const t = useT()
  const [username, setUsername] = useState(initial?.username ?? '')
  const [displayName, setDisplayName] = useState(initial?.displayName ?? '')
  const [password, setPassword] = useState('')
  const save = useMutation({
    mutationFn: () => api.saveUser(username.trim().toLowerCase(), { displayName, password: password || undefined }),
    onSuccess: onClose,
  })
  const errors = save.error instanceof ApiError ? save.error.fieldErrors : {}
  const generalError = save.error && Object.keys(errors).length === 0 ? (save.error as Error).message : null

  return (
    <form className="card form" onSubmit={e => { e.preventDefault(); save.mutate() }}>
      <div className="grid-2">
        <label>
          <span className="field-label">{t('Usuario')}</span>
          <input value={username} onChange={e => setUsername(e.target.value)} disabled={initial !== null} required autoComplete="off" />
          {errors.username && <span className="field-error">{errors.username}</span>}
        </label>
        <label>
          <span className="field-label">{t('Nombre')}</span>
          <input value={displayName} onChange={e => setDisplayName(e.target.value)} />
        </label>
        <label>
          <span className="field-label">{initial ? t('Nueva contraseña') : t('Contraseña')}</span>
          <input type="password" value={password} onChange={e => setPassword(e.target.value)} required={!initial}
            minLength={minPassword} autoComplete="new-password" placeholder={initial ? t('Sin cambios') : undefined} />
          {errors.password && <span className="field-error">{errors.password}</span>}
        </label>
      </div>
      {generalError && <div className="alert">{generalError}</div>}
      <div className="actions">
        <button className="primary" disabled={save.isPending}>{save.isPending ? t('Guardando…') : t('Guardar')}</button>
        <button type="button" onClick={onClose}>{t('Cancelar')}</button>
      </div>
    </form>
  )
}
