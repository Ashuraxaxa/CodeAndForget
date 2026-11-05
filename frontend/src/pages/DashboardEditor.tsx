import { ArrowDown, ArrowUp, GripVertical, Pencil, Plus, Trash2, X } from 'lucide-react'
import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type {
  AggregateFunction, Column, DashboardShare, DashboardView, DashboardWidget, DateBucket, WidgetSize, WidgetType,
} from '../api/types'
import { labelOf } from '../api/types'
import { SearchSelect } from '../components/Combo'
import { ErrorText } from '../components/ErrorText'
import { WidgetView } from '../components/WidgetView'
import { useT, type Translate } from '../i18n'
import { rangeOf, type RangeId } from '../lib/dashboardRange'

/**
 * Editor de un dashboard (P-130, D-111): nombre, tarjetas (con vista previa), orden arrastrando o con flechas,
 * tamaño y, para el dueño, con qué perfiles se comparte. Todo se valida de nuevo en el servidor al guardar.
 */
export function DashboardEditor({ view, widgetLimit, range, onDone }: {
  view: DashboardView; widgetLimit?: number; range: RangeId; onDone: () => void
}) {
  const t = useT()
  const qc = useQueryClient()
  const d = view.dashboard
  const [name, setName] = useState(d.name)
  const [description, setDescription] = useState(d.description ?? '')
  const [widgets, setWidgets] = useState<DashboardWidget[]>(d.widgets)
  const [shares, setShares] = useState<DashboardShare[]>(d.shares)
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const [dragging, setDragging] = useState<number | null>(null)

  const save = useMutation({
    mutationFn: () => api.updateDashboard(d.id, {
      name: name.trim(), description: description.trim() || null, widgets, shares: view.canShare ? shares : undefined,
    }),
    onSuccess: v => {
      qc.setQueryData(['dashboard', d.id], v)
      qc.invalidateQueries({ queryKey: ['dashboards'] })
      qc.invalidateQueries({ queryKey: ['dashboard', d.id, 'widget'] })
      onDone()
    },
  })
  const errors = (save.error as { fieldErrors?: Record<string, string> } | null)?.fieldErrors ?? {}

  const move = (from: number, to: number) => setWidgets(ws => {
    if (to < 0 || to >= ws.length || from === to) return ws
    const next = [...ws]
    const [w] = next.splice(from, 1)
    next.splice(to, 0, w)
    return next
  })
  const full = widgetLimit != null && widgets.length >= widgetLimit

  return (
    <div className="dashboard-editor">
      <section className="card form">
        <div className="grid-2">
          <label>{t('Nombre')}<input value={name} onChange={e => setName(e.target.value)} maxLength={100} required /></label>
          <label>{t('Descripción')}<input value={description} onChange={e => setDescription(e.target.value)} maxLength={500} /></label>
        </div>
        {errors.name && <span className="field-error">{errors.name}</span>}
      </section>

      <section className="card">
        <div className="editor-head">
          <h2>{t('Tarjetas')}</h2>
          <span className="muted small">{widgetLimit != null ? t('{n} de {max}', { n: widgets.length, max: widgetLimit }) : widgets.length}</span>
        </div>
        {widgets.length === 0 && <p className="muted">{t('Agrega la primera tarjeta.')}</p>}
        <ol className="widget-list">
          {widgets.map((w, i) => (
            <li key={w.id || i} className={`widget-item ${dragging === i ? 'dragging' : ''}`}
              onDragOver={e => { if (dragging != null) e.preventDefault() }}
              onDrop={e => { e.preventDefault(); if (dragging != null) move(dragging, i); setDragging(null) }}>
              <span className="col-grip" draggable title={t('Arrastrar para ordenar')}
                onDragStart={e => { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', String(i)); setDragging(i) }}
                onDragEnd={() => setDragging(null)}><GripVertical size={16} aria-hidden /></span>
              <span className="widget-item-name">
                <strong>{w.title || t('(sin título)')}</strong>
                <span className="muted small">{typeLabel(t, w.type)} · {w.schema}.{w.table}</span>
                {Object.entries(errors).filter(([k]) => k.startsWith(`widgets[${i}]`)).map(([k, v]) => <span key={k} className="field-error">{v}</span>)}
              </span>
              <select value={w.size} aria-label={t('Tamaño')} onChange={e => setWidgets(ws => ws.map((x, j) => j === i ? { ...x, size: e.target.value as WidgetSize } : x))}>
                <option value="S">{t('Chica')}</option><option value="M">{t('Mediana')}</option><option value="L">{t('Grande')}</option>
              </select>
              <button type="button" className="icon-button" title={t('Subir')} aria-label={t('Subir')} disabled={i === 0} onClick={() => move(i, i - 1)}><ArrowUp size={15} aria-hidden /></button>
              <button type="button" className="icon-button" title={t('Bajar')} aria-label={t('Bajar')} disabled={i === widgets.length - 1} onClick={() => move(i, i + 1)}><ArrowDown size={15} aria-hidden /></button>
              <button type="button" className="icon-button" title={t('Editar')} aria-label={t('Editar')} onClick={() => setEditing(i)}><Pencil size={15} aria-hidden /></button>
              <button type="button" className="icon-button danger-text" title={t('Quitar')} aria-label={t('Quitar')} onClick={() => setWidgets(ws => ws.filter((_, j) => j !== i))}><Trash2 size={15} aria-hidden /></button>
            </li>
          ))}
        </ol>
        <button type="button" disabled={full} onClick={() => setEditing('new')} title={full ? t('Llegaste al tope de tarjetas de tu plan.') : undefined}>
          <Plus size={15} aria-hidden />{t('Agregar tarjeta')}
        </button>
      </section>

      {view.canShare && <ShareEditor value={shares} onChange={setShares} />}

      {save.error && Object.keys(errors).length === 0 && <div className="alert"><ErrorText error={save.error} /></div>}
      <div className="actions">
        <button className="primary" disabled={!name.trim() || save.isPending} onClick={() => save.mutate()}>{save.isPending ? t('Guardando…') : t('Guardar')}</button>
        <button type="button" onClick={onDone}>{t('Cancelar')}</button>
      </div>

      {editing != null && (
        <WidgetForm
          initial={editing === 'new' ? null : widgets[editing]}
          range={range}
          onCancel={() => setEditing(null)}
          onSave={w => {
            setWidgets(ws => editing === 'new' ? [...ws, w] : ws.map((x, j) => j === editing ? w : x))
            setEditing(null)
          }} />
      )}
    </div>
  )
}

function typeLabel(t: Translate, type: WidgetType): string {
  return ({
    Number: t('Número'), Bar: t('Barras'), Line: t('Líneas'), Pie: t('Dona'), Top: t('Top N'), Pivot: t('Tabla dinámica'), Map: t('Mapa'),
  } as Record<WidgetType, string>)[type]
}

/** Agrupaciones que pide cada tipo: Número 0, Tabla dinámica 2, el resto 1. */
const groupsFor = (type: WidgetType) => type === 'Number' ? 0 : type === 'Pivot' || type === 'Map' ? 2 : 1

const isNumeric = (c: Column) => (c.type === 'Integer' || c.type === 'Decimal') && !c.isLargeNumber
const isDate = (c: Column) => c.type === 'Date' || c.type === 'DateTime'

function emptyWidget(): DashboardWidget {
  return {
    id: '', title: '', type: 'Bar', size: 'M', connection: '', schema: '', table: '',
    query: { function: 'Count', column: null, groupBy: [{ column: '' }], order: 'ValueDesc', limit: 10, filters: {}, exact: {}, dateColumn: null },
  }
}

/** Una tarjeta: de qué tabla sale, qué calcula, cómo agrupa y su vista previa con los datos de verdad. */
function WidgetForm({ initial, range, onSave, onCancel }: {
  initial: DashboardWidget | null; range: RangeId; onSave: (w: DashboardWidget) => void; onCancel: () => void
}) {
  const t = useT()
  const [w, setW] = useState<DashboardWidget>(initial ?? emptyWidget())
  const q = w.query
  const setQ = (patch: Partial<DashboardWidget['query']>) => setW(x => ({ ...x, query: { ...x.query, ...patch } }))

  const connections = useQuery({ queryKey: ['connections'], queryFn: api.connections })
  useEffect(() => {
    if (!w.connection && connections.data?.length === 1) setW(x => ({ ...x, connection: connections.data![0].id }))
  }, [connections.data, w.connection])
  const tables = useQuery({ queryKey: ['tables', w.connection], queryFn: () => api.tables(w.connection), enabled: !!w.connection })
  const meta = useQuery({
    queryKey: ['meta', w.connection, w.schema, w.table], queryFn: () => api.table(w.connection, w.schema, w.table),
    enabled: !!w.connection && !!w.table,
  })
  const columns = useMemo(() => (meta.data?.columns ?? []).filter(c => c.isSelectable !== false && !c.hidden), [meta.data])
  const option = (c: Column) => ({ value: c.name, label: labelOf(c), hint: c.name !== labelOf(c) ? c.name : c.nativeType })
  const groupable = columns.filter(c => c.isSortable)
  const metricCols = q.function === 'Sum' || q.function === 'Avg' ? columns.filter(isNumeric)
    : q.function === 'Min' || q.function === 'Max' ? columns.filter(c => c.isSortable && (isNumeric(c) || isDate(c)))
    : columns.filter(c => c.isSortable)
  const dateCols = columns.filter(c => isDate(c) && c.isFilterable)

  function setType(type: WidgetType) {
    const n = groupsFor(type)
    const groupBy = Array.from({ length: n }, (_, i) => q.groupBy[i] ?? { column: '' })
    setW(x => ({ ...x, type, query: { ...x.query, groupBy, order: type === 'Line' || type === 'Pivot' ? 'Key' : x.query.order, limit: type === 'Map' ? Math.max(x.query.limit, 500) : x.query.limit } }))
  }
  function setGroup(i: number, column: string, bucket?: DateBucket | null) {
    const col = columns.find(c => c.name === column)
    const b = col && isDate(col) ? (bucket === undefined ? (q.groupBy[i]?.bucket ?? 'Month') : bucket) : null
    setQ({ groupBy: q.groupBy.map((g, j) => j === i ? { column, bucket: b } : g) })
  }

  const ready = !!w.table && q.groupBy.every(g => g.column) && (q.function === 'Count' || !!q.column)
  const r = q.dateColumn ? rangeOf(range) : {}
  // La vista previa se pide cuando la tarjeta está completa, medio segundo después del último cambio.
  const [debounced, setDebounced] = useState(w)
  useEffect(() => { const h = setTimeout(() => setDebounced(w), 500); return () => clearTimeout(h) }, [w])
  const preview = useQuery({
    queryKey: ['preview', JSON.stringify(debounced), r.from, r.to],
    queryFn: () => api.previewWidget(debounced, r.from, r.to),
    enabled: ready && debounced === w,
    retry: false,
    staleTime: 30_000,
  })

  const filters = Object.entries(q.filters)
  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label={t('Tarjeta')} onClick={e => { if (e.target === e.currentTarget) onCancel() }}>
      <div className="modal card widget-form">
        <div className="editor-head">
          <h2>{initial ? t('Editar tarjeta') : t('Nueva tarjeta')}</h2>
          <button type="button" className="icon-button" aria-label={t('Cerrar')} onClick={onCancel}><X size={16} aria-hidden /></button>
        </div>
        <div className="widget-form-body">
          <div className="form widget-form-fields">
            <label>{t('Título')}<input value={w.title} onChange={e => setW(x => ({ ...x, title: e.target.value }))} maxLength={100} placeholder={t('Ventas por categoría')} /></label>
            <label>{t('Tipo')}
              <select value={w.type} onChange={e => setType(e.target.value as WidgetType)}>
                {(['Number', 'Bar', 'Line', 'Pie', 'Top', 'Pivot', 'Map'] as WidgetType[]).map(x => <option key={x} value={x}>{typeLabel(t, x)}</option>)}
              </select>
            </label>
            <label>{t('Conexión')}
              <SearchSelect value={w.connection} ariaLabel={t('Conexión')} placeholder={`— ${t('conexión')} —`}
                options={(connections.data ?? []).map(c => ({ value: c.id, label: c.name, hint: c.id }))}
                onChange={v => setW(x => ({ ...x, connection: v, schema: '', table: '', query: { ...x.query, column: null, groupBy: x.query.groupBy.map(() => ({ column: '' })), filters: {}, dateColumn: null } }))} />
            </label>
            <label>{t('Tabla')}
              <SearchSelect value={w.table ? `${w.schema}.${w.table}` : ''} ariaLabel={t('Tabla')} placeholder={`— ${t('tabla')} —`}
                options={(tables.data?.tables ?? []).map(x => ({ value: `${x.schema}.${x.name}`, label: x.label || x.name, hint: `${x.schema}.${x.name}` }))}
                onChange={v => {
                  const [schema, ...rest] = v.split('.')
                  setW(x => ({ ...x, schema, table: rest.join('.'), query: { ...x.query, column: null, groupBy: x.query.groupBy.map(() => ({ column: '' })), filters: {}, dateColumn: null } }))
                }} />
            </label>
            <label>{t('Calcular')}
              <select value={q.function} onChange={e => setQ({ function: e.target.value as AggregateFunction, column: e.target.value === 'Count' ? null : q.column })}>
                <option value="Count">{t('Cantidad de filas')}</option>
                <option value="CountDistinct">{t('Cantidad de distintos')}</option>
                <option value="Sum">{t('Suma')}</option>
                <option value="Avg">{t('Promedio')}</option>
                <option value="Min">{t('Mínimo')}</option>
                <option value="Max">{t('Máximo')}</option>
              </select>
            </label>
            {q.function !== 'Count' && (
              <label>{t('De la columna')}
                <SearchSelect value={q.column ?? ''} ariaLabel={t('De la columna')} placeholder={`— ${t('columna')} —`}
                  options={metricCols.map(option)} onChange={v => setQ({ column: v })} />
              </label>
            )}
            {q.groupBy.map((g, i) => {
              const col = columns.find(c => c.name === g.column)
              return (
                <div key={i} className="grid-2">
                  <label>{w.type === 'Map' ? (i === 0 ? t('Latitud') : t('Longitud')) : w.type === 'Pivot' ? (i === 0 ? t('Filas') : t('Columnas')) : t('Agrupar por')}
                    <SearchSelect value={g.column} ariaLabel={w.type === 'Map' ? (i === 0 ? t('Latitud') : t('Longitud')) : t('Agrupar por')} placeholder={`— ${t('columna')} —`}
                      options={(w.type === 'Map' ? columns.filter(isNumeric) : groupable).map(option)} onChange={v => setGroup(i, v)} />
                  </label>
                  {col && isDate(col) && w.type !== 'Map' && (
                    <label>{t('Por')}
                      <select value={g.bucket ?? ''} onChange={e => setGroup(i, g.column, (e.target.value || null) as DateBucket | null)}>
                        <option value="Day">{t('Día')}</option><option value="Week">{t('Semana')}</option><option value="Month">{t('Mes')}</option>
                        <option value="Quarter">{t('Trimestre')}</option><option value="Year">{t('Año')}</option><option value="">{t('Fecha exacta')}</option>
                      </select>
                    </label>
                  )}
                </div>
              )
            })}
            {q.groupBy.length > 0 && (
              <div className="grid-2">
                <label>{t('Ordenar')}
                  <select value={q.order} onChange={e => setQ({ order: e.target.value as DashboardWidget['query']['order'] })}>
                    <option value="ValueDesc">{t('Mayor resultado primero')}</option>
                    <option value="ValueAsc">{t('Menor resultado primero')}</option>
                    <option value="Key">{t('Por el grupo (fechas en orden)')}</option>
                  </select>
                </label>
                <label>{t('Máximo de grupos')}
                  <input type="number" min={1} max={500} value={q.limit} onChange={e => setQ({ limit: Math.max(1, Math.min(500, Number(e.target.value) || 1)) })} />
                </label>
              </div>
            )}
            <label>{t('Fecha para el período del dashboard')}
              <select value={q.dateColumn ?? ''} onChange={e => setQ({ dateColumn: e.target.value || null })}>
                <option value="">{t('Ninguna: la tarjeta no cambia con el período')}</option>
                {dateCols.map(c => <option key={c.name} value={c.name}>{labelOf(c)}</option>)}
              </select>
            </label>

            <div>
              <span className="field-label">{t('Filtros')}</span>
              {filters.map(([col, value], i) => (
                <div key={i} className="rule-head">
                  <SearchSelect value={col} ariaLabel={t('Columna')} placeholder={`— ${t('columna')} —`}
                    options={columns.filter(c => c.isFilterable).map(option)}
                    onChange={v => setQ({ filters: Object.fromEntries(filters.map(([k, x], j) => j === i ? [v, x] : [k, x])) })} />
                  <input value={value} aria-label={t('Valor')} placeholder={t('contiene o es igual a…')}
                    onChange={e => setQ({ filters: Object.fromEntries(filters.map(([k, x], j) => j === i ? [k, e.target.value] : [k, x])) })} />
                  <button type="button" className="link danger" onClick={() => setQ({ filters: Object.fromEntries(filters.filter((_, j) => j !== i)) })}>{t('Quitar')}</button>
                </div>
              ))}
              <button type="button" className="link small" disabled={!w.table || '' in q.filters} onClick={() => setQ({ filters: { ...q.filters, '': '' } })}>
                <Plus size={14} aria-hidden />{t('Filtro')}
              </button>
            </div>
          </div>

          <div className="widget-form-preview">
            <span className="field-label">{t('Vista previa')}</span>
            {ready
              ? <WidgetView widget={{ ...w, size: 'L' }} data={preview.data} isLoading={preview.isFetching && !preview.data} error={preview.error} />
              : <p className="muted small">{t('Elige la tabla, qué calcular y cómo agrupar para ver la tarjeta.')}</p>}
          </div>
        </div>
        <div className="actions">
          <button className="primary" disabled={!ready} onClick={() => onSave({ ...w, query: { ...q, filters: Object.fromEntries(filters.filter(([k]) => k)) } })}>
            {t('Listo')}
          </button>
          <button type="button" onClick={onCancel}>{t('Cancelar')}</button>
        </div>
      </div>
    </div>
  )
}

/** Con qué perfiles se comparte (solo el dueño o un administrador): "puede ver" o "puede editar". */
function ShareEditor({ value, onChange }: { value: DashboardShare[]; onChange: (v: DashboardShare[]) => void }) {
  const t = useT()
  const profiles = useQuery({ queryKey: ['dashboards', 'profiles'], queryFn: api.dashboardProfiles })
  const name = (id: string) => profiles.data?.find(p => p.id === id)?.name ?? id
  const free = (profiles.data ?? []).filter(p => !value.some(s => s.profile === p.id))
  return (
    <section className="card">
      <h2>{t('Compartir')}</h2>
      <p className="hint">{t('Privado hasta que lo compartes. Cada persona ve las tarjetas con sus propios permisos.')}</p>
      {profiles.error && <div className="alert"><ErrorText error={profiles.error} /></div>}
      <ul className="share-list">
        {value.map((s, i) => (
          <li key={s.profile} className="rule-head">
            <strong>{name(s.profile)}</strong>
            <select value={s.canEdit ? 'edit' : 'view'} aria-label={t('Permiso')}
              onChange={e => onChange(value.map((x, j) => j === i ? { ...x, canEdit: e.target.value === 'edit' } : x))}>
              <option value="view">{t('Puede ver')}</option><option value="edit">{t('Puede editar')}</option>
            </select>
            <button type="button" className="link danger" onClick={() => onChange(value.filter((_, j) => j !== i))}>{t('Quitar')}</button>
          </li>
        ))}
      </ul>
      {free.length > 0 && (
        <SearchSelect value="" ariaLabel={t('Compartir con un perfil')} placeholder={`+ ${t('Compartir con un perfil')}`}
          options={free.map(p => ({ value: p.id, label: p.name }))} onChange={v => onChange([...value, { profile: v, canEdit: false }])} />
      )}
      {profiles.data && profiles.data.length === 0 && <p className="muted small">{t('Todavía no hay perfiles: créalos en Seguridad.')}</p>}
    </section>
  )
}
