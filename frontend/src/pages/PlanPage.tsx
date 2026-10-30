import { BadgeCheck, Check, ExternalLink, KeyRound, Lock, Search, Sparkles, X } from 'lucide-react'
import { useMemo, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { PlanInfo, PlanName } from '../api/types'
import { PRICING_URL, planLabel } from '../lib/plan'
import { useCan } from '../lib/useMe'
import { formatInstant } from '../lib/format'
import { matches } from '../lib/search'
import { translateServer, useT, type Translate } from '../i18n'

/** Plan y licencia (P-116, D-106): qué incluye, cuánto se usa, la clave y las tablas de Gratis. */
export function PlanPage() {
  const t = useT()
  const qc = useQueryClient()
  const canManage = useCan('ManageSecurity')
  const plan = useQuery({ queryKey: ['plan'], queryFn: api.plan })
  const [key, setKey] = useState('')
  const done = (p: PlanInfo) => { qc.setQueryData(['plan'], p); qc.invalidateQueries() }
  const save = useMutation({ mutationFn: () => api.setLicense(key.trim()), onSuccess: p => { setKey(''); done(p) } })
  const remove = useMutation({ mutationFn: api.removeLicense, onSuccess: done })
  const trial = useMutation({ mutationFn: api.startTrial, onSuccess: done })

  if (plan.isLoading) return <div className="page"><p className="muted">{t('Cargando…')}</p></div>
  if (plan.error || !plan.data) return <div className="page"><div className="alert">{(plan.error as Error)?.message}</div></div>
  const p = plan.data

  function submit(e: FormEvent) {
    e.preventDefault()
    if (key.trim()) save.mutate()
  }

  const features: [keyof PlanInfo['features'], string, PlanName][] = [
    ['profiles', t('Perfiles y permisos por tabla, columna y acción'), 'Pro'],
    ['allFormats', t('Exportar e importar Excel, JSON y XML'), 'Pro'],
    ['importUpsert', t('Importar actualizando por clave'), 'Pro'],
    ['notifications', t('Avisos por correo'), 'Pro'],
    ['actions', t('Botones con procedimientos'), 'Pro'],
    ['copySettings', t('Copiar ajustes entre conexiones'), 'Pro'],
    ['ai', t('IA por MCP y tokens de acceso'), 'Pro'],
    ['sso', t('Inicio de sesión con proveedor de identidad (SSO)'), 'Business'],
    ['infrastructure', t('Varias instancias, Redis, plugins y OpenTelemetry'), 'Business'],
  ]

  return (
    <div className="page plan-page">
      <div className="page-header">
        <h1 className="with-icon"><BadgeCheck size={22} aria-hidden />{t('Plan')}</h1>
        <a className="button" href={PRICING_URL} target="_blank" rel="noopener">{t('Ver planes y precios')}<ExternalLink size={15} aria-hidden /></a>
      </div>

      {p.requiresBusiness.length > 0 && (
        <div className="alert">{t('Esta instalación usa funciones del plan Empresa sin esa licencia, y la app no atiende hasta activarla: {list}.', { list: p.requiresBusiness.map(x => translateServer(x)).join('; ') })}</div>
      )}
      {p.licenseError && <div className="alert">{translateServer(p.licenseError)}</div>}
      {p.inGrace && p.graceUntil && (
        <div className="alert warn">{t('La licencia venció. Sigue funcionando hasta el {date}; después la instalación vuelve al plan Gratis.', { date: formatInstant(p.graceUntil) })}</div>
      )}

      <section className="card plan-current">
        <div className="plan-name">
          <span className="muted small">{t('Plan actual')}</span>
          <strong>{planLabel(t, p.plan)}</strong>
          <span className="muted">{sourceText(t, p)}</span>
        </div>
        <div className="plan-usage">
          <Meter label={t('Usuarios')} used={p.usage.users} max={p.limits.users} />
          <Meter label={t('Conexiones')} used={p.usage.connections} max={p.limits.connections} />
          {p.limits.tables != null && <Meter label={t('Tablas elegidas')} used={p.usage.tables} max={p.limits.tables} />}
          <div className="meter"><span>{t('Auditoría visible')}</span><b>{p.limits.auditDays ? t('{n} días', { n: p.limits.auditDays }) : t('Según la configuración')}</b></div>
        </div>
      </section>

      <section className="card">
        <h2>{t('Qué incluye')}</h2>
        <ul className="plan-features">
          {features.map(([id, label, from]) => (
            <li key={id} className={p.features[id] ? 'on' : 'off'}>
              {p.features[id] ? <Check size={16} aria-hidden /> : <X size={16} aria-hidden />}
              <span>{label}</span>
              {!p.features[id] && <span className="badge">{t('Desde {plan}', { plan: planLabel(t, from) })}</span>}
            </li>
          ))}
        </ul>
      </section>

      {canManage && (
        <section className="card form">
          <h2 className="with-icon"><KeyRound size={18} aria-hidden />{t('Licencia')}</h2>
          {p.source === 'config' ? (
            <p className="muted">{t('La licencia viene de la configuración del servidor (License:Key). Para cambiarla, cambia esa variable.')}</p>
          ) : (
            <form onSubmit={submit} className="plan-key">
              <label>
                {t('Pega aquí la clave que recibiste al comprar')}
                <textarea className="mono" rows={3} value={key} onChange={e => setKey(e.target.value)} placeholder="CELDO1.…" spellCheck={false} />
              </label>
              {save.error && <div className="alert">{(save.error as Error).message}</div>}
              <div className="actions">
                <button className="primary" disabled={!key.trim() || save.isPending}>{t('Activar licencia')}</button>
                {p.source === 'stored' && (
                  <button type="button" className="danger-text" disabled={remove.isPending} onClick={() => remove.mutate()}>{t('Quitar licencia')}</button>
                )}
              </div>
            </form>
          )}
          {p.trialAvailable && p.plan === 'Free' && (
            <div className="plan-trial">
              <Sparkles size={18} aria-hidden />
              <span>{t('Prueba Pro gratis durante 30 días, sin tarjeta. Al terminar, la instalación vuelve a Gratis sin perder nada.')}</span>
              <button type="button" disabled={trial.isPending} onClick={() => trial.mutate()}>{t('Empezar la prueba')}</button>
            </div>
          )}
          {trial.error && <div className="alert">{(trial.error as Error).message}</div>}
        </section>
      )}

      {p.limits.tables != null && <FreeTablesPicker plan={p} canManage={canManage} onSaved={done} />}
    </div>
  )
}

function sourceText(t: Translate, p: PlanInfo): string {
  if (p.source === 'trial' && p.trialEndsAt) return t('Prueba Pro hasta el {date}.', { date: formatInstant(p.trialEndsAt) })
  if (p.organization) {
    const until = p.expiresAt ? t('vence el {date}', { date: formatInstant(p.expiresAt) }) : t('no vence')
    return t('Licencia de {org}, {until}.', { org: p.organization, until })
  }
  return t('Sin licencia: 2 usuarios, 1 conexión y 15 tablas.')
}

function Meter({ label, used, max }: { label: string; used: number; max?: number | null }) {
  const t = useT()
  const full = max != null && used >= max
  return (
    <div className={`meter ${full ? 'full' : ''}`}>
      <span>{label}</span>
      <b>{max == null ? t('{n} · sin tope', { n: used }) : `${used} / ${max}`}</b>
      {max != null && <i aria-hidden><i style={{ width: `${Math.min(100, (used / Math.max(1, max)) * 100)}%` }} /></i>}
    </div>
  )
}

/**
 * Elegir las 15 tablas del plan Gratis (D-106). Las elegidas quedan fijas: se pueden sumar más hasta 15, nunca
 * quitar ni cambiar. Por eso hay un paso de confirmación antes de guardar.
 */
function FreeTablesPicker({ plan, canManage, onSaved }: { plan: PlanInfo; canManage: boolean; onSaved: (p: PlanInfo) => void }) {
  const t = useT()
  const tables = useQuery({ queryKey: ['plan', 'tables'], queryFn: api.freeTables, enabled: canManage && !!plan.freeConnection })
  const [picked, setPicked] = useState<Set<string>>(new Set())
  const [q, setQ] = useState('')
  const [confirming, setConfirming] = useState(false)
  const qc = useQueryClient()
  const add = useMutation({
    mutationFn: () => api.addFreeTables([...picked]),
    onSuccess: p => { setPicked(new Set()); setConfirming(false); qc.invalidateQueries({ queryKey: ['plan', 'tables'] }); onSaved(p) },
  })
  const max = plan.limits.tables ?? 15
  const left = max - plan.freeTables.length - picked.size
  const list = useMemo(() => (tables.data?.tables ?? []).filter(x => matches(q, x.name, x.label, x.schema)), [tables.data, q])

  if (!plan.freeConnection) return (
    <section className="card"><h2>{t('Tablas del plan Gratis')}</h2><p className="muted">{t('Crea la conexión y después elige aquí tus 15 tablas.')}</p></section>
  )

  const keyOf = (schema: string, name: string) => `${schema}.${name}`.toLowerCase()
  function toggle(k: string) {
    setConfirming(false)
    setPicked(prev => {
      const next = new Set(prev)
      if (next.has(k)) next.delete(k)
      else if (left > 0) next.add(k)
      return next
    })
  }

  return (
    <section className="card free-tables">
      <div className="free-tables-head">
        <div>
          <h2>{t('Tablas del plan Gratis')}</h2>
          <p className="muted">{t('Conexión {conn}: elegidas {n} de {max}. Las tablas elegidas no se pueden cambiar después; puedes sumar más hasta llegar a {max}.', { conn: plan.freeConnection, n: plan.freeTables.length, max })}</p>
        </div>
        {canManage && <span className={`badge ${left <= 0 ? 'full' : ''}`}>{t('Quedan {n}', { n: Math.max(0, left) })}</span>}
      </div>
      {!canManage && <p className="muted">{t('Las elige un administrador.')}</p>}
      {canManage && (
        <>
          <p className="muted small">{t('Si una tabla tiene combos de otra (por ejemplo, Producto → Categoría), elige también esa otra para que el combo funcione.')}</p>
          <label className="search-field">
            <Search size={16} aria-hidden />
            <input value={q} onChange={e => setQ(e.target.value)} placeholder={t('Buscar tabla…')} aria-label={t('Buscar tabla…')} />
          </label>
          {tables.error && <div className="alert">{(tables.error as Error).message}</div>}
          <ul className="free-table-list">
            {list.map(x => {
              const k = keyOf(x.schema, x.name)
              const fixed = x.selected
              const on = fixed || picked.has(k)
              return (
                <li key={k} className={fixed ? 'fixed' : on ? 'on' : ''}>
                  <label>
                    <input type="checkbox" checked={on} disabled={fixed || (!on && left <= 0)} onChange={() => toggle(k)} />
                    <span className="name">{x.label || x.name}</span>
                    <span className="muted small mono">{x.schema}.{x.name}</span>
                    {x.kind === 'View' && <span className="badge">{t('vista')}</span>}
                    {fixed && <Lock size={14} aria-label={t('Elegida')} />}
                  </label>
                </li>
              )
            })}
          </ul>
          {add.error && <div className="alert">{(add.error as Error).message}</div>}
          <div className="actions">
            {!confirming ? (
              <button className="primary" disabled={picked.size === 0} onClick={() => setConfirming(true)}>
                {t('Agregar {n} tablas', { n: picked.size })}
              </button>
            ) : (
              <div className="confirm-box">
                <span>{t('Estas {n} tablas quedarán fijas en el plan Gratis y no se podrán cambiar. ¿Confirmas?', { n: picked.size })}</span>
                <button className="primary" disabled={add.isPending} onClick={() => add.mutate()}>{t('Sí, agregarlas')}</button>
                <button type="button" onClick={() => setConfirming(false)}>{t('Cancelar')}</button>
              </div>
            )}
          </div>
        </>
      )}
    </section>
  )
}
