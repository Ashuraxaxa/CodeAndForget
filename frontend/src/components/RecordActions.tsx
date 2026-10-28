import { CircleCheck, Play } from 'lucide-react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import { labelOf, type ActionResult, type TableAction, type TableMeta } from '../api/types'
import { displayValue } from '../lib/format'
import { useT } from '../i18n'

/**
 * Botones de las acciones de la tabla (P-061) en el formulario de un registro. Pide los parámetros marcados como
 * "preguntar", confirma si la acción lo pide y muestra lo que devolvió la rutina.
 */
export type ActionRun = { action: TableAction; data: ActionResult }

/** El resultado vive fuera del formulario: después de la acción, el formulario se rearma con el registro actual. */
export function RecordActions({ conn, meta, recordKey, onDone, setResult }: {
  conn: string; meta: TableMeta; recordKey: string; onDone: () => void; setResult: (r: ActionRun | null) => void
}) {
  const t = useT()
  const qc = useQueryClient()
  const run = useMutation({
    mutationFn: ({ action, asked }: { action: TableAction; asked: Record<string, unknown> }) =>
      api.runAction(conn, meta.schema, meta.name, recordKey, action.name, asked),
    onSuccess: (data, { action }) => {
      setResult({ action, data })
      qc.invalidateQueries({ queryKey: ['rows', conn, meta.schema, meta.name] })
      onDone()
    },
    onError: e => alert((e instanceof ApiError && Object.values(e.fieldErrors)[0]) || (e as Error).message),
  })

  if (!meta.canExecute || !meta.actions?.length) return null

  function start(action: TableAction) {
    const asked: Record<string, unknown> = {}
    for (const [name, p] of Object.entries(action.parameters ?? {})) {
      if (!p.ask) continue
      const v = window.prompt(t('Valor para {param}:', { param: name }))
      if (v === null) return // canceló
      asked[name] = v
    }
    if (action.confirm && !window.confirm(action.confirm)) return
    setResult(null)
    run.mutate({ action, asked })
  }

  return (
    <>
      {meta.actions.map(a => (
        <button key={a.name} type="button" disabled={run.isPending} onClick={() => start(a)} title={a.routine}>
          <Play size={14} aria-hidden />{actionLabel(a)}
        </button>
      ))}
    </>
  )
}

const actionLabel = (a: TableAction) => labelOf({ name: a.name, label: a.label ?? null, labels: a.labels })

/** Lo que devolvió la última acción (P-061), debajo del encabezado del formulario. */
export function ActionResultCard({ result, onClose }: { result: ActionRun | null; onClose: () => void }) {
  const t = useT()
  if (!result) return null
  const columns = result.data.rows.length ? Object.keys(result.data.rows[0]) : []
  return (
    <>
      {(
        <div className="card action-result">
          <div className="row">
            <strong className="with-icon"><CircleCheck size={16} aria-hidden />{t('{action}: listo', { action: actionLabel(result.action) })}</strong>
            <button type="button" className="link" onClick={onClose}>{t('Cerrar')}</button>
          </div>
          {columns.length > 0 && (
            <div className="table-wrap">
              <table className="grid">
                <thead><tr>{columns.map(c => <th key={c}>{c}</th>)}</tr></thead>
                <tbody>
                  {result.data.rows.map((r, i) => (
                    <tr key={i}>{columns.map(c => <td key={c}>{displayValue({ type: 'String' } as never, r[c])}</td>)}</tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {result.data.truncated && <span className="hint">{t('Se muestran las primeras 200 filas.')}</span>}
        </div>
      )}
    </>
  )
}
