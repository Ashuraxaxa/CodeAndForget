import { useMemo, useState, type ReactNode } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import {
  WIDGETS, labelOf,
  type Column, type ColumnOverride, type TableMeta, type TableOverride, type VirtualForeignKey,
} from '../api/types'
import { FieldControl } from '../components/FieldControl'
import { useDebounce } from '../lib/useDebounce'
import { useMe } from '../lib/useMe'

/** Editor de ajustes de un mantenedor (P-012): sin editar JSON, con vista previa en vivo. Requiere el permiso ConfigureTables (D-053). */
export function ConfigPage() {
  const { conn = '', schema = '', table = '' } = useParams()
  const me = useMe()
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

  if (me.isLoading) return <p className="muted">Cargando…</p>
  if (!me.data?.permissions.includes('ConfigureTables'))
    return <div className="alert">No tiene permiso para configurar mantenedores (pídale a un administrador el rol correspondiente).</div>
  const error = current.error ?? initial.error
  if (error) return <div className="alert">{(error as Error).message}</div>
  if (!initial.data) return <p className="muted">Cargando…</p>

  return <Editor conn={conn} initialDoc={current.data!} initialTable={initial.data.table} />
}

type Tab = 'table' | 'columns'

function Editor({ conn, initialDoc, initialTable }: { conn: string; initialDoc: TableOverride; initialTable: TableMeta }) {
  const navigate = useNavigate()
  const qc = useQueryClient()
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
          <h1>Configurar · {labelOf(shown)}</h1>
          <div className="muted small">{schema}.{name} · los cambios se ven en la vista previa antes de guardar</div>
        </div>
        <Link to={back}>‹ Volver a la lista</Link>
      </div>

      <div className="tabs">
        <button className={tab === 'table' ? 'tab active' : 'tab'} onClick={() => setTab('table')}>Tabla</button>
        <button className={tab === 'columns' ? 'tab active' : 'tab'} onClick={() => setTab('columns')}>
          Columnas ({order.length})
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
                  />
                ))}
                {order.join() !== naturalOrder.join() && (
                  <button className="link" onClick={() => setOrder(naturalOrder)}>Volver al orden de la tabla</button>
                )}
                {orphans.length > 0 && (
                  <div className="alert">
                    <b>Ajustes de columnas que ya no existen en la base</b> (se ignoran; quítelos para poder guardar):
                    {orphans.map(col => (
                      <div key={col} className="row">
                        <code>{col}</code>
                        <button className="link danger" onClick={() => removeCol(col)}>Quitar</button>
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
              {save.isPending ? 'Guardando…' : 'Guardar'}
            </button>
            <Link to={back} className="button">Cancelar</Link>
            <button
              className="link danger"
              disabled={reset.isPending}
              onClick={() => confirm('¿Quitar todos los ajustes de esta tabla y volver a lo automático?') && reset.mutate()}
            >
              Restablecer a lo automático
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
  const set = (patch: Partial<TableOverride>) => setDoc(d => ({ ...d, ...patch }))
  const visible = table.columns.filter(c => c.type !== 'Binary' && c.type !== 'Unsupported')
  const [sortCol, sortDesc] = parseSort(doc.defaultSort)
  const declaredPk = table.hasPrimaryKey && !table.hasVirtualPrimaryKey
  const vpk = doc.virtualPrimaryKey ?? []

  return (
    <div className="grid-2">
      <Field label="Nombre visible" hint={`Por defecto: ${table.name}`} error={errors.label}>
        <input value={doc.label ?? ''} placeholder={table.name} onChange={e => set({ label: e.target.value })} />
      </Field>
      <Field label="Grupo del menú" hint={`Por defecto: ${table.schema}`} error={errors.group}>
        <input value={doc.group ?? ''} placeholder={table.schema} onChange={e => set({ group: e.target.value })} />
      </Field>
      <Field label="Columna a mostrar" hint="Se ve en los combos de las tablas que referencian a esta" error={errors.displayColumn}>
        <select value={doc.displayColumn ?? ''} onChange={e => set({ displayColumn: e.target.value })}>
          <option value="">(automática{!doc.displayColumn && shown.displayColumn ? `: ${shown.displayColumn}` : ''})</option>
          {visible.map(c => <option key={c.name} value={c.name}>{c.name}</option>)}
        </select>
      </Field>
      <Field label="Orden por defecto" hint="Cuando el usuario no eligió otro" error={errors.defaultSort}>
        <div className="row">
          <select value={sortCol} onChange={e => set({ defaultSort: e.target.value ? (sortDesc ? '-' : '') + e.target.value : '' })}>
            <option value="">(por clave)</option>
            {table.columns.filter(c => c.isSortable).map(c => <option key={c.name} value={c.name}>{c.name}</option>)}
          </select>
          <select value={sortDesc ? 'desc' : 'asc'} disabled={!sortCol}
            onChange={e => set({ defaultSort: (e.target.value === 'desc' ? '-' : '') + sortCol })}>
            <option value="asc">ascendente</option>
            <option value="desc">descendente</option>
          </select>
        </div>
      </Field>
      <Check label="Ocultar del menú" hint="No es un permiso: se sigue pudiendo abrir por su dirección"
        checked={doc.hidden === true} onChange={v => set({ hidden: v || undefined })} />
      <Check label="Solo lectura" hint="No se puede crear, editar ni borrar"
        checked={doc.readOnly === true} onChange={v => set({ readOnly: v || undefined })} />

      {!declaredPk && (
        <Field label="Clave virtual" span hint="La base no declara PK: marque las columnas que identifican una sola fila" error={errors.virtualPrimaryKey}>
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

      <VirtualForeignKeys conn={conn} table={table} value={doc.virtualForeignKeys ?? []} errors={errors}
        onChange={v => set({ virtualForeignKeys: v })} />
    </div>
  )
}

function VirtualForeignKeys({ conn, table, value, errors, onChange }: {
  conn: string
  table: TableMeta
  value: VirtualForeignKey[]
  errors: Record<string, string>
  onChange: (v: VirtualForeignKey[]) => void
}) {
  const [col, setCol] = useState('')
  const [ref, setRef] = useState('') // "esquema.tabla"
  const [refCol, setRefCol] = useState('')
  const tables = useQuery({ queryKey: ['tables', conn], queryFn: () => api.tables(conn) })
  const [refSchema, refTable] = splitRef(ref, tables.data?.tables)
  const target = useQuery({
    queryKey: ['meta', conn, refSchema, refTable],
    queryFn: () => api.table(conn, refSchema, refTable),
    enabled: !!refTable,
  })
  const declared = new Set(table.foreignKeys.filter(f => !value.some(v => v.columns.join() === f.columns.join())).flatMap(f => f.columns))

  function add() {
    onChange([...value, { columns: [col], refSchema, refTable, refColumns: [refCol] }])
    setCol(''); setRef(''); setRefCol('')
  }

  return (
    <Field label="Relaciones virtuales" span hint="Relaciones que existen pero la base no declara: dan combo y etiqueta como una FK real">
      {value.map((v, i) => (
        <div key={i} className="row">
          <span className="chip">{v.columns.join(', ')} → {v.refSchema}.{v.refTable} ({v.refColumns.join(', ')})</span>
          <button className="link danger" onClick={() => onChange(value.filter((_, j) => j !== i))}>Quitar</button>
          {errors[`virtualForeignKeys.${i}`] && <span className="field-error">{errors[`virtualForeignKeys.${i}`]}</span>}
        </div>
      ))}
      <div className="row">
        <select value={col} onChange={e => setCol(e.target.value)}>
          <option value="">Columna…</option>
          {table.columns.filter(c => !declared.has(c.name)).map(c => <option key={c.name} value={c.name}>{c.name}</option>)}
        </select>
        <span>→</span>
        <select value={ref} onChange={e => { setRef(e.target.value); setRefCol('') }}>
          <option value="">Tabla…</option>
          {tables.data?.tables.map(t => <option key={`${t.schema}.${t.name}`} value={`${t.schema}.${t.name}`}>{t.schema}.{t.name}</option>)}
        </select>
        <select value={refCol} disabled={!target.data} onChange={e => setRefCol(e.target.value)}>
          <option value="">Columna…</option>
          {target.data?.columns.map(c => <option key={c.name} value={c.name}>{c.name}{c.isPrimaryKey ? ' (PK)' : ''}</option>)}
        </select>
        <button disabled={!col || !refTable || !refCol} onClick={add}>Agregar</button>
      </div>
    </Field>
  )
}

// ---------- Pestaña "Columnas" ----------

function ColumnEditor({ column: c, value: v, errors, first, last, onMove, onChange }: {
  column: Column
  value: ColumnOverride
  errors: Record<string, string>
  first: boolean
  last: boolean
  onMove: (delta: number) => void
  onChange: (patch: Partial<ColumnOverride>) => void
}) {
  const isText = c.type === 'String' || c.type === 'Text'
  const isNumber = (c.type === 'Integer' || c.type === 'Decimal') && !c.isLargeNumber
  const prefix = `columns.${c.name}.`
  const colErrors = Object.entries(errors).filter(([k]) => k === `columns.${c.name}` || k.startsWith(prefix))
  const [open, setOpen] = useState(() => !!(v.widget || v.pattern || v.maxLength != null || v.min != null || v.max != null || v.options?.length))
  const hidden = v.hidden === true

  return (
    <div className={`col-editor${hidden ? ' is-hidden' : ''}`}>
      <div className="col-head">
        <div className="col-move">
          <button title="Subir" disabled={first} onClick={() => onMove(-1)}>↑</button>
          <button title="Bajar" disabled={last} onClick={() => onMove(1)}>↓</button>
        </div>
        <div className="col-name">
          <b>{c.isPrimaryKey && '🔑 '}{c.name}</b>
          <span className="muted small"> {c.nativeType}{c.isRequiredOnInsert ? ' · obligatoria' : ''}</span>
        </div>
        <label className="check"><input type="checkbox" checked={!hidden} onChange={e => onChange({ hidden: e.target.checked ? undefined : true })} />Visible</label>
        <label className="check"><input type="checkbox" checked={v.showInGrid !== false} disabled={hidden}
          onChange={e => onChange({ showInGrid: e.target.checked ? undefined : false })} />En la grilla</label>
        <label className="check"><input type="checkbox" checked={v.readOnly === true || c.isIdentity || c.isComputed} disabled={c.isIdentity || c.isComputed}
          onChange={e => onChange({ readOnly: e.target.checked || undefined })} />Solo lectura</label>
        <label className="check"><input type="checkbox" checked={v.required === true || (!c.isNullable && !c.isReadOnly)} disabled={!c.isNullable || c.isReadOnly}
          onChange={e => onChange({ required: e.target.checked || undefined })} />Obligatoria</label>
      </div>

      {!hidden && (
        <div className="grid-2">
          <input value={v.label ?? ''} placeholder={`Etiqueta (${c.name})`} onChange={e => onChange({ label: e.target.value })} />
          <input value={v.helpText ?? ''} placeholder="Ayuda bajo el campo" onChange={e => onChange({ helpText: e.target.value })} />
        </div>
      )}

      {!hidden && !c.isReadOnly && (isText || isNumber) && (
        <>
          <button className="link small" onClick={() => setOpen(o => !o)}>{open ? '▾' : '▸'} Control y validaciones</button>
          {open && (
            <div className="grid-2">
              {isText && (
                <label>
                  <span className="field-label">Control</span>
                  <select value={v.widget ?? ''} onChange={e => onChange({ widget: e.target.value })}>
                    <option value="">(automático)</option>
                    {WIDGETS.filter(w => w !== 'money').map(w => <option key={w} value={w}>{w}</option>)}
                  </select>
                </label>
              )}
              {isNumber && (
                <label>
                  <span className="field-label">Control</span>
                  <select value={v.widget ?? ''} onChange={e => onChange({ widget: e.target.value })}>
                    <option value="">(automático)</option>
                    <option value="money">money</option>
                  </select>
                </label>
              )}
              {isText && (
                <>
                  <label>
                    <span className="field-label">Formato (expresión regular)</span>
                    <input className="mono" value={v.pattern ?? ''} placeholder="ej.: [A-Z]{3}-\d{3}" onChange={e => onChange({ pattern: e.target.value })} />
                    <span className="hint">Debe cumplirlo el texto completo</span>
                  </label>
                  <label>
                    <span className="field-label">Largo máximo</span>
                    <input type="number" min={1} value={v.maxLength ?? ''} placeholder={c.maxLength ? String(c.maxLength) : ''}
                      onChange={e => onChange({ maxLength: num(e.target.value) })} />
                  </label>
                  <label className="span-2">
                    <span className="field-label">Valores permitidos</span>
                    <input value={(v.options ?? []).join(', ')} placeholder="Separados por coma: Pendiente, Pagado, Anulado"
                      onChange={e => onChange({ options: e.target.value.split(',').map(s => s.trim()).filter(Boolean) })} />
                  </label>
                </>
              )}
              {isNumber && (
                <>
                  <label>
                    <span className="field-label">Mínimo</span>
                    <input type="number" step="any" value={v.min ?? ''} onChange={e => onChange({ min: num(e.target.value) })} />
                  </label>
                  <label>
                    <span className="field-label">Máximo</span>
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
  const grid = table.columns.filter(c => c.isSelectable && !c.hidden && c.showInGrid)
  const form = table.columns.filter(c => c.isSelectable && !c.hidden && !(c.isIdentity || c.isComputed || c.isRowVersion))
  const fkByColumn = new Map(table.foreignKeys.filter(f => f.columns.length === 1).map(f => [f.columns[0], f]))

  return (
    <aside className={`card preview${loading ? ' loading' : ''}`}>
      <div className="muted small">Vista previa</div>
      <h2>{labelOf(table)}</h2>
      <div className="muted small">
        Menú: {table.group ?? table.schema}{table.hidden && ' · oculta'}
        {!table.isEditable && ' · solo lectura'}
        {table.defaultSort && ` · orden: ${table.defaultSort}`}
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
  return clean({ ...doc, columns: Object.keys(columns).length > 0 ? columns : undefined })
}

function clean<T extends object>(o: T): T {
  return Object.fromEntries(Object.entries(o).filter(([, v]) =>
    v !== undefined && v !== null && v !== '' && !(Array.isArray(v) && v.length === 0) && !(typeof v === 'number' && Number.isNaN(v)),
  )) as T
}
