import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, isConcurrencyError } from '../api/client'
import { LABEL_SUFFIX, VERSION_FIELD, labelOf, type Column, type Row, type TableMeta, type TransferFormat } from '../api/types'
import { FIXED_PREFIX, RETURN_PARAM, displayValue, keyQuery } from '../lib/format'
import { useDebounce } from '../lib/useDebounce'
import { useCan } from '../lib/useMe'
import { useTableLabels } from '../lib/useTableLabels'
import { TreeView } from '../components/TreeView'

const PAGE_SIZES = [10, 25, 50, 100]

/** Grilla genérica: orden, filtros por columna, búsqueda, paginación y acciones por fila. */
export function TablePage() {
  const { conn = '', schema = '', table = '' } = useParams()
  const meta = useQuery({ queryKey: ['meta', conn, schema, table], queryFn: () => api.table(conn, schema, table) })

  if (meta.isLoading) return <p className="muted">Cargando definición…</p>
  if (meta.error) return <div className="alert">{(meta.error as Error).message}</div>
  // La key reinicia el estado (filtros, página) al cambiar de tabla.
  return <Grid key={`${conn}/${schema}/${table}`} conn={conn} meta={meta.data!} />
}

/**
 * Grilla de una tabla. Con <c>fixed</c> se muestra dentro de otro registro (maestro-detalle, P-014):
 * filtra por igualdad exacta, oculta esas columnas y los formularios que abre vuelven a <c>returnTo</c>.
 */
