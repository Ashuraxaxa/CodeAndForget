import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, isConcurrencyError } from '../api/client'
import { LABEL_SUFFIX, SIZE_SUFFIX, VERSION_FIELD, labelOf, type Column, type LookupItem, type Row, type TableMeta } from '../api/types'
import { FieldControl } from '../components/FieldControl'
import { fromInputValue, keyQuery, parseRecordSearch, toInputValue } from '../lib/format'
import { ChildTabs } from '../components/ChildTabs'
import { ManyToManySelect } from '../components/ManyToManySelect'
import { FileField } from '../components/FileField'
import { useCan } from '../lib/useMe'

/** Formulario de alta y edición generado desde la metadata de la tabla. */
export function RecordPage({ mode }: { mode: 'new' | 'edit' }) {
  const { conn = '', schema = '', table = '' } = useParams()
  const location = useLocation()
  // La query string trae la clave del registro y, desde una pestaña hija, valores fijos y a dónde volver (P-014).
  const { key, fixed, returnTo } = parseRecordSearch(location.search)

  const meta = useQuery({ queryKey: ['meta', conn, schema, table], queryFn: () => api.table(conn, schema, table) })
  const row = useQuery({
    queryKey: ['row', conn, schema, table, key],
    queryFn: () => api.get(conn, schema, table, key),
    enabled: mode === 'edit',
    gcTime: 0,
  })

  // Valores vinculados de cada relación N:M del registro (P-015).
  const links = useQueries({
    queries: (meta.data?.manyToMany ?? []).map(l => ({
      queryKey: ['links', conn, schema, table, key, l.name],
      queryFn: () => api.links(conn, schema, table, key, l.name),
      enabled: mode === 'edit',
      gcTime: 0,
    })),
  })

  if (meta.isLoading || row.isLoading || links.some(l => l.isLoading)) return <p className="muted">Cargando…</p>
  const error = meta.error ?? row.error ?? links.find(l => l.error)?.error
  if (error) return <div className="alert">{(error as Error).message}</div>
  const initialLinks = Object.fromEntries(meta.data!.manyToMany.map((l, i) => [l.name, links[i]?.data ?? []]))

  return (
    <RecordForm key={location.search} conn={conn} meta={meta.data!} row={mode === 'edit' ? row.data! : null}
      recordKey={key} fixed={fixed} returnTo={returnTo} initialLinks={initialLinks} />
  )
}

