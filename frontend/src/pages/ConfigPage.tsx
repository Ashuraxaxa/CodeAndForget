import { useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import {
  WIDGETS, labelOf,
  type Column, type ColumnOverride, type ForeignKey, type LinkDisplay, type NotifyOperation, type NotifyOverride, type TableMeta, type TableOverride,
  type VirtualForeignKey,
} from '../api/types'
import { FieldControl } from '../components/FieldControl'
import { useDebounce } from '../lib/useDebounce'
import { useMe } from '../lib/useMe'
import { useT, type Translate } from '../i18n'

/** Editor de ajustes de un mantenedor (P-012): sin editar JSON, con vista previa en vivo. Requiere el permiso ConfigureTables (D-053). */
export function ConfigPage() {
  const { conn = '', schema = '', table = '' } = useParams()
  const me = useMe()
  const t = useT()
  const current = useQuery({
    queryKey: ['override', conn, schema, table],
    queryFn: () => api.override(conn, schema, table),
    enabled: me.data?.permissions.includes('ConfigureTables') === true,
    gcTime: 0,
  })
  // La primera vista previa da las columnas tal como quedan con el ajuste guardado.
  const initial = useQuery({
    queryKey: ['override-initial', conn, schema, table],
    queryFn: () => api.previewOverride(conn, schema, table, current.data!),
    enabled: current.data !== undefined,
    gcTime: 0,
  })

  if (me.isLoading) return <p className="muted">{t('Cargando…')}</p>
  if (!me.data?.permissions.includes('ConfigureTables'))
    return <div className="alert">{t('No tiene permiso para configurar mantenedores (pídale a un administrador el rol correspondiente).')}</div>
  const error = current.error ?? initial.error
  if (error) return <div className="alert">{(error as Error).message}</div>
  if (!initial.data) return <p className="muted">{t('Cargando…')}</p>

  return <Editor conn={conn} initialDoc={current.data!} initialTable={initial.data.table} />
}

type Tab = 'table' | 'columns'

function Editor({ conn, initialDoc, initialTable }: { conn: string; initialDoc: TableOverride; initialTable: TableMeta }) {
  const navigate = useNavigate()
  const qc = useQueryClient()
  const t = useT()
  const { schema, name } = initialTable
  const back = `/c/${conn}/t/${encodeURIComponent(schema)}/${encodeURIComponent(name)}`

  const [tab, setTab] = useState<Tab>('table')
  const [doc, setDoc] = useState<TableOverride>(initialDoc)
  const [order, setOrder] = useState<string[]>(() => initialTable.columns.map(c => c.name))
  const byName = useMemo(() => new Map(initialTable.columns.map(c => [c.name, c])), [initialTable])
  const naturalOrder = useMemo(
    () => [...initialTable.columns].sort((a, b) => a.ordinal - b.ordinal).map(c => c.name), [initialTable])

  const built = useMemo(() => buildDoc(doc, order, naturalOrder), [doc, order, naturalOrder])
  const debounced = useDebounce(built, 400)
  const preview = useQuery({
    queryKey: ['override-preview', conn, schema, name, JSON.stringify(debounced)],
    queryFn: () => api.previewOverride(conn, schema, name, debounced),
    placeholderData: keepPreviousData,
  })
  const errors = Object.fromEntries(Object.entries(preview.data?.errors ?? {}).map(([k, v]) => [k, v.join(' ')]))
  const shown = preview.data?.table ?? initialTable

  function done() {
    qc.invalidateQueries({ queryKey: ['meta', conn] })
    qc.invalidateQueries({ queryKey: ['tables', conn] })
    qc.invalidateQueries({ queryKey: ['rows', conn] })
    qc.invalidateQueries({ queryKey: ['lookup', conn] })
    qc.invalidateQueries({ queryKey: ['diff', conn] })
    navigate(back)
  }

  const save = useMutation({ mutationFn: () => api.saveOverride(conn, schema, name, built), onSuccess: done })
  const reset = useMutation({
    mutationFn: async () => {
      try { await api.resetOverride(conn, schema, name) }
      catch (e) { if (!(e instanceof ApiError && e.status === 404)) throw e } // ya estaba en automático
    },
    onSuccess: done,
  })
  const saveErrors = save.error instanceof ApiError ? save.error.fieldErrors : {}
  const allErrors = { ...errors, ...saveErrors }

  // Ajustes guardados para columnas que la base ya no tiene (P-013).
  const orphans = Object.keys(doc.columns ?? {}).filter(n => !byName.has(n))
  const removeCol = (col: string) =>
    setDoc(d => ({ ...d, columns: Object.fromEntries(Object.entries(d.columns ?? {}).filter(([k]) => k !== col)) }))

  const setCol = (col: string, patch: Partial<ColumnOverride>) =>
    setDoc(d => ({ ...d, columns: { ...d.columns, [col]: { ...d.columns?.[col], ...patch } } }))

  // Arrastrar para ordenar (P-085): se suelta una columna sobre otra y toma su lugar.
  const [dragging, setDragging] = useState<string | null>(null)
  function moveTo(col: string, target: string) {
    setOrder(o => {
      const n = o.filter(x => x !== col)
      n.splice(o.indexOf(target), 0, col)
      return n
    })
  }

  function move(col: string, delta: number) {
    setOrder(o => {
      const i = o.indexOf(col), j = i + delta
      if (j < 0 || j >= o.length) return o
      const n = [...o]
      ;[n[i], n[j]] = [n[j], n[i]]
      return n
    })
  }

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1>{t('Configurar · {table}', { table: labelOf(shown) })}</h1>
          <div className="muted small">{schema}.{name} · {t('los cambios se ven en la vista previa antes de guardar')}</div>
        </div>
        <Link to={back}>‹ {t('Volver a la lista')}</Link>
      </div>

      <div className="tabs">
        <button className={tab === 'table' ? 'tab active' : 'tab'} onClick={() => setTab('table')}>{t('Tabla')}</button>
        <button className={tab === 'columns' ? 'tab active' : 'tab'} onClick={() => setTab('columns')}>
          {t('Columnas ({n})', { n: order.length })}
        </button>
      </div>

      <div className="config-layout">
        <div className="card form">
          {tab === 'table'
            ? <TableTab conn={conn} doc={doc} setDoc={setDoc} table={initialTable} shown={shown} errors={allErrors} />
            : (
              <div className="col-list">
                {order.map((col, i) => (
                  <ColumnEditor
                    key={col}
                    column={byName.get(col)!}
                    value={doc.columns?.[col] ?? {}}
                    errors={allErrors}
                    first={i === 0}
                    last={i === order.length - 1}
                    onMove={d => move(col, d)}
                    onChange={p => setCol(col, p)}
                    dragging={dragging}
                    onDragStart={() => setDragging(col)}
                    onDragEnd={() => setDragging(null)}
                    onDropOn={() => { if (dragging && dragging !== col) moveTo(dragging, col); setDragging(null) }}
                  />
                ))}
                {order.join() !== naturalOrder.join() && (
                  <button className="link" onClick={() => setOrder(naturalOrder)}>{t('Volver al orden de la tabla')}</button>
                )}
                {orphans.length > 0 && (
                  <div className="alert">
                    <b>{t('Ajustes de columnas que ya no existen en la base')}</b> {t('(se ignoran; quítelos para poder guardar):')}
                    {orphans.map(col => (
                      <div key={col} className="row">
                        <code>{col}</code>
                        <button className="link danger" onClick={() => removeCol(col)}>{t('Quitar')}</button>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}

          {Object.keys(allErrors).length > 0 && (
            <div className="alert">
              {Object.entries(allErrors).map(([k, v]) => <div key={k}><b>{fieldName(k)}:</b> {v}</div>)}
            </div>
          )}
          {save.error && !(save.error instanceof ApiError && Object.keys(saveErrors).length > 0) && (
            <div className="alert">{(save.error as Error).message}</div>
          )}

          <div className="actions">
            <button className="primary" disabled={save.isPending} onClick={() => save.mutate()}>
              {save.isPending ? t('Guardando…') : t('Guardar')}
            </button>
            <Link to={back} className="button">{t('Cancelar')}</Link>
            <button
              className="link danger"
              disabled={reset.isPending}
              onClick={() => confirm(t('¿Quitar todos los ajustes de esta tabla y volver a lo automático?')) && reset.mutate()}
            >
              {t('Restablecer a lo automático')}
            </button>
          </div>
        </div>

        <Preview conn={conn} table={shown} loading={preview.isFetching} />
      </div>
    </div>
  )
}

// ---------- Pestaña "Tabla" ----------

function TableTab({ conn, doc, setDoc, table, shown, errors }: {
  conn: string
  doc: TableOverride
  setDoc: (f: (d: TableOverride) => TableOverride) => void
  table: TableMeta
  shown: TableMeta
  errors: Record<string, string>
}) {
  const t = useT()
  const set = (patch: Partial<TableOverride>) => setDoc(d => ({ ...d, ...patch }))
  const visible = table.columns.filter(c => c.type !== 'Binary' && c.type !== 'Unsupported')
  const [sortCol, sortDesc] = parseSort(doc.defaultSort)
  const declaredPk = table.hasPrimaryKey && !table.hasVirtualPrimaryKey
  const vpk = doc.virtualPrimaryKey ?? []
  // Relaciones de la tabla consigo misma que pueden armar el árbol (P-089).
  const selfRefs = table.primaryKey.length === 1
    ? table.foreignKeys.filter(f => f.columns.length === 1 && f.refSchema === table.schema && f.refTable === table.name)
    : []
  const setLink = (name: string, v: LinkDisplay) => {
    const next = { ...doc.links, [name]: v }
    if (v === 'selector') delete next[name]
    set({ links: Object.keys(next).length ? next : undefined })
  }

  return (
    <div className="grid-2">
      <Field label={t('Nombre visible')} hint={t('Por defecto: {value}', { value: table.name })} error={errors.label}>
        <input value={doc.label ?? ''} placeholder={table.name} onChange={e => set({ label: e.target.value })} />
      </Field>
      <Field label={t('Grupo del menú')} hint={t('Por defecto: {value}', { value: table.schema })} error={errors.group}>
        <input value={doc.group ?? ''} placeholder={table.schema} onChange={e => set({ group: e.target.value })} />
      </Field>
      <Field label={t('Columna a mostrar')} hint={t('Se ve en los combos de las tablas que referencian a esta')} error={errors.displayColumn}>
        <select value={doc.displayColumn ?? ''} onChange={e => set({ displayColumn: e.target.value })}>
          <option value="">({t('automática')}{!doc.displayColumn && shown.displayColumn ? `: ${shown.displayColumn}` : ''})</option>
          {visible.map(c => <option key={c.name} value={c.name}>{c.name}</option>)}
        </select>
      </Field>
      <Field label={t('Orden por defecto')} hint={t('Cuando el usuario no eligió otro')} error={errors.defaultSort}>
        <div className="row">
          <select value={sortCol} onChange={e => set({ defaultSort: e.target.value ? (sortDesc ? '-' : '') + e.target.value : '' })}>
            <option value="">{t('(por clave)')}</option>
            {table.columns.filter(c => c.isSortable).map(c => <option key={c.name} value={c.name}>{c.name}</option>)}
          </select>
          <select value={sortDesc ? 'desc' : 'asc'} disabled={!sortCol}
            onChange={e => set({ defaultSort: (e.target.value === 'desc' ? '-' : '') + sortCol })}>
            <option value="asc">{t('ascendente')}</option>
            <option value="desc">{t('descendente')}</option>
          </select>
        </div>
      </Field>
      <Check label={t('Ocultar del menú')} hint={t('No es un permiso: se sigue pudiendo abrir por su dirección')}
        checked={doc.hidden === true} onChange={v => set({ hidden: v || undefined })} />
      <Check label={t('Solo lectura')} hint={t('No se puede crear, editar ni borrar')}
        checked={doc.readOnly === true} onChange={v => set({ readOnly: v || undefined })} />

      {!declaredPk && (
        <Field label={t('Clave virtual')} span hint={t('La base no declara PK: marque las columnas que identifican una sola fila')} error={errors.virtualPrimaryKey}>
          <div className="checks">
            {visible.map(c => (
              <label key={c.name} className="check">
                <input type="checkbox" checked={vpk.includes(c.name)}
                  onChange={e => set({ virtualPrimaryKey: e.target.checked ? [...vpk, c.name] : vpk.filter(x => x !== c.name) })} />
                {c.name}
              </label>
            ))}
          </div>
        </Field>
      )}

      {shown.referencedBy.length > 0 && (
        <ChildrenPicker all={shown.referencedBy} value={doc.children} error={errors.children} onChange={v => set({ children: v })} />
      )}

      {selfRefs.length > 1 && (
        <Field label={t('Relación del árbol')} hint={t('Cuál de las relaciones de la tabla consigo misma arma la vista de árbol')} error={errors.treeParent}>
          <select value={doc.treeParent ?? ''} onChange={e => set({ treeParent: e.target.value || undefined })}>
            <option value="">({t('automática')}: {selfRefs[0].columns.join(', ')})</option>
            {selfRefs.map(f => <option key={f.name} value={f.name}>{f.columns.join(', ')} ({f.name})</option>)}
          </select>
        </Field>
      )}

      {shown.manyToMany.length > 0 && (
        <Field label={t('Relaciones N:M')} span error={errors.links}
          hint={t('Selector: se eligen varios en el formulario. Pestaña: la tabla puente con su propio formulario, útil si tiene columnas propias (p. ej. cantidad).')}>
          {shown.manyToMany.map(l => (
            <div key={l.name} className="row">
              <span>{l.targetSchema}.{l.targetTable} <span className="muted small">· {t('vía {bridge}', { bridge: l.bridgeTable })}</span></span>
              <select value={doc.links?.[l.name] ?? 'selector'} onChange={e => setLink(l.name, e.target.value as LinkDisplay)}>
                <option value="selector">{t('Selector múltiple')}</option>
                <option value="tab">{t('Pestaña')}</option>
                <option value="none">{t('No mostrar')}</option>
              </select>
            </div>
          ))}
        </Field>
      )}

      <VirtualForeignKeys conn={conn} table={table} value={doc.virtualForeignKeys ?? []} errors={errors}
        onChange={v => set({ virtualForeignKeys: v })} />

      <NotifyEditor value={doc.notify} errors={errors} onChange={v => set({ notify: v })} />
    </div>
  )
}

const notifyOps = (t: Translate): { op: NotifyOperation; label: string }[] => [
  { op: 'create', label: t('Altas') }, { op: 'update', label: t('Ediciones') }, { op: 'delete', label: t('Bajas') },
  { op: 'import', label: t('Importaciones') }, { op: 'links', label: t('Vínculos N:M') },
]
const DEFAULT_NOTIFY: NotifyOperation[] = ['create', 'update', 'delete']

/** Avisos por correo para tablas críticas (D-073). Apagado mientras no haya destinatarios. */
function NotifyEditor({ value, errors, onChange }: {
  value: NotifyOverride | undefined
  errors: Record<string, string>
  onChange: (v: NotifyOverride | undefined) => void
}) {
  const t = useT()
  const status = useQuery({ queryKey: ['notifications-status'], queryFn: api.notificationsStatus, staleTime: 60_000 })
  const [emails, setEmails] = useState((value?.emails ?? []).join(', '))
  const on = value?.on?.length ? value.on : DEFAULT_NOTIFY
  const update = (patch: Partial<NotifyOverride>) => {
    const next = { ...value, ...patch }
    onChange(next.emails?.length ? next : undefined)
  }
  return (
    <Field label={t('Avisos por correo')} span
      hint={t('Para tablas críticas: a quién avisar cuando alguien cambia un registro. Vacío = sin avisos.')}
      error={errors['notify.emails'] ?? errors['notify.on']}>
      {status.data && !status.data.enabled && (
        <div className="banner small">{t('El envío de correos no está configurado en la app (sección Notifications): los avisos se guardan pero no se envían.')}</div>
      )}
      <input value={emails} placeholder="jefa@empresa.cl, ti@empresa.cl"
        onChange={e => setEmails(e.target.value)}
        onBlur={() => update({ emails: emails.split(/[,;\s]+/).map(x => x.trim()).filter(Boolean) })} />
      <div className="checks">
        {notifyOps(t).map(({ op, label }) => (
          <label key={op} className="check">
            <input type="checkbox" checked={on.includes(op)} disabled={!value?.emails?.length}
              onChange={e => update({ on: e.target.checked ? [...on, op] : on.filter(x => x !== op) })} />
            {label}
          </label>
        ))}
        <label className="check">
          <input type="checkbox" checked={value?.includeValues !== false} disabled={!value?.emails?.length}
            onChange={e => update({ includeValues: e.target.checked ? undefined : false })} />
          {t('Incluir los valores antes y después')}
        </label>
      </div>
    </Field>
  )
}

function VirtualForeignKeys({ conn, table, value, errors, onChange }: {
  conn: string
  table: TableMeta
  value: VirtualForeignKey[]
  errors: Record<string, string>
  onChange: (v: VirtualForeignKey[]) => void
}) {
  const t = useT()
  // Una o más parejas columna local → columna referenciada (P-085: también compuestas, p. ej. país + código).
  const [pairs, setPairs] = useState<{ col: string; refCol: string }[]>([{ col: '', refCol: '' }])
  const [ref, setRef] = useState('') // "esquema.tabla"
  const tables = useQuery({ queryKey: ['tables', conn], queryFn: () => api.tables(conn) })
  const [refSchema, refTable] = splitRef(ref, tables.data?.tables)
  const target = useQuery({
    queryKey: ['meta', conn, refSchema, refTable],
    queryFn: () => api.table(conn, refSchema, refTable),
    enabled: !!refTable,
  })
  const declared = new Set(table.foreignKeys.filter(f => !value.some(v => v.columns.join() === f.columns.join())).flatMap(f => f.columns))
  const setPair = (i: number, p: Partial<{ col: string; refCol: string }>) => setPairs(s => s.map((x, j) => (j === i ? { ...x, ...p } : x)))
  const complete = refTable !== '' && pairs.every(p => p.col && p.refCol) && new Set(pairs.map(p => p.col)).size === pairs.length

  function chooseTable(key: string) {
    setRef(key)
    // Con una tabla elegida, se proponen sus columnas de PK (una pareja por cada una).
    setPairs([{ col: '', refCol: '' }])
  }
  // Al cargar la tabla elegida, una pareja por columna de su PK.
  const pk = target.data?.primaryKey ?? []
  if (target.data && pairs.length === 1 && !pairs[0].refCol && pk.length > 0)
    setPairs(pk.map(k => ({ col: '', refCol: k })))

  function add() {
    onChange([...value, { columns: pairs.map(p => p.col), refSchema, refTable, refColumns: pairs.map(p => p.refCol) }])
    setRef(''); setPairs([{ col: '', refCol: '' }])
  }

  return (
    <Field label={t('Relaciones virtuales')} span hint={t('Relaciones que existen pero la base no declara: dan combo y etiqueta como una FK real')}>
      {value.map((v, i) => (
        <div key={i} className="row">
          <span className="chip">{v.columns.join(', ')} → {v.refSchema}.{v.refTable} ({v.refColumns.join(', ')})</span>
          <button className="link danger" onClick={() => onChange(value.filter((_, j) => j !== i))}>{t('Quitar')}</button>
          {errors[`virtualForeignKeys.${i}`] && <span className="field-error">{errors[`virtualForeignKeys.${i}`]}</span>}
        </div>
      ))}
      <div className="row">
        <select value={ref} onChange={e => chooseTable(e.target.value)}>
          <option value="">{t('Tabla…')}</option>
          {tables.data?.tables.map(x => <option key={`${x.schema}.${x.name}`} value={`${x.schema}.${x.name}`}>{x.schema}.{x.name}</option>)}
        </select>
      </div>
      {pairs.map((p, i) => (
        <div key={i} className="row">
          <select value={p.col} onChange={e => setPair(i, { col: e.target.value })}>
            <option value="">{t('Columna…')}</option>
            {table.columns.filter(c => !declared.has(c.name)).map(c => <option key={c.name} value={c.name}>{c.name}</option>)}
          </select>
          <span>→</span>
          <select value={p.refCol} disabled={!target.data} onChange={e => setPair(i, { refCol: e.target.value })}>
            <option value="">{t('Columna…')}</option>
            {target.data?.columns.map(c => <option key={c.name} value={c.name}>{c.name}{c.isPrimaryKey ? ' (PK)' : ''}</option>)}
          </select>
          {pairs.length > 1 && <button className="link danger" onClick={() => setPairs(s => s.filter((_, j) => j !== i))}>{t('Quitar')}</button>}
        </div>
      ))}
      <div className="row">
        <button className="link" disabled={!target.data} onClick={() => setPairs(s => [...s, { col: '', refCol: '' }])}>＋ {t('Otra columna (relación compuesta)')}</button>
        <button disabled={!complete} onClick={add}>{t('Agregar')}</button>
      </div>
    </Field>
  )
}

/** Tablas hijas en pestañas al editar (P-014): cuáles y en qué orden. Todas y en su orden = automático. */
function ChildrenPicker({ all, value, error, onChange }: {
  all: ForeignKey[]
  value?: string[]
  error?: string
  onChange: (v: string[] | undefined) => void
}) {
  const t = useT()
  const natural = all.map(f => f.name)
  const order = [...(value ?? natural), ...natural.filter(n => value && !value.includes(n))]
  const shown = new Set(value ?? natural)
  const describe = (name: string) => {
    const f = all.find(x => x.name === name)
    return f ? `${f.schema}.${f.table} (${f.columns.join(', ')})` : `${name} (${t('ya no existe')})`
  }
  function emit(nextOrder: string[], nextShown: Set<string>) {
    const picked = nextOrder.filter(n => nextShown.has(n) && natural.includes(n))
    onChange(picked.join() === natural.join() ? undefined : picked)
  }
  function move(i: number, d: number) {
    const n = [...order]
    const j = i + d
    if (j < 0 || j >= n.length) return
    ;[n[i], n[j]] = [n[j], n[i]]
    emit(n, shown)
  }
  return (
    <Field label={t('Tablas hijas')} span hint={t('Se muestran en pestañas al editar un registro')} error={error}>
      {order.map((name, i) => (
        <div key={name} className="row">
          <div className="col-move">
            <button title={t('Subir')} disabled={i === 0} onClick={() => move(i, -1)}>↑</button>
            <button title={t('Bajar')} disabled={i === order.length - 1} onClick={() => move(i, 1)}>↓</button>
          </div>
          <label className="check">
            <input type="checkbox" checked={shown.has(name)} onChange={e => {
              const next = new Set(shown)
              if (e.target.checked) next.add(name); else next.delete(name)
              emit(order, next)
            }} />
            {describe(name)}
          </label>
        </div>
      ))}
    </Field>
  )
}

// ---------- Pestaña "Columnas" ----------

function ColumnEditor({ column: c, value: v, errors, first, last, onMove, onChange, dragging, onDragStart, onDragEnd, onDropOn }: {
  column: Column
  value: ColumnOverride
  errors: Record<string, string>
  first: boolean
  last: boolean
  onMove: (delta: number) => void
  onChange: (patch: Partial<ColumnOverride>) => void
  dragging: string | null
  onDragStart: () => void
  onDragEnd: () => void
  onDropOn: () => void
}) {
  const t = useT()
  const isText = c.type === 'String' || c.type === 'Text'
  const isNumber = (c.type === 'Integer' || c.type === 'Decimal') && !c.isLargeNumber
  const prefix = `columns.${c.name}.`
  const colErrors = Object.entries(errors).filter(([k]) => k === `columns.${c.name}` || k.startsWith(prefix))
  const [open, setOpen] = useState(() => !!(v.widget || v.pattern || v.maxLength != null || v.min != null || v.max != null || v.options?.length))
  const hidden = v.hidden === true

  return (
    <div className={`col-editor${hidden ? ' is-hidden' : ''}${dragging === c.name ? ' dragging' : ''}`}
      onDragOver={e => { if (dragging) e.preventDefault() }} onDrop={e => { e.preventDefault(); onDropOn() }}>
      <div className="col-head">
        <span className="col-grip" draggable title={t('Arrastrar para ordenar')}
          onDragStart={e => { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', c.name); onDragStart() }}
          onDragEnd={onDragEnd}>⠿</span>
        <div className="col-move">
          <button title={t('Subir')} disabled={first} onClick={() => onMove(-1)}>↑</button>
          <button title={t('Bajar')} disabled={last} onClick={() => onMove(1)}>↓</button>
        </div>
        <div className="col-name">
          <b>{c.isPrimaryKey && '🔑 '}{c.name}</b>
          <span className="muted small"> {c.nativeType}{c.isRequiredOnInsert ? ' · ' + t('obligatoria') : ''}</span>
        </div>
        <label className="check"><input type="checkbox" checked={!hidden} onChange={e => onChange({ hidden: e.target.checked ? undefined : true })} />{t('Visible')}</label>
        <label className="check"><input type="checkbox" checked={v.showInGrid !== false} disabled={hidden}
          onChange={e => onChange({ showInGrid: e.target.checked ? undefined : false })} />{t('En la grilla')}</label>
        <label className="check"><input type="checkbox" checked={v.readOnly === true || c.isIdentity || c.isComputed} disabled={c.isIdentity || c.isComputed}
          onChange={e => onChange({ readOnly: e.target.checked || undefined })} />{t('Solo lectura')}</label>
        <label className="check"><input type="checkbox" checked={v.required === true || (!c.isNullable && !c.isReadOnly)} disabled={!c.isNullable || c.isReadOnly}
          onChange={e => onChange({ required: e.target.checked || undefined })} />{t('Obligatoria')}</label>
      </div>

      {!hidden && (
        <div className="grid-2">
          <input value={v.label ?? ''} placeholder={t('Etiqueta ({name})', { name: c.name })} onChange={e => onChange({ label: e.target.value })} />
          <input value={v.helpText ?? ''} placeholder={t('Ayuda bajo el campo')} onChange={e => onChange({ helpText: e.target.value })} />
        </div>
      )}

      {!hidden && c.isFile && (
        <label className="check">
          <input type="checkbox" checked={v.widget === 'image'} onChange={e => onChange({ widget: e.target.checked ? 'image' : undefined })} />
          {t('Es una imagen: miniatura en la grilla')}
        </label>
      )}

      {!hidden && !c.isReadOnly && (isText || isNumber) && (
        <>
          <button className="link small" onClick={() => setOpen(o => !o)}>{open ? '▾' : '▸'} {t('Control y validaciones')}</button>
          {open && (
            <div className="grid-2">
              {isText && (
                <label>
                  <span className="field-label">{t('Control')}</span>
                  <select value={v.widget ?? ''} onChange={e => onChange({ widget: e.target.value })}>
                    <option value="">{t('(automático)')}</option>
                    {WIDGETS.filter(w => w !== 'money').map(w => <option key={w} value={w}>{w}</option>)}
                  </select>
                </label>
              )}
              {isNumber && (
                <label>
                  <span className="field-label">{t('Control')}</span>
                  <select value={v.widget ?? ''} onChange={e => onChange({ widget: e.target.value })}>
                    <option value="">{t('(automático)')}</option>
                    <option value="money">money</option>
                  </select>
                </label>
              )}
              {isText && (
                <>
                  <label>
                    <span className="field-label">{t('Formato (expresión regular)')}</span>
                    <input className="mono" value={v.pattern ?? ''} placeholder={t('ej.: {example}', { example: '[A-Z]{3}-\\d{3}' })} onChange={e => onChange({ pattern: e.target.value })} />
                    <span className="hint">{t('Debe cumplirlo el texto completo')}</span>
                  </label>
                  <label>
                    <span className="field-label">{t('Largo máximo')}</span>
                    <input type="number" min={1} value={v.maxLength ?? ''} placeholder={c.maxLength ? String(c.maxLength) : ''}
                      onChange={e => onChange({ maxLength: num(e.target.value) })} />
                  </label>
                  <label className="span-2">
                    <span className="field-label">{t('Valores permitidos')}</span>
                    <input value={(v.options ?? []).join(', ')} placeholder={t('Separados por coma: Pendiente, Pagado, Anulado')}
                      onChange={e => onChange({ options: e.target.value.split(',').map(s => s.trim()).filter(Boolean) })} />
                  </label>
                </>
              )}
              {isNumber && (
                <>
                  <label>
                    <span className="field-label">{t('Mínimo')}</span>
                    <input type="number" step="any" value={v.min ?? ''} onChange={e => onChange({ min: num(e.target.value) })} />
                  </label>
                  <label>
                    <span className="field-label">{t('Máximo')}</span>
                    <input type="number" step="any" value={v.max ?? ''} onChange={e => onChange({ max: num(e.target.value) })} />
                  </label>
                </>
              )}
            </div>
          )}
        </>
      )}

      {colErrors.map(([k, msg]) => <div key={k} className="field-error">{fieldName(k)}: {msg}</div>)}
    </div>
  )
}

// ---------- Vista previa ----------

function Preview({ conn, table, loading }: { conn: string; table: TableMeta; loading: boolean }) {
  const t = useT()
  const grid = table.columns.filter(c => c.isSelectable && !c.hidden && c.showInGrid)
  const form = table.columns.filter(c => c.isSelectable && !c.hidden && !(c.isIdentity || c.isComputed || c.isRowVersion))
  const fkByColumn = new Map(table.foreignKeys.filter(f => f.columns.length === 1).map(f => [f.columns[0], f]))

  return (
    <aside className={`card preview${loading ? ' loading' : ''}`}>
      <div className="muted small">{t('Vista previa')}</div>
      <h2>{labelOf(table)}</h2>
      <div className="muted small">
        {t('Menú: {group}', { group: table.group ?? table.schema })}{table.hidden && ' · ' + t('oculta')}
        {!table.isEditable && ' · ' + t('solo lectura')}
        {table.defaultSort && ' · ' + t('orden: {sort}', { sort: table.defaultSort })}
      </div>
      <div className="table-wrap">
        <table className="grid">
          <thead><tr>{grid.map(c => <th key={c.name}>{c.isPrimaryKey && <span className="pk">🔑</span>}{labelOf(c)}</th>)}</tr></thead>
        </table>
      </div>
      <div className="form">
        {form.map(c => {
          const required = !c.isReadOnly && c.isRequiredOnInsert
          return (
            <label key={c.name}>
              <span className="field-label">{labelOf(c)}{required && <span className="req"> *</span>}</span>
              <FieldControl conn={conn} column={c} fk={fkByColumn.get(c.name)} value={c.type === 'Boolean' && !c.isNullable ? false : ''}
                disabled required={required} onChange={() => {}} />
              {c.helpText && <span className="hint">{c.helpText}</span>}
            </label>
          )
        })}
      </div>
    </aside>
  )
}

// ---------- Utilidades ----------

function Field({ label, hint, error, span, children }: {
  label: string; hint?: string; error?: string; span?: boolean; children: ReactNode
}) {
  return (
    <div className={span ? 'field span-2' : 'field'}>
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="hint">{hint}</span>}
      {error && <span className="field-error">{error}</span>}
    </div>
  )
}

function Check({ label, hint, checked, onChange }: { label: string; hint?: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="field">
      <label className="check"><input type="checkbox" checked={checked} onChange={e => onChange(e.target.checked)} />{label}</label>
      {hint && <span className="hint">{hint}</span>}
    </div>
  )
}

const num = (s: string) => (s.trim() === '' ? undefined : Number(s))

function parseSort(s?: string): [string, boolean] {
  const first = (s ?? '').split(',')[0]?.trim() ?? ''
  return first.startsWith('-') ? [first.slice(1), true] : [first, false]
}

function splitRef(ref: string, tables?: { schema: string; name: string }[]): [string, string] {
  const t = tables?.find(x => `${x.schema}.${x.name}` === ref)
  return t ? [t.schema, t.name] : ['', '']
}

/** "columns.precio.min" → "precio · min". */
function fieldName(key: string) {
  return key.startsWith('columns.') ? key.slice(8).replace('.', ' · ') : key
}

/**
 * Documento que se guarda: sin vacíos, para que lo no tocado siga automático.
 * El orden se guarda solo si se cambió, y entonces para todas las columnas.
 */
function buildDoc(doc: TableOverride, order: string[], natural: string[]): TableOverride {
  const reordered = order.join('\u0001') !== natural.join('\u0001')
  const columns: Record<string, ColumnOverride> = {}
  const names = new Set([...Object.keys(doc.columns ?? {}), ...(reordered ? order : [])])
  for (const name of names) {
    const c = clean({ ...doc.columns?.[name], order: reordered ? order.indexOf(name) + 1 : undefined })
    if (Object.keys(c).length > 0) columns[name] = c
  }
  const result = clean({ ...doc, columns: Object.keys(columns).length > 0 ? columns : undefined })
  // "children: []" significa "ninguna hija", distinto de no definirlo ("todas"): no se limpia.
  if (Array.isArray(doc.children)) result.children = doc.children
  return result
}

function clean<T extends object>(o: T): T {
  return Object.fromEntries(Object.entries(o).filter(([, v]) =>
    v !== undefined && v !== null && v !== '' && !(Array.isArray(v) && v.length === 0) && !(typeof v === 'number' && Number.isNaN(v)),
  )) as T
}
