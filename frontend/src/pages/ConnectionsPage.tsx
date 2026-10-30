import { SearchSelect } from '../components/Combo'
import { ArrowRight, Copy, CopyPlus, Database, Pencil, Plug, Plus, Trash2 } from 'lucide-react'
import { CopyOverridesPanel } from '../components/CopyOverrides'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import type { Connection, ConnectionInput, Engine, EngineInfo } from '../api/types'
import { useCan } from '../lib/useMe'
import { useT } from '../i18n'

const EXAMPLES: Record<string, string> = {
  SqlServer: 'Server=mi-servidor,1433;Database=MiBase;User Id=usuario;Password=***;TrustServerCertificate=True',
  Postgres: 'Host=mi-servidor;Port=5432;Database=mibase;Username=usuario;Password=***',
  MySql: 'Server=mi-servidor;Port=3306;Database=mibase;User ID=usuario;Password=***',
}

/**
 * Nombre y estilo de los motores que trae la app. MySql cubre MySQL y MariaDB (un solo proveedor).
 * Los de plugins (P-064) toman nombre y ejemplo del servidor.
 */
const ENGINES: Record<string, { label: string; badge: string }> = {
  SqlServer: { label: 'SQL Server', badge: 'badge-mssql' },
  Postgres: { label: 'PostgreSQL', badge: 'badge-pg' },
  MySql: { label: 'MySQL / MariaDB', badge: 'badge-mysql' },
}

/** Motores instalados en el servidor; mientras llega la lista, los tres de siempre. */
function useEngines(): EngineInfo[] {
  return useQuery({ queryKey: ['engines'], queryFn: api.engines, staleTime: Infinity }).data
    ?? Object.entries(ENGINES).map(([name, e]): EngineInfo => ({ name, label: e.label }))
}

