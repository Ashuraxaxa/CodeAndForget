import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { formatInstant } from '../lib/format'
import { api } from '../api/client'
import type { SchemaChange } from '../api/types'
import { useCan } from '../lib/useMe'
import { translateServer, useT } from '../i18n'

/**
 * Aviso de cambios en el esquema desde la foto de referencia y de ajustes que quedaron sin efecto (P-013).
 * Quien puede configurar mantenedores los da por revisados con "Aceptar cambios".
 */
export function SchemaChangesBanner({ conn }: { conn: string }) {
  const qc = useQueryClient()
  const t = useT()
  const canConfigure = useCan('ConfigureTables')
  const [open, setOpen] = useState(false)
  const diff = useQuery({ queryKey: ['diff', conn], queryFn: () => api.diff(conn) })
  const accept = useMutation({
    mutationFn: () => api.acceptDiff(conn),
    onSuccess: d => qc.setQueryData(['diff', conn], d),
  })

  const d = diff.data
  if (!d || (d.changes.length === 0 && d.overrideWarnings.length === 0)) return null

  const parts = [
    d.changes.length > 0 && t('{n} cambios en el esquema', { n: d.changes.length }),
    d.overrideWarnings.length > 0 && t('{n} ajustes sin efecto', { n: d.overrideWarnings.length }),
  ].filter(Boolean)

  const byTable = new Map<string, SchemaChange[]>()
  for (const c of d.changes) {
    const k = `${c.schema}.${c.table}`
    byTable.set(k, [...(byTable.get(k) ?? []), c])
  }

  return (
    <div className="banner">
      <div className="banner-head">
        <span>⚠ {t('{changes} desde el {date}', { changes: parts.join(' · '), date: formatInstant(d.since) })}</span>
        <button className="link" onClick={() => setOpen(o => !o)}>{open ? t('Ocultar') : t('Ver detalle')}</button>
        {canConfigure && d.changes.length > 0 && (
          <button disabled={accept.isPending} onClick={() => accept.mutate()}
            title={t('Toma el esquema actual como referencia; los avisos de cambios desaparecen')}>
            {accept.isPending ? t('Aceptando…') : t('Aceptar cambios')}
          </button>
        )}
      </div>
      {open && (
        <div className="banner-body">
          {[...byTable.entries()].map(([table, changes]) => {
            const c0 = changes[0]
            const removed = c0.kind === 'TableRemoved'
            return (
              <div key={table}>
                {removed ? <b>{table}</b> : (
                  <Link to={`/c/${conn}/t/${encodeURIComponent(c0.schema)}/${encodeURIComponent(c0.table)}`}><b>{table}</b></Link>
                )}
                <ul>
                  {changes.map((c, i) => <li key={i}>{c.column ? <code>{c.column}</code> : null}{c.column ? ': ' : ''}{translateServer(c.description)}</li>)}
                </ul>
              </div>
            )
          })}
          {d.overrideWarnings.length > 0 && (
            <div>
              <b>{t('Ajustes sin efecto')}</b> <span className="muted small">{t('(se ignoran; corríjalos en ⚙ Configurar)')}</span>
              <ul>{d.overrideWarnings.map(w => <li key={w}>{w}</li>)}</ul>
            </div>
          )}
          {accept.error && <div className="alert">{(accept.error as Error).message}</div>}
        </div>
      )}
    </div>
  )
}