function RecordForm({ conn, meta, row, recordKey, fixed, returnTo, initialLinks }: {
  conn: string
  meta: TableMeta
  row: Row | null
  recordKey: string
  /** Columnas que vienen completadas y bloqueadas (la FK al padre, en un alta desde una pestaña hija). */
  fixed: Record<string, string>
  returnTo: string | null
  initialLinks: Record<string, LookupItem[]>
}) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const isEdit = row !== null
  const list = `/c/${conn}/t/${encodeURIComponent(meta.schema)}/${encodeURIComponent(meta.name)}`
  const back = returnTo ?? list
  const children = meta.children ?? meta.referencedBy
  const fkByColumn = new Map(meta.foreignKeys.filter(f => f.columns.length === 1).map(f => [f.columns[0], f]))

  // En alta se ocultan las columnas que genera la BD; en edición se muestran deshabilitadas.
  // Las ocultas por ajuste no se muestran (la PK oculta viaja igual en la clave de la URL).
  const fields = meta.columns.filter(c => c.isSelectable && !c.hidden && (isEdit || !(c.isIdentity || c.isComputed || c.isRowVersion)))
  // Sin permiso para guardar (D-063), el formulario es solo de consulta.
  const viewOnly = isEdit ? !meta.canUpdate : !meta.canCreate
  const canAudit = useCan('ViewAudit')
  const historyParams = new URLSearchParams({ conn, schema: meta.schema, table: meta.name })
  for (const [k, v] of new URLSearchParams(recordKey)) historyParams.set(`k.${k}`, v)
  const historyUrl = `/auditoria?${historyParams}`
  const isLocked = (c: Column) => viewOnly || c.isReadOnly || (isEdit && c.isPrimaryKey) || c.name in fixed

  const [initialValues] = useState<Record<string, string | boolean>>(() =>
    Object.fromEntries(fields.map(c => [c.name, c.name in fixed && !isEdit ? fixed[c.name] : toInputValue(c, row?.[c.name])])))
  const [values, setValues] = useState(initialValues)
  const [labels, setLabels] = useState<Record<string, string | null>>(() =>
    Object.fromEntries(fields.map(c => [c.name, (row?.[c.name + LABEL_SUFFIX] as string | null) ?? null])))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [links, setLinks] = useState(initialLinks)
  // Archivos (P-018): no vienen con el registro; solo se envían si se eligió otro o se quitó.
  const files = meta.columns.filter(c => c.isFile && !c.hidden)
  const [fileValues, setFileValues] = useState<Record<string, string | null | undefined>>({})
  const sameLinks = (a: LookupItem[], b: LookupItem[]) =>
    a.map(x => String(x.value)).sort().join('\u0001') === b.map(x => String(x.value)).sort().join('\u0001')

  // Concurrencia optimista (D-069): si otra persona guardó mientras se editaba, el servidor devuelve cómo quedó.
  const [conflict, setConflict] = useState<Record<string, unknown> | null>(null)

  const save = useMutation({
    /** <c>version</c>: la del registro al abrirlo, o la actual si el usuario decide guardar igual tras un conflicto. */
    mutationFn: async (version: unknown = row?.[VERSION_FIELD]) => {
      const body: Row = {}
      for (const c of fields) {
        // Los valores fijos se envían aunque estén bloqueados: son la FK al registro padre.
        if (!isEdit && c.name in fixed) { body[c.name] = fromInputValue(c, fixed[c.name]); continue }
        if (isLocked(c)) continue
        // Al editar se envía solo lo que cambió: un campo sin tocar no pierde precisión (p. ej. los segundos de
        // una fecha, que el control no muestra) ni aparece como cambio en la auditoría.
        if (isEdit && values[c.name] === initialValues[c.name]) continue
        const v = fromInputValue(c, values[c.name])
        // En alta, un vacío en columna con default se omite para que la BD ponga su valor.
        if (!isEdit && v === null && c.hasDefault) continue
        body[c.name] = v
      }
      for (const c of files) {
        if (c.isReadOnly || fileValues[c.name] === undefined) continue
        body[c.name] = fileValues[c.name]
      }
      // Si solo cambiaron vínculos N:M (o nada), el registro no se toca.
      const saved = isEdit && Object.keys(body).length === 0
        ? row
        : await (isEdit
          ? api.update(conn, meta.schema, meta.name, recordKey, body, version)
          : api.create(conn, meta.schema, meta.name, body))
      // Después del registro, las relaciones N:M que cambiaron: cada una se sincroniza en una transacción (P-015).
      const key = isEdit ? recordKey : keyQuery(meta, saved)
      for (const l of meta.manyToMany) {
        const now = links[l.name] ?? []
        if (sameLinks(now, initialLinks[l.name] ?? [])) continue
        try {
          await api.setLinks(conn, meta.schema, meta.name, key, l.name, now.map(v => v.value))
        } catch (e) {
          throw new Error(`El registro se guardó, pero no ${l.targetTable}: ${(e as Error).message}`)
        }
      }
      return saved
    },
    onSuccess: saved => {
      qc.invalidateQueries({ queryKey: ['rows', conn, meta.schema, meta.name] })
      qc.invalidateQueries({ queryKey: ['lookup', conn, meta.schema, meta.name] })
      qc.invalidateQueries({ queryKey: ['tree', conn, meta.schema, meta.name] })
      // Un alta con hijas sigue en el registro creado, para cargar sus hijas sin salir (P-014).
      if (!isEdit && !returnTo && children.length > 0 && meta.hasPrimaryKey && saved)
        navigate(`${list}/edit?${keyQuery(meta, saved)}`, { replace: true })
      else navigate(back)
    },
    onError: err => {
      setErrors(err instanceof ApiError ? err.fieldErrors : {})
      setConflict(isConcurrencyError(err) ? err.current ?? {} : null)
    },
  })

  function submit(e: FormEvent) {
    e.preventDefault()
    if (viewOnly) return
    setErrors({})
    setConflict(null)
    save.mutate(undefined)
  }

  const generalError = save.error && !conflict && Object.keys(errors).length === 0 ? (save.error as Error).message : null
  // Lo que cambió la otra persona, en los campos del formulario: "antes" es lo que se cargó al abrir.
  const conflictFields = conflict
    ? fields.filter(c => toInputValue(c, conflict[c.name]) !== initialValues[c.name])
    : []

  return (
    <div className="page">
      <div className="page-header">
        <h1>{viewOnly && isEdit ? `Registro · ${labelOf(meta)}` : isEdit ? `Editar registro · ${labelOf(meta)}` : `Nuevo registro · ${labelOf(meta)}`}</h1>
        <div className="actions">
          {isEdit && canAudit && <Link to={historyUrl}>Historial</Link>}
          <Link to={back}>{returnTo ? '‹ Volver' : '‹ Volver a la lista'}</Link>
        </div>
      </div>

      <form className="card form" onSubmit={submit}>
        <div className="grid-2">
          {fields.map(c => {
            const required = !isLocked(c) && (isEdit ? !c.isNullable && c.type !== 'Boolean' : c.isRequiredOnInsert)
            const fk = fkByColumn.get(c.name)
            return (
              <label key={c.name} className={c.type === 'Text' || c.type === 'Json' || c.type === 'Array' || c.widget === 'textarea' ? 'span-2' : undefined}>
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
          {files.map(c => {
            const required = !isEdit && c.isRequiredOnInsert
            return (
              <div key={c.name} className="field span-2">
                <span className="field-label">
                  {labelOf(c)}{required && <span className="req"> *</span>}
                  <span className="muted small"> archivo · {c.nativeType}</span>
                </span>
                <FileField conn={conn} schema={meta.schema} table={meta.name} recordKey={isEdit ? recordKey : ''} column={c}
                  size={row?.[c.name + SIZE_SUFFIX]} value={fileValues[c.name]} disabled={viewOnly || c.isReadOnly}
                  onChange={v => setFileValues(s => ({ ...s, [c.name]: v }))} />
                {c.helpText && <span className="hint">{c.helpText}</span>}
                {errors[c.name] && <span className="field-error">{errors[c.name]}</span>}
              </div>
            )
          })}
          {meta.manyToMany.map(l => (
            <label key={l.name} className="span-2">
              <span className="field-label">
                {l.targetTable}<span className="muted small"> · varios, vía {l.bridgeTable}</span>
              </span>
              <ManyToManySelect id={`m2m-${l.name}`} conn={conn} link={l} value={links[l.name] ?? []} disabled={viewOnly}
                onChange={v => setLinks(s => ({ ...s, [l.name]: v }))} />
              {errors[l.name] && <span className="field-error">{errors[l.name]}</span>}
            </label>
          ))}
        </div>

        {(generalError || errors._) && <div className="alert">{generalError ?? errors._}</div>}

        {conflict && (
          <div className="banner conflict">
            <strong>Otra persona cambió este registro mientras lo editaba.</strong>
            {conflictFields.length > 0 ? (
              <ul>
                {conflictFields.map(c => (
                  <li key={c.name}>
                    {labelOf(c)}: ahora <strong>{String(toInputValue(c, conflict[c.name]) || '∅')}</strong>
                    {values[c.name] !== initialValues[c.name] && <> · usted puso <strong>{String(values[c.name] || '∅')}</strong></>}
                  </li>
                ))}
              </ul>
            ) : <p className="small">Cambió algo que este formulario no muestra.</p>}
            <div className="actions">
              <button type="button" onClick={() => window.location.reload()}>Descartar lo mío y ver lo actual</button>
              <button type="button" className="danger" disabled={save.isPending}
                onClick={() => { const v = conflict[VERSION_FIELD]; setConflict(null); save.mutate(v) }}>
                Guardar lo mío igual
              </button>
            </div>
          </div>
        )}

        <div className="actions">
          {!viewOnly && <button className="primary" disabled={save.isPending}>{save.isPending ? 'Guardando…' : 'Guardar'}</button>}
          <Link to={back} className="button">{viewOnly ? 'Volver' : 'Cancelar'}</Link>
        </div>
      </form>

      {isEdit && children.length > 0 && <ChildTabs conn={conn} row={row} relations={children} />}
    </div>
  )
}
