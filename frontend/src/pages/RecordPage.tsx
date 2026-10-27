import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import { LABEL_SUFFIX, labelOf, type Column, type Row, type TableMeta } from '../api/types'
import { FieldControl } from '../components/FieldControl'
import { fromInputValue, toInputValue } from '../lib/format'

/** Formulario de alta y edición generado desde la metadata de la tabla. */
export function RecordPage({ mode }: { mode: 'new' | 'edit' }) {
  const { conn = '', schema = '', table = '' } = useParams()
  const key = useLocation().search.replace(/^\?/, '')

  const meta = useQuery({ queryKey: ['meta', conn, schema, table], queryFn: () => api.table(conn, schema, table) })
  const row = useQuery({
    queryKey: ['row', conn, schema, table, key],
    queryFn: () => api.get(conn, schema, table, key),
    enabled: mode === 'edit',
    gcTime: 0,
  })

  if (meta.isLoading || row.isLoading) return <p className="muted">Cargando…</p>
  const error = meta.error ?? row.error
  if (error) return <div className="alert">{(error as Error).message}</div>

  return <RecordForm conn={conn} meta={meta.data!} row={mode === 'edit' ? row.data! : null} recordKey={key} />
}

function RecordForm({ conn, meta, row, recordKey }: { conn: string; meta: TableMeta; row: Row | null; recordKey: string }) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const isEdit = row !== null
  const back = `/c/${conn}/t/${encodeURIComponent(meta.schema)}/${encodeURIComponent(meta.name)}`
  const fkByColumn = new Map(meta.foreignKeys.filter(f => f.columns.length === 1).map(f => [f.columns[0], f]))

  // En alta se ocultan las columnas que genera la BD; en edición se muestran deshabilitadas.
  // Las ocultas por ajuste no se muestran (la PK oculta viaja igual en la clave de la URL).
  const fields = meta.columns.filter(c => c.isSelectable && !c.hidden && (isEdit || !(c.isIdentity || c.isComputed || c.isRowVersion)))
  const isLocked = (c: Column) => c.isReadOnly || (isEdit && c.isPrimaryKey)

  const [values, setValues] = useState<Record<string, string | boolean>>(() =>
    Object.fromEntries(fields.map(c => [c.name, toInputValue(c, row?.[c.name])])))
  const [labels, setLabels] = useState<Record<string, string | null>>(() =>
    Object.fromEntries(fields.map(c => [c.name, (row?.[c.name + LABEL_SUFFIX] as string | null) ?? null])))
  const [errors, setErrors] = useState<Record<string, string>>({})

  const save = useMutation({
    mutationFn: () => {
      const body: Row = {}
      for (const c of fields) {
        if (isLocked(c)) continue
        const v = fromInputValue(c, values[c.name])
        // En alta, un vacío en columna con default se omite para que la BD ponga su valor.
        if (!isEdit && v === null && c.hasDefault) continue
        body[c.name] = v
      }
      return isEdit
        ? api.update(conn, meta.schema, meta.name, recordKey, body)
        : api.create(conn, meta.schema, meta.name, body)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['rows', conn, meta.schema, meta.name] })
      qc.invalidateQueries({ queryKey: ['lookup', conn, meta.schema, meta.name] })
      navigate(back)
    },
    onError: err => setErrors(err instanceof ApiError ? err.fieldErrors : {}),
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    setErrors({})
    save.mutate()
  }

  const generalError = save.error && Object.keys(errors).length === 0 ? (save.error as Error).message : null

  return (
    <div className="page">
      <div className="page-header">
        <h1>{isEdit ? `Editar registro · ${labelOf(meta)}` : `Nuevo registro · ${labelOf(meta)}`}</h1>
        <Link to={back}>‹ Volver a la lista</Link>
      </div>

      <form className="card form" onSubmit={submit}>
        <div className="grid-2">
          {fields.map(c => {
            const required = !isLocked(c) && (isEdit ? !c.isNullable && c.type !== 'Boolean' : c.isRequiredOnInsert)
            const fk = fkByColumn.get(c.name)
            return (
              <label key={c.name} className={c.type === 'Text' || c.type === 'Json' || c.widget === 'textarea' ? 'span-2' : undefined}>
                <span className="field-label" title={c.label ? c.name : undefined}>
                  {labelOf(c)}{required && <span className="req"> *</span>}
                  <span className="muted small"> {fk ? `→ ${fk.refTable}` : c.nativeType}</span>
                </span>
                <FieldControl
                  conn={conn}
                  column={c}
                  fk={fk}
                  value={values[c.name]}
                  fkLabel={labels[c.name]}
                  disabled={isLocked(c)}
                  required={required}
                  onChange={(v, l) => {
                    setValues(s => ({ ...s, [c.name]: v }))
                    if (l !== undefined) setLabels(s => ({ ...s, [c.name]: l }))
                  }}
                />
                {c.helpText && <span className="hint">{c.helpText}</span>}
                {c.isComputed && <span className="hint">Calculada por la base de datos</span>}
                {errors[c.name] && <span className="field-error">{errors[c.name]}</span>}
              </label>
            )
          })}
        </div>

        {(generalError || errors._) && <div className="alert">{generalError ?? errors._}</div>}

        <div className="actions">
          <button className="primary" disabled={save.isPending}>{save.isPending ? 'Guardando…' : 'Guardar'}</button>
          <Link to={back} className="button">Cancelar</Link>
        </div>
      </form>
    </div>
  )
}
