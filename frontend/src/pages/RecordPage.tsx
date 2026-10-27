import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import { LABEL_SUFFIX, labelOf, type Column, type LookupItem, type Row, type TableMeta } from '../api/types'
import { FieldControl } from '../components/FieldControl'
import { fromInputValue, keyQuery, parseRecordSearch, toInputValue } from '../lib/format'
import { ChildTabs } from '../components/ChildTabs'
import { ManyToManySelect } from '../components/ManyToManySelect'

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
  const isLocked = (c: Column) => c.isReadOnly || (isEdit && c.isPrimaryKey) || c.name in fixed

  const [values, setValues] = useState<Record<string, string | boolean>>(() =>
    Object.fromEntries(fields.map(c => [c.name, c.name in fixed && !isEdit ? fixed[c.name] : toInputValue(c, row?.[c.name])])))
  const [labels, setLabels] = useState<Record<string, string | null>>(() =>
    Object.fromEntries(fields.map(c => [c.name, (row?.[c.name + LABEL_SUFFIX] as string | null) ?? null])))
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [links, setLinks] = useState(initialLinks)
  const sameLinks = (a: LookupItem[], b: LookupItem[]) =>
    a.map(x => String(x.value)).sort().join('\u0001') === b.map(x => String(x.value)).sort().join('\u0001')

  const save = useMutation({
    mutationFn: async () => {
      const body: Row = {}
      for (const c of fields) {
        // Los valores fijos se envían aunque estén bloqueados: son la FK al registro padre.
        if (!isEdit && c.name in fixed) { body[c.name] = fromInputValue(c, fixed[c.name]); continue }
        if (isLocked(c)) continue
        const v = fromInputValue(c, values[c.name])
        // En alta, un vacío en columna con default se omite para que la BD ponga su valor.
        if (!isEdit && v === null && c.hasDefault) continue
        body[c.name] = v
      }
      const saved = await (isEdit
        ? api.update(conn, meta.schema, meta.name, recordKey, body)
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
      // Un alta con hijas sigue en el registro creado, para cargar sus hijas sin salir (P-014).
      if (!isEdit && !returnTo && children.length > 0 && meta.hasPrimaryKey && saved)
        navigate(`${list}/edit?${keyQuery(meta, saved)}`, { replace: true })
      else navigate(back)
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
        <Link to={back}>{returnTo ? '‹ Volver' : '‹ Volver a la lista'}</Link>
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
          {meta.manyToMany.map(l => (
            <label key={l.name} className="span-2">
              <span className="field-label">
                {l.targetTable}<span className="muted small"> · varios, vía {l.bridgeTable}</span>
              </span>
              <ManyToManySelect id={`m2m-${l.name}`} conn={conn} link={l} value={links[l.name] ?? []}
                onChange={v => setLinks(s => ({ ...s, [l.name]: v }))} />
              {errors[l.name] && <span className="field-error">{errors[l.name]}</span>}
            </label>
          ))}
        </div>

        {(generalError || errors._) && <div className="alert">{generalError ?? errors._}</div>}

        <div className="actions">
          <button className="primary" disabled={save.isPending}>{save.isPending ? 'Guardando…' : 'Guardar'}</button>
          <Link to={back} className="button">Cancelar</Link>
        </div>
      </form>

      {isEdit && children.length > 0 && <ChildTabs conn={conn} row={row} relations={children} />}
    </div>
  )
}