export function Grid({ conn, meta, fixed, returnTo }: {
  conn: string
  meta: TableMeta
  fixed?: Record<string, string>
  returnTo?: string
}) {
  const embedded = fixed !== undefined
  const qc = useQueryClient()
  const navigate = useNavigate()
  const canConfigure = useCan('ConfigureTables')
  const [params, setParams] = useSearchParams()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [sort, setSort] = useState<{ col: string; desc: boolean } | null>(null)
  const [search, setSearch] = useState('')
  // Los filtros iniciales pueden venir en la URL (?f.categoria_id=2), útil para enlazar desde otra tabla.
  const [filters, setFilters] = useState<Record<string, string>>(() =>
    Object.fromEntries([...params.entries()].filter(([k]) => k.startsWith('f.')).map(([k, v]) => [k.slice(2), v])))

  const debouncedSearch = useDebounce(search)
  const debouncedFilters = useDebounce(filters)
  useEffect(() => setPage(1), [debouncedSearch, debouncedFilters, pageSize])

  // El orden viene del servidor (ajuste Order); las ocultas y las que van solo en el formulario no se muestran.
  const columns = meta.columns.filter(c => c.isSelectable && !c.hidden && c.showInGrid && !(fixed && c.name in fixed))
  const fkByColumn = new Map(meta.foreignKeys.filter(f => f.columns.length === 1).map(f => [f.columns[0], f]))
  const tableLabel = useTableLabels(conn)
  const base = `/c/${conn}/t/${encodeURIComponent(meta.schema)}/${encodeURIComponent(meta.name)}`
  // Lo que se agrega a los enlaces de alta y edición: los valores fijos y a dónde volver.
  const extra = embedded
    ? '&' + new URLSearchParams([
      ...Object.entries(fixed!).map(([k, v]) => [FIXED_PREFIX + k, v]),
      ...(returnTo ? [[RETURN_PARAM, returnTo]] : []),
    ]).toString()
    : ''
  const editUrl = (row: Row) => `${base}/edit?${keyQuery(meta, row)}${extra}`
  const newUrl = embedded ? `${base}/new?${extra.slice(1)}` : `${base}/new`

  const listParams = {
    page, pageSize,
    sort: sort ? `${sort.desc ? '-' : ''}${sort.col}` : undefined,
    q: debouncedSearch || undefined,
    filters: debouncedFilters,
    exact: fixed,
  }
  const rows = useQuery({
    queryKey: ['rows', conn, meta.schema, meta.name, listParams],
    queryFn: () => api.list(conn, meta.schema, meta.name, listParams),
    placeholderData: keepPreviousData,
  })

  const remove = useMutation({
    mutationFn: (row: Row) => api.remove(conn, meta.schema, meta.name, keyQuery(meta, row), row[VERSION_FIELD]),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['rows', conn, meta.schema, meta.name] }),
    onError: err => {
      // Otra persona lo cambió (D-069): se recarga la grilla para que decida con los datos al día.
      if (isConcurrencyError(err)) {
        qc.invalidateQueries({ queryKey: ['rows', conn, meta.schema, meta.name] })
        alert('Otra persona cambió este registro mientras lo veía. La lista se actualizó: revíselo y vuelva a intentar.')
      } else alert((err as Error).message)
    },
  })

  const [exporting, setExporting] = useState(false)
  // Vista de árbol para tablas con autorreferencia (P-016).
  // Queda en la URL (?vista=arbol) para volver a ella después de editar.
  const view = params.get('vista') === 'arbol' ? 'tree' : 'grid'
  const setView = (v: 'grid' | 'tree') => setParams(p => {
    const n = new URLSearchParams(p)
    if (v === 'tree') n.set('vista', 'arbol'); else n.delete('vista')
    return n
  }, { replace: true })
  const showTree = !embedded && meta.selfReference != null && view === 'tree'
  async function exportAs(format: TransferFormat) {
    setExporting(true)
    try {
      await api.exportTable(conn, meta.schema, meta.name, format,
        { sort: listParams.sort, q: listParams.q, filters: listParams.filters, exact: fixed })
    } catch (e) {
      alert((e as Error).message)
    } finally {
      setExporting(false)
    }
  }

  function toggleSort(col: string) {
    setSort(s => (!s || s.col !== col ? { col, desc: false } : !s.desc ? { col, desc: true } : null))
  }

  const total = rows.data?.total ?? 0
  // En tablas grandes sin filtros el total es una estimación del motor (D-072).
  const totalText = rows.data?.totalIsCapped
    ? `más de ${total.toLocaleString('es')} registros`
    : `${rows.data?.totalIsEstimate ? '≈ ' : ''}${total.toLocaleString('es')} registros`
  const capped = rows.data?.totalIsCapped === true
  // Con el total cortado no se sabe cuál es la última página: se puede seguir mientras la página venga llena.
  const pages = capped
    ? Math.max(page + ((rows.data?.rows.length ?? 0) === pageSize ? 1 : 0), Math.ceil(total / pageSize))
    : Math.max(1, Math.ceil(total / pageSize))

  return (
    <div className={embedded ? 'embedded-grid' : 'page'}>
      {embedded ? (
        <div className="page-header">
          <div className="muted small">{totalText}</div>
          <div className="actions">
            <input className="search" placeholder="Buscar en textos…" value={search} onChange={e => setSearch(e.target.value)} />
            {meta.canCreate && <Link className="button primary" to={newUrl}>Nuevo</Link>}
          </div>
        </div>
      ) : (
      <div className="page-header">
        <div>
          <h1>{labelOf(meta)}</h1>
          <div className="muted small">
            {meta.schema} · <span title={rows.data?.totalIsEstimate ? 'Estimado por la base de datos: la tabla es grande y contar todas sus filas sería lento'
              : capped ? 'La tabla es grande: se deja de contar en este número. Afine la búsqueda o los filtros.' : undefined}>{totalText}</span>
            {meta.kind === 'View' && <span className="tag">vista · solo lectura</span>}
            {meta.kind === 'Table' && !meta.hasPrimaryKey && <span className="tag">sin PK · solo lectura</span>}
            {meta.kind === 'Table' && meta.hasPrimaryKey && meta.forcedReadOnly && <span className="tag">solo lectura</span>}
            {meta.isEditable && !meta.canCreate && !meta.canUpdate && !meta.canDelete && <span className="tag">solo consulta</span>}
          </div>
        </div>
        <div className="actions">
          <input className="search" placeholder="Buscar en textos…" value={search} onChange={e => setSearch(e.target.value)} />
          {meta.canExport && <select className="export" value="" title="Exportar lo que muestra la grilla (filtros, búsqueda y orden)"
            disabled={exporting} onChange={e => e.target.value && exportAs(e.target.value as TransferFormat)}>
            <option value="">{exporting ? 'Exportando…' : '⤓ Exportar'}</option>
            <option value="csv">CSV</option>
            <option value="xlsx">Excel</option>
            <option value="json">JSON</option>
            <option value="xml">XML</option>
          </select>}
          {meta.canImport && <Link className="button" to={`${base}/import`}>⤒ Importar</Link>}
          {canConfigure && <Link className="button" to={`${base}/config`} title="Ajustar etiquetas, columnas y validaciones">⚙ Configurar</Link>}
          {meta.canCreate && <Link className="button primary" to={newUrl}>Nuevo</Link>}
        </div>
      </div>
      )}

      {!embedded && meta.selfReference && (
        <div className="tabs">
          <button className={view === 'grid' ? 'tab active' : 'tab'} onClick={() => setView('grid')}>Grilla</button>
          <button className={view === 'tree' ? 'tab active' : 'tab'} onClick={() => setView('tree')}>
            Árbol <span className="muted small">· {meta.selfReference.columns[0]}</span>
          </button>
        </div>
      )}

      {showTree ? <TreeView conn={conn} meta={meta} /> : <>
      {!embedded && meta.referencedBy.length > 0 && (
        <div className="related muted small">
          Referenciada por:{' '}
          {meta.referencedBy.map(r => (
            <Link key={r.name} className="chip" to={`/c/${conn}/t/${encodeURIComponent(r.schema)}/${encodeURIComponent(r.table)}`}>
              {tableLabel(r.schema, r.table)}
            </Link>
          ))}
        </div>
      )}

      {rows.error && <div className="alert">{(rows.error as Error).message}</div>}

      <div className="table-wrap">
        <table className="grid">
          <thead>
            <tr>
              {columns.map(c => (
                <th
                  key={c.name}
                  onClick={() => c.isSortable && toggleSort(c.name)}
                  className={c.isSortable ? undefined : 'no-sort'}
                  title={`${c.label ? c.name + ' · ' : ''}${c.nativeType}${c.isNullable ? '' : ' · obligatorio'}${c.isSortable ? '' : ' · no se puede ordenar'}${c.helpText ? '\n' + c.helpText : ''}`}
                >
                  {c.isPrimaryKey && <span className="pk">🔑</span>}
                  {labelOf(c)}
                  {sort?.col === c.name && (sort.desc ? ' ▼' : ' ▲')}
                </th>
              ))}
              {meta.isEditable && <th className="actions-col" />}
            </tr>
            <tr className="filters">
              {columns.map(c => (
                <th key={c.name}>
                  {c.isFilterable && (
                    <FilterInput column={c} isForeignKey={fkByColumn.has(c.name)} value={filters[c.name] ?? ''} onChange={v => setFilters(f => ({ ...f, [c.name]: v }))} />
                  )}
                </th>
              ))}
              {meta.isEditable && <th />}
            </tr>
          </thead>
          <tbody className={rows.isFetching ? 'loading' : undefined}>
            {rows.data?.rows.map((row, i) => (
              <tr key={meta.hasPrimaryKey ? keyQuery(meta, row) : i}
                  onDoubleClick={() => meta.isEditable && navigate(editUrl(row))}>
                {columns.map(c => {
                  const fk = fkByColumn.get(c.name)
                  const label = row[c.name + LABEL_SUFFIX]
                  return (
                    <td key={c.name} className={fk ? 't-fk' : `t-${c.type.toLowerCase()}`}>
                      {fk && label != null ? (
                        <span title={String(row[c.name])}>{String(label)}</span>
                      ) : (
                        <span className="cell">{displayValue(c, row[c.name])}</span>
                      )}
                    </td>
                  )
                })}
                {meta.isEditable && (
                  <td className="actions-col">
                    {/* Sin permiso de edición el formulario se abre de solo lectura (y muestra las pestañas hijas). */}
                    <Link to={editUrl(row)}>{meta.canUpdate ? 'Editar' : 'Ver'}</Link>
                    {meta.canDelete && (
                      <button
                        className="link danger"
                        onClick={() => confirm('¿Eliminar este registro?') && remove.mutate(row)}
                      >
                        Eliminar
                      </button>
                    )}
                  </td>
                )}
              </tr>
            ))}
            {rows.data?.rows.length === 0 && (
              <tr><td colSpan={columns.length + 1} className="empty">Sin registros</td></tr>
            )}
          </tbody>
        </table>
      </div>

      <div className="pager">
        <button disabled={page <= 1} onClick={() => setPage(p => p - 1)}>‹ Anterior</button>
        <span>Página {page} {capped ? `de ${pages}+` : `de ${pages}`}</span>
        <button disabled={page >= pages} onClick={() => setPage(p => p + 1)}>Siguiente ›</button>
        <select value={pageSize} onChange={e => setPageSize(Number(e.target.value))}>
          {PAGE_SIZES.map(s => <option key={s} value={s}>{s} por página</option>)}
        </select>
      </div>
      </>}
    </div>
  )
}

function FilterInput({ column: c, isForeignKey, value, onChange }: {
  column: Column; isForeignKey: boolean; value: string; onChange: (v: string) => void
}) {
  if (c.type === 'Boolean') {
    return (
      <select value={value} onChange={e => onChange(e.target.value)}>
        <option value="">Todos</option>
        <option value="true">Sí</option>
        <option value="false">No</option>
      </select>
    )
  }
  if (c.type === 'Enum') {
    return (
      <select value={value} onChange={e => onChange(e.target.value)}>
        <option value="">Todos</option>
        {c.enumValues?.map(v => <option key={v} value={v}>{v}</option>)}
      </select>
    )
  }
  const contains = c.type === 'String' || c.type === 'Text'
  return (
    <input
      value={value}
      // Una FK se filtra por el id o por el texto que muestra (P-032).
      placeholder={contains ? 'contiene…' : isForeignKey ? 'id o nombre…' : '='}
      type={c.type === 'Date' ? 'date' : 'text'}
      onChange={e => onChange(e.target.value)}
    />
  )
}
