import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import type { Connection, ConnectionInput, Engine } from '../api/types'

const EXAMPLES: Record<Engine, string> = {
  SqlServer: 'Server=mi-servidor,1433;Database=MiBase;User Id=usuario;Password=***;TrustServerCertificate=True',
  Postgres: 'Host=mi-servidor;Port=5432;Database=mibase;Username=usuario;Password=***',
}

export function ConnectionsPage() {
  const qc = useQueryClient()
  const { data, isLoading, error } = useQuery({ queryKey: ['connections'], queryFn: api.connections })
  const [editing, setEditing] = useState<Connection | 'new' | null>(null)

  const remove = useMutation({
    mutationFn: api.deleteConnection,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['connections'] }),
  })

  return (
    <div className="page">
      <div className="page-header">
        <h1>Conexiones</h1>
        <button className="primary" onClick={() => setEditing('new')}>Nueva conexión</button>
      </div>

      {editing && (
        <ConnectionForm
          initial={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}

      {isLoading && <p className="muted">Cargando…</p>}
      {error && <div className="alert">{(error as Error).message}</div>}
      {data?.length === 0 && !editing && (
        <div className="empty">Aún no hay conexiones. Crea una para descubrir sus tablas.</div>
      )}

      <div className="cards">
        {data?.map(c => (
          <div key={c.id} className="card conn-card">
            <div>
              <div className="conn-title">{c.name}</div>
              <div className="muted small">
                <span className={`badge ${c.engine === 'SqlServer' ? 'badge-mssql' : 'badge-pg'}`}>
                  {c.engine === 'SqlServer' ? 'SQL Server' : 'PostgreSQL'}
                </span>{' '}
                {c.server} / {c.database}
              </div>
              {c.schemas.length > 0 && <div className="muted small">Esquemas: {c.schemas.join(', ')}</div>}
            </div>
            <div className="actions">
              <Link className="button primary" to={`/c/${c.id}`}>Abrir</Link>
              <button onClick={() => setEditing(c)}>Editar</button>
              <button
                className="danger"
                onClick={() => confirm(`¿Eliminar la conexión "${c.name}"? No borra datos de la base.`) && remove.mutate(c.id)}
              >
                Eliminar
              </button>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function ConnectionForm({ initial, onClose }: { initial: Connection | null; onClose: () => void }) {
  const qc = useQueryClient()
  const [form, setForm] = useState({
    id: initial?.id ?? '',
    name: initial?.name ?? '',
    engine: (initial?.engine ?? 'SqlServer') as Engine,
    connectionString: '',
    schemas: initial?.schemas.join(', ') ?? '',
  })
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})

  const input = (): ConnectionInput => ({
    id: form.id.trim().toLowerCase(),
    name: form.name,
    engine: form.engine,
    connectionString: form.connectionString || undefined,
    schemas: form.schemas.split(',').map(s => s.trim()).filter(Boolean),
  })

  const handleError = (err: unknown) => {
    setFieldErrors(err instanceof ApiError ? err.fieldErrors : {})
    setResult({ ok: false, text: (err as Error).message })
  }

  const test = useMutation({
    mutationFn: () => api.testConnection(input()),
    onSuccess: r => {
      setFieldErrors({})
      const server = r.server ? ` Servidor: ${r.server}.` : ''
      setResult(r.ok
        ? { ok: true, text: `Conexión correcta: ${r.tables} tablas/vistas encontradas.${server}` }
        : { ok: false, text: r.error ?? 'No se pudo conectar.' })
    },
    onError: handleError,
  })

  const save = useMutation({
    mutationFn: () => api.saveConnection(input()),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['connections'] })
      qc.invalidateQueries({ queryKey: ['tables', form.id] })
      onClose()
    },
    onError: handleError,
  })

  const set = (k: keyof typeof form) => (e: { target: { value: string } }) =>
    setForm(f => ({ ...f, [k]: e.target.value }))

  function submit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>{initial ? `Editar ${initial.name}` : 'Nueva conexión'}</h2>
      <div className="grid-2">
        <label>
          Identificador
          <input value={form.id} onChange={set('id')} disabled={!!initial} placeholder="ventas" required />
          <span className="hint">Se usa en la URL. Minúsculas, números, - o _.</span>
          {fieldErrors.id && <span className="field-error">{fieldErrors.id}</span>}
        </label>
        <label>
          Nombre
          <input value={form.name} onChange={set('name')} placeholder="Ventas producción" required />
          {fieldErrors.name && <span className="field-error">{fieldErrors.name}</span>}
        </label>
        <label>
          Motor
          <select value={form.engine} onChange={set('engine')}>
            <option value="SqlServer">SQL Server</option>
            <option value="Postgres">PostgreSQL</option>
          </select>
        </label>
        <label>
          Esquemas (opcional)
          <input value={form.schemas} onChange={set('schemas')} placeholder="dbo, ventas" />
          <span className="hint">Separados por coma. Vacío = todos.</span>
        </label>
      </div>
      <label>
        Cadena de conexión
        <textarea
          rows={2}
          value={form.connectionString}
          onChange={set('connectionString')}
          placeholder={initial ? 'Déjala vacía para conservar la actual' : EXAMPLES[form.engine]}
          required={!initial}
        />
        <span className="hint">Se guarda cifrada. Usa un usuario de BD con los permisos mínimos necesarios.</span>
        {fieldErrors.connectionString && <span className="field-error">{fieldErrors.connectionString}</span>}
      </label>

      {result && <div className={result.ok ? 'alert ok' : 'alert'}>{result.text}</div>}

      <div className="actions">
        <button type="button" onClick={() => test.mutate()} disabled={test.isPending}>
          {test.isPending ? 'Probando…' : 'Probar conexión'}
        </button>
        <button className="primary" disabled={save.isPending}>{save.isPending ? 'Guardando…' : 'Guardar'}</button>
        <button type="button" className="link" onClick={onClose}>Cancelar</button>
      </div>
    </form>
  )
}
