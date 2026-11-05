import { Check, Copy, KeyRound, Lock, Pencil, Plus, Power, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { EmbedApp } from '../api/types'
import { ErrorText } from '../components/ErrorText'
import { formatInstant } from '../lib/format'
import { useT } from '../i18n'

/**
 * Seguridad → Aplicaciones (P-135, D-113): las apps que embeben Celdo para usuarios sin cuenta. Cada una firma sus
 * tokens con su clave, que se muestra una sola vez.
 */
export function EmbedAppsPanel() {
  const t = useT()
  const qc = useQueryClient()
  const apps = useQuery({ queryKey: ['embed-apps'], queryFn: api.embedApps })
  const settings = useQuery({ queryKey: ['embed-settings'], queryFn: api.embedSettings })
  const setPublic = useMutation({
    mutationFn: (on: boolean) => api.setEmbedSettings({ publicDashboards: on }),
    onSuccess: s => qc.setQueryData(['embed-settings'], s),
  })
  const profiles = useQuery({ queryKey: ['profiles'], queryFn: api.profiles })
  const [editing, setEditing] = useState<EmbedApp | 'new' | null>(null)
  const [secret, setSecret] = useState<{ app: EmbedApp; secret: string } | null>(null)
  const refresh = () => qc.invalidateQueries({ queryKey: ['embed-apps'] })

  const toggle = useMutation({ mutationFn: (a: EmbedApp) => api.updateEmbedApp(a.id, { enabled: !a.enabled }), onSuccess: refresh })
  const rotate = useMutation({
    mutationFn: (a: EmbedApp) => api.rotateEmbedSecret(a.id).then(r => ({ app: a, secret: r.secret })),
    onSuccess: r => { setSecret(r); refresh() },
  })
  const remove = useMutation({ mutationFn: (a: EmbedApp) => api.deleteEmbedApp(a.id), onSuccess: refresh })
  const profileName = (id: string) => profiles.data?.find(p => p.id === id)?.name ?? id
  const data = apps.data
  const error = apps.error ?? toggle.error ?? rotate.error ?? remove.error

  return (
    <div className="embed-apps">
      <p className="muted">
        {t('Para embeber dashboards y mantenedores en otra aplicación, para usuarios que no tienen cuenta en Celdo. El servidor de esa aplicación firma un token corto con su clave; Celdo nunca da más que el perfil elegido.')}
        {' '}<a href="https://github.com/Ashuraxaxa/CodeAndForget/blob/main/docs/embeber.md" target="_blank" rel="noopener">{t('Cómo firmar el token')}</a>
      </p>
      <label className="check public-toggle">
        <input type="checkbox" checked={settings.data?.publicDashboards === true} disabled={!settings.data || setPublic.isPending}
          onChange={e => setPublic.mutate(e.target.checked)} />
        <span><strong>{t('Permitir dashboards públicos')}</strong>{' · '}<span className="muted">{t('El dueño de un dashboard puede crear un enlace que cualquiera mira sin login. Al apagarlo, ningún enlace responde.')}</span></span>
      </label>
      {setPublic.error && <div className="alert"><ErrorText error={setPublic.error} /></div>}
      {error && <div className="alert"><ErrorText error={error} /></div>}
      {secret && <SecretCard app={secret.app} secret={secret.secret} onClose={() => setSecret(null)} />}

      {editing ? (
        <AppForm app={editing === 'new' ? null : editing} profiles={profiles.data ?? []} onCancel={() => setEditing(null)}
          onSaved={r => { setEditing(null); refresh(); if (r) setSecret(r) }} />
      ) : (
        <div className="actions">
          <button type="button" className="primary" disabled={data ? !data.canCreate : true} onClick={() => setEditing('new')}>
            <Plus size={16} aria-hidden />{t('Nueva aplicación')}
          </button>
          {data && data.limit != null && <span className="badge">{t('{n} de {max} en tu plan', { n: data.items.length, max: data.limit })}</span>}
        </div>
      )}

      {data && data.items.length === 0 && !editing && <p className="muted">{t('Todavía no hay aplicaciones.')}</p>}
      {data && data.items.length > 0 && (
        <div className="table-wrap">
          <table className="grid">
            <thead><tr>
              <th className="no-sort">{t('Nombre')}</th><th className="no-sort">{t('Id (kid)')}</th><th className="no-sort">{t('Sitios')}</th>
              <th className="no-sort">{t('Perfil')}</th><th className="no-sort">{t('Estado')}</th><th className="no-sort">{t('Clave desde')}</th><th className="no-sort" />
            </tr></thead>
            <tbody>
              {data.items.map(a => (
                <tr key={a.id}>
                  <td><strong>{a.name}</strong></td>
                  <td><code>{a.id}</code></td>
                  <td>{a.origins.join(', ')}</td>
                  <td>{profileName(a.profileId)}</td>
                  <td>{a.locked ? <span className="badge full"><Lock size={13} aria-hidden />{t('Requiere otro plan')}</span>
                    : a.enabled ? <span className="badge">{t('Activa')}</span> : <span className="badge muted">{t('Desactivada')}</span>}</td>
                  <td className="nowrap">{formatInstant(a.secretSetAt)}</td>
                  <td className="actions-col nowrap">
                    <button className="icon-button sm" title={t('Editar')} aria-label={t('Editar {name}', { name: a.name })} onClick={() => setEditing(a)}><Pencil size={15} aria-hidden /></button>
                    <button className="icon-button sm" title={a.enabled ? t('Desactivar') : t('Activar')} aria-label={a.enabled ? t('Desactivar') : t('Activar')}
                      disabled={toggle.isPending} onClick={() => toggle.mutate(a)}><Power size={15} aria-hidden /></button>
                    <button className="icon-button sm" title={t('Nueva clave')} aria-label={t('Nueva clave')} disabled={rotate.isPending}
                      onClick={() => { if (confirm(t('¿Crear una clave nueva para "{name}"? La actual deja de valer y las páginas abiertas se cierran.', { name: a.name }))) rotate.mutate(a) }}>
                      <KeyRound size={15} aria-hidden />
                    </button>
                    <button className="icon-button sm danger" title={t('Borrar')} aria-label={t('Borrar {name}', { name: a.name })} disabled={remove.isPending}
                      onClick={() => { if (confirm(t('¿Borrar la aplicación "{name}"? Sus páginas embebidas dejan de funcionar.', { name: a.name }))) remove.mutate(a) }}>
                      <Trash2 size={15} aria-hidden />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function AppForm({ app, profiles, onCancel, onSaved }: {
  app: EmbedApp | null
  profiles: { id: string; name: string }[]
  onCancel: () => void
  onSaved: (created: { app: EmbedApp; secret: string } | null) => void
}) {
  const t = useT()
  const [name, setName] = useState(app?.name ?? '')
  const [origins, setOrigins] = useState((app?.origins ?? []).join('\n'))
  const [profileId, setProfileId] = useState(app?.profileId ?? '')
  const [columns, setColumns] = useState(Object.entries(app?.filterColumns ?? {}).map(([k, v]) => `${k} = ${v}`).join('\n'))
  const [unfiltered, setUnfiltered] = useState((app?.unfiltered ?? []).join('\n'))
  const lines = (s: string) => s.split(/\n/).map(l => l.trim()).filter(Boolean)
  const input = () => ({
    name: name.trim(), origins: origins.split(/[\s,]+/).filter(Boolean), profileId,
    filterColumns: Object.fromEntries(lines(columns).map(l => { const i = l.indexOf('='); return i < 0 ? [l, ''] : [l.slice(0, i).trim(), l.slice(i + 1).trim()] })),
    unfiltered: lines(unfiltered),
  })
  const save = useMutation({
    mutationFn: async () => app ? (await api.updateEmbedApp(app.id, input()), null) : api.createEmbedApp(input()),
    onSuccess: onSaved,
  })
  const fieldErrors = save.error && 'fieldErrors' in save.error ? (save.error as { fieldErrors: Record<string, string> }).fieldErrors : {}

  function submit(e: FormEvent) {
    e.preventDefault()
    save.mutate()
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>{app ? t('Aplicación · {name}', { name: app.name }) : t('Nueva aplicación')}</h2>
      <div className="grid-2">
        <label>
          {t('Nombre')}
          <input value={name} onChange={e => setName(e.target.value)} maxLength={100} placeholder={t('Portal de clientes')} required autoFocus />
          {fieldErrors.name && <span className="field-error">{fieldErrors.name}</span>}
        </label>
        <label>
          {t('Perfil')}
          <select value={profileId} onChange={e => setProfileId(e.target.value)} required>
            <option value="">{t('Elige un perfil…')}</option>
            {profiles.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          <span className="hint">{t('El máximo que pueden dar sus tokens. Para sus dashboards, el perfil necesita "Ver" dashboards y que se compartan con él.')}</span>
          {fieldErrors.profileId && <span className="field-error">{fieldErrors.profileId}</span>}
        </label>
      </div>
      <label>
        {t('Sitios desde donde se embebe')}
        <textarea rows={3} value={origins} onChange={e => setOrigins(e.target.value)} placeholder="https://portal.empresa.com" required />
        <span className="hint">{t('Uno por línea: esquema y host, sin ruta. Se acepta un comodín al inicio: https://*.empresa.com')}</span>
        {fieldErrors.origins && <span className="field-error">{fieldErrors.origins}</span>}
      </label>
      <details className="embed-rows" open={!!(columns || unfiltered)}>
        <summary>{t('Filtros por fila')}</summary>
        <p className="hint">{t('Si el token trae filtros, p. ej. { "cliente_id": 42 }, cada tabla con esa columna muestra y edita solo esas filas, y al crear se pone el valor solo. Una tabla sin la columna no se abre, salvo que esté abajo como "sin filtro".')}</p>
        <div className="grid-2">
          <label>
            {t('Otra columna para un filtro')}
            <textarea rows={3} value={columns} onChange={e => setColumns(e.target.value)} placeholder="ventas.factura.cliente_id = id_cliente" />
            <span className="hint">{t('Una por línea: esquema.tabla.filtro = columna')}</span>
            {fieldErrors.filterColumns && <span className="field-error">{fieldErrors.filterColumns}</span>}
          </label>
          <label>
            {t('Tablas sin filtro')}
            <textarea rows={3} value={unfiltered} onChange={e => setUnfiltered(e.target.value)} placeholder="ventas.categoria" />
            <span className="hint">{t('Una por línea: esquema.tabla. Para catálogos que todos pueden ver.')}</span>
            {fieldErrors.unfiltered && <span className="field-error">{fieldErrors.unfiltered}</span>}
          </label>
        </div>
      </details>
      {save.error && Object.keys(fieldErrors).length === 0 && <div className="alert"><ErrorText error={save.error} /></div>}
      <div className="actions">
        <button className="primary" disabled={save.isPending}>{app ? t('Guardar') : t('Crear y ver la clave')}</button>
        <button type="button" onClick={onCancel}>{t('Cancelar')}</button>
      </div>
    </form>
  )
}

function SecretCard({ app, secret, onClose }: { app: EmbedApp; secret: string; onClose: () => void }) {
  const t = useT()
  const [copied, setCopied] = useState(false)
  return (
    <div className="card token-created">
      <strong>{t('Clave de "{name}": cópiala ahora, no se vuelve a mostrar.', { name: app.name })}</strong>
      <div className="row">
        <code className="token-secret">{secret}</code>
        <button type="button" onClick={() => { navigator.clipboard?.writeText(secret); setCopied(true) }}>
          {copied ? <Check size={16} aria-hidden /> : <Copy size={16} aria-hidden />}{copied ? t('Copiado') : t('Copiar')}
        </button>
      </div>
      <p className="muted small">
        {t('Guárdala en el servidor de la aplicación, nunca en el navegador. Firma con HS256, kid = {id}, y vence en 1 hora como máximo.', { id: app.id })}
      </p>
      <div><button type="button" onClick={onClose}>{t('Listo')}</button></div>
    </div>
  )
}
