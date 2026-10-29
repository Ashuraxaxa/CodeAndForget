import { ArrowLeft, History } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError, isConcurrencyError } from '../api/client'
import { LABEL_SUFFIX, SIZE_SUFFIX, VERSION_FIELD, helpTextOf, labelOf, selectorLinks, type Column, type ForeignKey, type LookupItem, type Row, type TableMeta } from '../api/types'
import { FieldControl } from '../components/FieldControl'
import { CompositeForeignKeySelect } from '../components/ForeignKeySelect'
import { fromInputValue, keyQuery, parseRecordSearch, toInputValue } from '../lib/format'
import { ChildTabs } from '../components/ChildTabs'
import { ManyToManySelect } from '../components/ManyToManySelect'
import { FileField } from '../components/FileField'
import { useCan } from '../lib/useMe'
import { useTableLabels } from '../lib/useTableLabels'
import { ActionResultCard, RecordActions, type ActionRun } from '../components/RecordActions'
import { useT } from '../i18n'

/** Formulario de alta y edición generado desde la metadata de la tabla. */
export function RecordPage({ mode }: { mode: 'new' | 'edit' }) {
  const { conn = '', schema = '', table = '' } = useParams()
  const location = useLocation()
  const t = useT()
  // La query string trae la clave del registro y, desde una pestaña hija, valores fijos y a dónde volver (P-014).
  const { key, fixed, returnTo } = parseRecordSearch(location.search)

  const meta = useQuery({ queryKey: ['meta', conn, schema, table], queryFn: () => api.table(conn, schema, table) })
  const [reloads, setReloads] = useState(0)
  const [actionRun, setActionRun] = useState<ActionRun | null>(null)
  const row = useQuery({
    queryKey: ['row', conn, schema, table, key],
    queryFn: () => api.get(conn, schema, table, key),
    enabled: mode === 'edit',
    gcTime: 0,
  })

  // Valores vinculados de cada relación N:M del registro (P-015).
  const links = useQueries({
    queries: (meta.data ? selectorLinks(meta.data) : []).map(l => ({
      queryKey: ['links', conn, schema, table, key, l.name],
      queryFn: () => api.links(conn, schema, table, key, l.name),
      enabled: mode === 'edit',
      gcTime: 0,
    })),
  })

  if (meta.isLoading || row.isLoading || links.some(l => l.isLoading)) return <p className="muted">{t('Cargando…')}</p>
  const error = meta.error ?? row.error ?? links.find(l => l.error)?.error
  if (error) return <div className="alert">{(error as Error).message}</div>
  const initialLinks = Object.fromEntries(selectorLinks(meta.data!).map((l, i) => [l.name, links[i]?.data ?? []]))

  // Una acción (P-061) puede cambiar el registro: se vuelve a leer y el formulario se rearma con lo actual.
  const reload = () => { row.refetch().then(() => setReloads(n => n + 1)) }
  return (
    <RecordForm key={`${location.search}#${reloads}`} conn={conn} onReload={reload} actionRun={actionRun} setActionRun={setActionRun} meta={meta.data!} row={mode === 'edit' ? row.data! : null}
      recordKey={key} fixed={fixed} returnTo={returnTo} initialLinks={initialLinks} />
  )
}

