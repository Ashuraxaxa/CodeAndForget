import { Check, Copy, KeyRound, Plus, Trash2 } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { SearchSelect } from '../components/Combo'
import { formatInstant } from '../lib/format'
import { useT } from '../i18n'

/**
 * Tokens de acceso del usuario (P-113, D-100), p. ej. para conectar una IA por MCP. Cada token tiene exactamente el
 * acceso de quien lo creó; el secreto se muestra una sola vez.
 */
export function TokensPage() {
  const t = useT()
  const qc = useQueryClient()
  const tokens = useQuery({ queryKey: ['tokens'], queryFn: api.tokens })
  const mcp = useQuery({ queryKey: ['tokens', 'mcp'], queryFn: api.mcpInfo, staleTime: Infinity })
  const [name, setName] = useState('')
  const [days, setDays] = useState('90')
  const [created, setCreated] = useState<{ name: string; secret: string } | null>(null)
  const [copied, setCopied] = useState(false)

  const create = useMutation({
    mutationFn: () => api.createToken(name.trim(), days ? Number(days) : undefined),
    onSuccess: r => {
      setCreated({ name: r.token.name, secret: r.secret })
      setCopied(false)
      setName('')
      qc.invalidateQueries({ queryKey: ['tokens'] })
    },
  })
  const remove = useMutation({ mutationFn: api.deleteToken, onSuccess: () => qc.invalidateQueries({ queryKey: ['tokens'] }) })

  function submit(e: FormEvent) {
    e.preventDefault()
    if (name.trim()) create.mutate()
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1 className="with-icon"><KeyRound size={22} aria-hidden />{t('Tokens de acceso')}</h1>
      </div>
      <p className="muted">{t('Para conectar una IA u otra herramienta sin iniciar sesión. Cada token tiene exactamente tu acceso: si cambian tus permisos, cambian los del token. Bórralo cuando ya no lo uses.')}</p>

      {created && (
        <div className="card token-created">
          <strong>{t('Token "{name}" creado. Cópialo ahora: no se volverá a mostrar.', { name: created.name })}</strong>
          <div className="row">
            <code className="token-secret">{created.secret}</code>
            <button type="button" onClick={() => { navigator.clipboard?.writeText(created.secret); setCopied(true) }}>
              {copied ? <Check size={16} aria-hidden /> : <Copy size={16} aria-hidden />}{copied ? t('Copiado') : t('Copiar')}
            </button>
          </div>
        </div>
      )}

      {mcp.data && <McpHelp enabled={mcp.data.enabled} url={new URL(mcp.data.path, document.baseURI).href} secret={created?.secret} />}

      <form className="card form" onSubmit={submit}>
        <h2>{t('Nuevo token')}</h2>
        <div className="grid-2">
          <label>
            {t('Nombre')}
            <input value={name} onChange={e => setName(e.target.value)} placeholder={t('p. ej. Claude en mi PC')} maxLength={100} required />
          </label>
          <label>
            {t('Vence en')}
            <SearchSelect value={days} onChange={setDays} options={[
              { value: '30', label: t('{n} días', { n: 30 }) },
              { value: '90', label: t('{n} días', { n: 90 }) },
              { value: '365', label: t('{n} días', { n: 365 }) },
              { value: '', label: t('Sin vencimiento') },
            ]} />
          </label>
        </div>
        {create.error && <div className="alert">{(create.error as Error).message}</div>}
        <div className="actions">
          <button className="primary" disabled={create.isPending || !name.trim()}><Plus size={16} aria-hidden />{t('Crear token')}</button>
        </div>
      </form>

      {tokens.error && <div className="alert">{(tokens.error as Error).message}</div>}
      {tokens.data && tokens.data.length === 0 && <p className="muted">{t('Aún no tienes tokens.')}</p>}
      {tokens.data && tokens.data.length > 0 && (
        <div className="table-wrap">
          <table className="grid">
            <thead><tr>
              <th className="no-sort">{t('Nombre')}</th><th className="no-sort">{t('Token')}</th><th className="no-sort">{t('Creado')}</th>
              <th className="no-sort">{t('Vence')}</th><th className="no-sort">{t('Último uso')}</th><th className="no-sort" />
            </tr></thead>
            <tbody>
              {tokens.data.map(tk => (
                <tr key={tk.id}>
                  <td>{tk.name}</td>
                  <td><code>{tk.prefix}…</code></td>
                  <td className="nowrap">{formatInstant(tk.createdAt)}</td>
                  <td className="nowrap">{tk.expiresAt ? formatInstant(tk.expiresAt) : t('Sin vencimiento')}</td>
                  <td className="nowrap">{tk.lastUsedAt ? formatInstant(tk.lastUsedAt) : t('Nunca')}</td>
                  <td className="actions-col">
                    <button className="icon-button sm danger" title={t('Borrar')} aria-label={t('Borrar')}
                      onClick={() => confirm(t('¿Borrar el token "{name}"? Lo que lo use dejará de tener acceso.', { name: tk.name })) && remove.mutate(tk.id)}>
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

/** Cómo conectar un cliente MCP (P-114): la URL y el comando de Claude Code, con el token recién creado si lo hay. */
function McpHelp({ enabled, url, secret }: { enabled: boolean; url: string; secret?: string }) {
  const t = useT()
  if (!enabled) return <p className="muted">{t('El servidor MCP está apagado. Para que una IA revise el esquema y proponga ajustes, actívelo con Mcp:Enabled = true.')}</p>
  const token = secret ?? '<token>'
  const command = `claude mcp add --transport http mantenedores ${url} --header "Authorization: Bearer ${token}"`
  return (
    <details className="card mcp-help">
      <summary><strong>{t('Conectar una IA por MCP')}</strong></summary>
      <p className="muted">{t('La IA lee el esquema, las sugerencias y, si la conexión lo permite, una muestra de datos. Sus ajustes quedan como propuestas que aceptas o descartas en Configurar.')}</p>
      <label>{t('URL del servidor MCP')}<code className="token-secret">{url}</code></label>
      <label>{t('Claude Code')}<code className="token-secret">{command}</code></label>
      <p className="muted">{t('En otros clientes (Claude Desktop, Cursor, VS Code…), agregue un servidor MCP por HTTP con esa URL y el encabezado Authorization: Bearer y su token.')}</p>
    </details>
  )
}