export function ConnectionsPage() {
  const t = useT()
  const qc = useQueryClient()
  const canManage = useCan('ManageConnections')
  const canConfigure = useCan('ConfigureTables')
  const [copying, setCopying] = useState<Connection | null>(null)
  const [duplicating, setDuplicating] = useState<Connection | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const { data, isLoading, error } = useQuery({ queryKey: ['connections'], queryFn: api.connections })
  const engines = useEngines()
  const engineLabel = (e: Engine) => ENGINES[e]?.label ?? engines.find(x => x.name === e)?.label ?? e
  const [editing, setEditing] = useState<Connection | 'new' | null>(null)

  const remove = useMutation({
    mutationFn: api.deleteConnection,
    onSuccess: () => qc.invalidateQueries({ queryKey: ['connections'] }),
  })

  return (
    <div className="page">
      <div className="page-header">
        <h1>{t('Conexiones')}</h1>
        {canManage && <button className="primary" onClick={() => setEditing('new')}><Plus size={16} aria-hidden />{t('Nueva conexión')}</button>}
      </div>

      {notice && <div className="alert ok">{notice}</div>}
      {editing && (
        <ConnectionForm
          initial={editing === 'new' ? null : editing}
          onClose={() => setEditing(null)}
        />
      )}
      {duplicating && (
        <ConnectionForm initial={null} template={duplicating}
          onClose={() => setDuplicating(null)} onSaved={setNotice} />
      )}
      {copying && data && (
        <CopyOverridesPanel target={copying} connections={data} onClose={() => setCopying(null)} onDone={setNotice} />
      )}

      {isLoading && <p className="muted">{t('Cargando…')}</p>}
      {error && <div className="alert">{(error as Error).message}</div>}
      {data?.length === 0 && !editing && (
        <div className="empty">
          <Database size={36} strokeWidth={1.5} className="empty-icon" aria-hidden />
          {canManage
            ? t('Aún no hay conexiones. Crea una para descubrir sus tablas.')
            : t('Aún no hay conexiones. Pídele a quien administra las conexiones que agregue una.')}
        </div>
      )}

      <div className="cards">
        {data?.map(c => (
          <div key={c.id} className="card conn-card">
            <div className="conn-main">
              <span className={`conn-icon ${ENGINES[c.engine]?.badge ?? ''}`} aria-hidden><Database size={20} /></span>
              <div>
              <div className="conn-title">{c.name}</div>
              <div className="muted small">
                <span className={`badge ${ENGINES[c.engine]?.badge ?? ''}`}>
                  {engineLabel(c.engine)}
                </span>{' '}
                {c.server} / {c.database}
              </div>
              {c.schemas.length > 0 && <div className="muted small">{t('Esquemas: {list}', { list: c.schemas.join(', ') })}</div>}
              </div>
            </div>
            <div className="actions">
              <Link className="button primary" to={`/c/${c.id}`}>{t('Abrir')}<ArrowRight size={16} aria-hidden /></Link>
              {canConfigure && (data?.length ?? 0) > 1 && (
                <button className="icon-button" title={t('Copiar ajustes desde otra conexión')} aria-label={t('Copiar ajustes desde otra conexión')}
                  onClick={() => { setNotice(null); setCopying(c) }}><Copy size={16} aria-hidden /></button>
              )}
              {canManage && (
                <>
                  <button className="icon-button" title={t('Duplicar: la misma base en otro servidor, con sus ajustes')}
                    aria-label={t('Duplicar')} onClick={() => { setNotice(null); setDuplicating(c) }}><CopyPlus size={16} aria-hidden /></button>
                  <button className="icon-button" title={t('Editar')} aria-label={t('Editar')} onClick={() => setEditing(c)}><Pencil size={16} aria-hidden /></button>
                  <button
                    className="icon-button danger"
                    title={t('Eliminar')} aria-label={t('Eliminar')}
                    onClick={() => confirm(t('¿Eliminar la conexión "{name}"? No borra datos de la base.', { name: c.name })) && remove.mutate(c.id)}
                  >
                    <Trash2 size={16} aria-hidden />
                  </button>
                </>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

/**
 * Alta o edición de una conexión. Con <c>template</c> es "Duplicar" (P-111): mismo motor y esquemas, otro servidor;
 * al guardar se copian los ajustes de la original.
 */
function ConnectionForm({ initial, template, onClose, onSaved }: {
  initial: Connection | null
  template?: Connection
  onClose: () => void
  onSaved?: (message: string) => void
}) {
  const t = useT()
  const qc = useQueryClient()
  const base = initial ?? template
  const [form, setForm] = useState({
    id: initial?.id ?? '',
    name: template ? `${template.name} (${t('copia')})` : initial?.name ?? '',
    engine: (base?.engine ?? 'SqlServer') as Engine,
    connectionString: '',
    schemas: base?.schemas.join(', ') ?? '',
    allowSamples: base?.allowSamples ?? false,
  })
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  // Un motor guardado que ya no está instalado igual se muestra, para no cambiarlo sin querer.
  const engines = useEngines()
  const options = engines.some(e => e.name === form.engine) ? engines : [...engines, { name: form.engine, label: form.engine }]
  const example = EXAMPLES[form.engine] ?? engines.find(e => e.name === form.engine)?.example ?? ''

  const input = (): ConnectionInput => ({
    id: form.id.trim().toLowerCase(),
    name: form.name,
    engine: form.engine,
    connectionString: form.connectionString || undefined,
    schemas: form.schemas.split(',').map(s => s.trim()).filter(Boolean),
    allowSamples: form.allowSamples,
  })

  const handleError = (err: unknown) => {
    setFieldErrors(err instanceof ApiError ? err.fieldErrors : {})
    setResult({ ok: false, text: (err as Error).message })
  }

  const test = useMutation({
    mutationFn: () => api.testConnection(input()),
    onSuccess: r => {
      setFieldErrors({})
      const server = r.server ? ' ' + t('Servidor: {server}.', { server: r.server }) : ''
      setResult(r.ok
        ? { ok: true, text: t('Conexión correcta: {n} tablas/vistas encontradas.', { n: r.tables }) + server }
        : { ok: false, text: r.error ?? t('No se pudo conectar.') })
    },
    onError: handleError,
  })

  const save = useMutation({
    mutationFn: async () => {
      const saved = await api.saveConnection(input())
      // Duplicar: los ajustes de la original pasan a la nueva (lo que no aplica se informa).
      return template ? { saved, copy: await api.copyOverrides(saved.id, { from: template.id, apply: true }) } : { saved, copy: null }
    },
    onSuccess: ({ saved, copy }) => {
      qc.invalidateQueries({ queryKey: ['connections'] })
      qc.invalidateQueries({ queryKey: ['tables', form.id] })
      if (copy) {
        const skipped = copy.items.filter(i => i.status === 'MissingTable' || i.status === 'Invalid').length
        onSaved?.(t('Conexión {name} creada con {n} ajustes copiados.', { name: saved.name, n: copy.copied })
          + (skipped ? ' ' + t('{n} no se copiaron (tabla inexistente o ajuste no válido en la nueva base).', { n: skipped }) : ''))
      }
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
      <h2>{initial ? t('Editar {name}', { name: initial.name }) : template ? t('Duplicar {name}', { name: template.name }) : t('Nueva conexión')}</h2>
      {template && <p className="muted small">{t('Misma base en otro servidor o instancia: indique el identificador y la cadena de conexión nuevos. Al guardar se copian los ajustes de {name}.', { name: template.name })}</p>}
      <div className="grid-2">
        <label>
          {t('Identificador')}
          <input value={form.id} onChange={set('id')} disabled={!!initial} placeholder="ventas" required />
          <span className="hint">{t('Se usa en la URL. Minúsculas, números, - o _.')}</span>
          {fieldErrors.id && <span className="field-error">{fieldErrors.id}</span>}
        </label>
        <label>
          {t('Nombre')}
          <input value={form.name} onChange={set('name')} placeholder={t('Ventas producción')} required />
          {fieldErrors.name && <span className="field-error">{fieldErrors.name}</span>}
        </label>
        <label>
          {t('Motor')}
          <SearchSelect value={form.engine} disabled={!!template} onChange={v => setForm(f => ({ ...f, engine: v }))}
            options={options.map(e => ({ value: e.name, label: ENGINES[e.name]?.label ?? e.label }))} />
        </label>
        <label>
          {t('Esquemas (opcional)')}
          <input value={form.schemas} onChange={set('schemas')} placeholder="dbo, ventas" />
          <span className="hint">
            {form.engine === 'MySql'
              ? t('En MySQL cada esquema es una base de datos. Vacío = la base de la cadena de conexión.')
              : t('Separados por coma. Vacío = todos.')}
          </span>
        </label>
      </div>
      <label>
        {t('Cadena de conexión')}
        <textarea
          rows={2}
          value={form.connectionString}
          onChange={set('connectionString')}
          placeholder={initial ? t('Déjala vacía para conservar la actual') : example}
          required={!initial}
        />
        <span className="hint">{t('Se guarda cifrada. Usa un usuario de BD con los permisos mínimos necesarios.')}</span>
        {fieldErrors.connectionString && <span className="field-error">{fieldErrors.connectionString}</span>}
      </label>

      <label className="check">
        <input type="checkbox" checked={form.allowSamples} onChange={e => setForm(f => ({ ...f, allowSamples: e.target.checked }))} />
        {t('Permitir que la IA vea una muestra de datos (las 1.000 filas más recientes de cada tabla)')}
      </label>
      <span className="hint">{t('Sin esto, una IA conectada por MCP solo ve la estructura (tablas y columnas). Respeta los permisos de quien creó el token.')}</span>

      {result && <div className={result.ok ? 'alert ok' : 'alert'}>{result.text}</div>}

      <div className="actions">
        <button type="button" onClick={() => test.mutate()} disabled={test.isPending}>
          <Plug size={16} aria-hidden />
          {test.isPending ? t('Probando…') : t('Probar conexión')}
        </button>
        <button className="primary" disabled={save.isPending}>{save.isPending ? t('Guardando…') : t('Guardar')}</button>
        <button type="button" className="link" onClick={onClose}>{t('Cancelar')}</button>
      </div>
    </form>
  )
}