function RecordForm({ conn, meta, row, recordKey, fixed, returnTo, initialLinks, onReload, actionRun, setActionRun }: {
  conn: string
  onReload: () => void
  actionRun: ActionRun | null
  setActionRun: (r: ActionRun | null) => void
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
  const t = useT()
  const isEdit = row !== null
  const list = `/c/${conn}/t/${encodeURIComponent(meta.schema)}/${encodeURIComponent(meta.name)}`
  const back = returnTo ?? list
  const children = meta.children ?? meta.referencedBy
  const fkByColumn = new Map(meta.foreignKeys.filter(f => f.columns.length === 1).map(f => [f.columns[0], f]))
  // FK compuesta (P-030): un solo combo, en su columna de etiqueta, llena todas sus columnas; las demás no se muestran.
  const compositeByColumn = new Map(meta.foreignKeys.filter(f => f.columns.length > 1).map(f => [f.labelColumn, f]))
  const compositeMembers = new Set(meta.foreignKeys.filter(f => f.columns.length > 1)
    .flatMap(f => f.columns.filter(c => c !== f.labelColumn)))
  const tableLabel = useTableLabels(conn)

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

  // Combos filtrados por otra columna (P-110): AreaID solo ofrece áreas de la EmpresaID elegida.
  const fieldName = (name: string) => { const f = meta.columns.find(x => x.name === name); return f ? labelOf(f) : name }
  /** Filtros del combo con los valores del registro; null si falta alguno (el combo queda bloqueado). */
  const equalsFor = (fk: ForeignKey | undefined): Record<string, string> | null | undefined => {
    if (!fk?.lookupFilters?.length) return undefined
    const out: Record<string, string> = {}
    for (const f of fk.lookupFilters) {
      const v = values[f.localColumn]
      if (v === undefined || v === '') return null
      out[f.refColumn] = String(v)
    }
    return out
  }
  const blockedText = (fk: ForeignKey | undefined) =>
    t('Elija primero {field}', { field: (fk?.lookupFilters ?? []).map(f => fieldName(f.localColumn)).join(', ') })
  /** Cambia campos y, si cambió algo que filtra un combo, vacía ese combo: lo elegido puede no corresponder. */
  function change(changes: Record<string, string | boolean>, label?: { column: string; text: string | null }) {
    const cleared = meta.foreignKeys.filter(fk => fk.lookupFilters?.some(f => f.localColumn in changes && changes[f.localColumn] !== values[f.localColumn]))
    setValues(s => {
      const n = { ...s, ...changes }
      for (const fk of cleared) for (const c of fk.columns) if (!(c in changes)) n[c] = ''
      return n
    })
    setLabels(s => {
      const n = { ...s }
      if (label) n[label.column] = label.text
      for (const fk of cleared) if (!fk.columns.some(c => c in changes)) n[fk.labelColumn] = null
      return n
    })
  }
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
      for (const l of selectorLinks(meta)) {
        const now = links[l.name] ?? []
        if (sameLinks(now, initialLinks[l.name] ?? [])) continue
        try {
          await api.setLinks(conn, meta.schema, meta.name, key, l.name, now.map(v => v.value))
        } catch (e) {
          throw new Error(t('El registro se guardó, pero no {relation}: {error}', { relation: tableLabel(l.targetSchema, l.targetTable), error: (e as Error).message }))
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
        <h1>{viewOnly && isEdit ? t('Registro · {table}', { table: labelOf(meta) }) : isEdit ? t('Editar registro · {table}', { table: labelOf(meta) }) : t('Nuevo registro · {table}', { table: labelOf(meta) })}</h1>
        <div className="actions">
          {isEdit && <RecordActions conn={conn} meta={meta} recordKey={recordKey} onDone={onReload} setResult={setActionRun} />}
          {isEdit && canAudit && <Link className="back-link" to={historyUrl}><History size={16} aria-hidden />{t('Historial')}</Link>}
          <Link className="back-link" to={back}><ArrowLeft size={16} aria-hidden />{returnTo ? t('Volver') : t('Volver a la lista')}</Link>
        </div>
      </div>

      <ActionResultCard result={actionRun} onClose={() => setActionRun(null)} />

      <form className="card form" onSubmit={submit}>
        <div className="grid-2">
          {fields.map(c => {
            const required = !isLocked(c) && (isEdit ? !c.isNullable && c.type !== 'Boolean' : c.isRequiredOnInsert)
            const composite = compositeByColumn.get(c.name)
            if (compositeMembers.has(c.name) && fields.some(f => compositeByColumn.get(f.name)?.columns.includes(c.name))) return null
            if (composite) {
              const cols = composite.columns.map(n => fields.find(f => f.name === n)).filter((x): x is Column => !!x)
              const locked = cols.some(isLocked)
              const anyRequired = cols.some(x => !isLocked(x) && (isEdit ? !x.isNullable : x.isRequiredOnInsert))
              return (
                <label key={c.name}>
                  <span className="field-label">
                    {tableLabel(composite.refSchema, composite.refTable)}{anyRequired && <span className="req"> *</span>}
                    <span className="muted small"> {composite.columns.join(' + ')}</span>
                  </span>
                  <CompositeForeignKeySelect conn={conn} fk={composite} disabled={locked}
                    values={Object.fromEntries(composite.columns.map(n => [n, values[n]]))} label={labels[c.name]}
                    nullable={cols.every(x => x.isNullable)}
                    equals={equalsFor(composite)} blockedText={blockedText(composite)}
                    onChange={(picked, l) => change(Object.fromEntries(composite.columns.map(n => [n, picked[n] ?? ''])), { column: c.name, text: l })} />
                  {composite.columns.map(n => errors[n] && <span key={n} className="field-error">{n}: {errors[n]}</span>)}
                </label>
              )
            }
            const fk = fkByColumn.get(c.name)
            return (
              <label key={c.name} className={c.type === 'Text' || c.type === 'Json' || c.type === 'Array' || c.widget === 'textarea' ? 'span-2' : undefined}>
                <span className="field-label" title={c.label ? c.name : undefined}>
                  {labelOf(c)}{required && <span className="req"> *</span>}
                  <span className="muted small"> {fk ? `→ ${tableLabel(fk.refSchema, fk.refTable)}` : c.nativeType}</span>
                </span>
                <FieldControl
                  conn={conn}
                  column={c}
                  fk={fk}
                  value={values[c.name]}
                  fkLabel={labels[c.name]}
                  disabled={isLocked(c)}
                  required={required}
                  fkEquals={equalsFor(fk)}
                  fkBlockedText={blockedText(fk)}
                  onChange={(v, l) => change({ [c.name]: v }, l !== undefined ? { column: c.name, text: l } : undefined)}
                />
                {helpTextOf(c) && <span className="hint">{helpTextOf(c)}</span>}
                {c.isComputed && <span className="hint">{t('Calculada por la base de datos')}</span>}
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
                  <span className="muted small"> {t('archivo')} · {c.nativeType}</span>
                </span>
                <FileField conn={conn} schema={meta.schema} table={meta.name} recordKey={isEdit ? recordKey : ''} column={c}
                  size={row?.[c.name + SIZE_SUFFIX]} value={fileValues[c.name]} disabled={viewOnly || c.isReadOnly}
                  onChange={v => setFileValues(s => ({ ...s, [c.name]: v }))} />
                {helpTextOf(c) && <span className="hint">{helpTextOf(c)}</span>}
                {errors[c.name] && <span className="field-error">{errors[c.name]}</span>}
              </div>
            )
          })}
          {selectorLinks(meta).map(l => (
            <label key={l.name} className="span-2">
              <span className="field-label">
                {tableLabel(l.targetSchema, l.targetTable)}<span className="muted small"> · {t('varios, vía {bridge}', { bridge: tableLabel(l.bridgeSchema, l.bridgeTable) })}</span>
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
            <strong>{t('Otra persona cambió este registro mientras lo editaba.')}</strong>
            {conflictFields.length > 0 ? (
              <ul>
                {conflictFields.map(c => (
                  <li key={c.name}>
                    {labelOf(c)}: {t('ahora')} <strong>{String(toInputValue(c, conflict[c.name]) || '∅')}</strong>
                    {values[c.name] !== initialValues[c.name] && <> · {t('usted puso')} <strong>{String(values[c.name] || '∅')}</strong></>}
                  </li>
                ))}
              </ul>
            ) : <p className="small">{t('Cambió algo que este formulario no muestra.')}</p>}
            <div className="actions">
              <button type="button" onClick={() => window.location.reload()}>{t('Descartar lo mío y ver lo actual')}</button>
              <button type="button" className="danger" disabled={save.isPending}
                onClick={() => { const v = conflict[VERSION_FIELD]; setConflict(null); save.mutate(v) }}>
                {t('Guardar lo mío igual')}
              </button>
            </div>
          </div>
        )}

        <div className="actions">
          {!viewOnly && <button className="primary" disabled={save.isPending}>{save.isPending ? t('Guardando…') : t('Guardar')}</button>}
          <Link to={back} className="button">{viewOnly ? t('Volver') : t('Cancelar')}</Link>
        </div>
      </form>

      {isEdit && children.length > 0 && <ChildTabs conn={conn} row={row} relations={children} />}
    </div>
  )
}
