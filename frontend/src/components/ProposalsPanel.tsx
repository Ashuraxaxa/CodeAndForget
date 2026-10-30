import { Bot, Check, X } from 'lucide-react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { OverrideProposal } from '../api/types'
import { formatInstant } from '../lib/format'
import { useT } from '../i18n'

/**
 * Propuestas pendientes de la IA para esta tabla (P-113, D-100). "Cargar en la edición" reemplaza lo que se está
 * editando por la propuesta; al guardar queda aceptada. "Descartar" la cierra sin cambios.
 */
export function ProposalsPanel({ conn, schema, table, loaded, onLoad }: {
  conn: string
  schema: string
  table: string
  loaded: string | null
  onLoad: (p: OverrideProposal) => void
}) {
  const t = useT()
  const qc = useQueryClient()
  const all = useQuery({ queryKey: ['proposals', conn], queryFn: () => api.proposals(conn) })
  const mine = (all.data ?? []).filter(p => p.schema.toLowerCase() === schema.toLowerCase() && p.table.toLowerCase() === table.toLowerCase())
  const discard = useMutation({
    mutationFn: (id: string) => api.resolveProposal(conn, id, 'discard'),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['proposals', conn] }),
  })
  if (mine.length === 0) return null
  return (
    <div className="card proposals">
      <h2 className="with-icon"><Bot size={18} aria-hidden />{t('Propuestas de la IA ({n})', { n: mine.length })}</h2>
      {mine.map(p => (
        <div key={p.id} className={`proposal ${loaded === p.id ? 'is-loaded' : ''}`}>
          <div className="muted small">{t('{who}, {when}', { who: p.createdBy ?? '?', when: formatInstant(p.createdAt) })}</div>
          {p.reason && <p className="proposal-reason">{p.reason}</p>}
          <div className="actions">
            {loaded === p.id
              ? <span className="badge with-icon"><Check size={14} aria-hidden />{t('En la edición: revísela en la vista previa y guarde')}</span>
              : <button type="button" onClick={() => onLoad(p)}>{t('Cargar en la edición')}</button>}
            <button type="button" className="link danger" onClick={() => discard.mutate(p.id)}><X size={14} aria-hidden />{t('Descartar')}</button>
          </div>
        </div>
      ))}
    </div>
  )
}
