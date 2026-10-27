import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { LABEL_SUFFIX, labelOf, type Column, type Row, type TableMeta } from '../api/types'
import { displayValue, keyQuery } from '../lib/format'
import { useDebounce } from '../lib/useDebounce'
import { useMe } from '../lib/useMe'

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

function Grid({ conn, meta }: { conn: string; meta: TableMeta }) {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const isAdmin = useMe().data?.isAdmin === true
  const [params] = useSearchParams()
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
  const columns = meta.columns.filter(c => c.isSelectable && !c.hidden && c.showInGrid)
  const fkByColumn = new Map(meta.foreignKeys.filter(f => f.columns.length === 1).map(f => [f.columns[0], f]))
  const base = `/c/${conn}/t/${encodeURIComponent(meta.schema)}/${encodeURIComponent(meta.name)}`

  const listParams = {
    page, pageSize,
    sort: sort ? `${sort.desc ? '-' : ''}${sort.col}` : undefined,
    q: debouncedSearch || undefined,
    filters: debouncedFilters,
  }
  const rows = useQuery({
    queryKey: ['rows', conn, meta.schema, meta.name, listParams],
    queryFn: () => api.list(conn, meta.schema, meta.name, listParams),
    placeholderData: keepPreviousData,
  })

  const remove = useMutation({
    mutationFn: (row: Row) => api.remove(conn, meta.schema, meta.name, keyQuery(meta, row)),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['rows', conn, meta.schema, meta.name] }),
    onError: err => alert((err as Error).message),
  })

  function toggleSort(col: string) {
    setSort(s => (!s || s.col !== col ? { col, desc: false } : !s.desc ? { col, desc: true } : null))
  }

  const total = rows.data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / pageSize))

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1>{labelOf(meta)}</h1>
          <div className="muted small">
            {meta.schema} · {total} registros
            {meta.kind === 'View' && <span className="tag">vista · solo lectura</span>}
            {meta.kind === 'Table' && !meta.hasPrimaryKey && <span className="tag">sin PK · solo lectura</span>}
            {meta.kind === 'Table' && meta.hasPrimaryKey && meta.forcedReadOnly && <span className="tag">solo lectura</span>}
          </div>
        </div>
        <div className="actions">
          <input className="search" placeholder="Buscar en textos…" value={search} onChange={e => setSearch(e.target.value)} />
          {isAdmin && <Link className="button" to={`${base}/config`} title="Ajustar etiquetas, columnas y validaciones">⚙ Configurar</Link>}
          {meta.isEditable && <Link className="button primary" to={`${base}/new`}>Nuevo</Link>}
        </div>
      </div>

      {meta.referencedBy.length > 0 && (
        <div className="related muted small">
          Referenciada por:{' '}
          {meta.referencedBy.map(r => (
            <Link key={r.name} className="chip" to={`/c/${conn}/t/${encodeURIComponent(r.schema)}/${encodeURIComponent(r.table)}`}>
              {r.table}
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
                    <FilterInput column={c} value={filters[c.name] ?? ''} onChange={v => setFilters(f => ({ ...f, [c.name]: v }))} />
                  )}
                </th>
              ))}
              {meta.isEditable && <th />}
            </tr>
          </thead>
          <tbody className={rows.isFetching ? 'loading' : undefined}>
            {rows.data?.rows.map((row, i) => (
              <tr key={meta.hasPrimaryKey ? keyQuery(meta, row) : i}
                  onDoubleClick={() => meta.isEditable && navigate(`${base}/edit?${keyQuery(meta, row)}`)}>
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
                    <Link to={`${base}/edit?${keyQuery(meta, row)}`}>Editar</Link>
                    <button
                      className="link danger"
                      onClick={() => confirm('¿Eliminar este registro?') && remove.mutate(row)}
                    >
                      Eliminar
                    </button>
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
        <span>Página {page} de {pages}</span>
        <button disabled={page >= pages} onClick={() => setPage(p => p + 1)}>Siguiente ›</button>
        <select value={pageSize} onChange={e => setPageSize(Number(e.target.value))}>
          {PAGE_SIZES.map(s => <option key={s} value={s}>{s} por página</option>)}
        </select>
      </div>
    </div>
  )
}

function FilterInput({ column: c, value, onChange }: { column: Column; value: string; onChange: (v: string) => void }) {
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
      placeholder={contains ? 'contiene…' : '='}
      type={c.type === 'Date' ? 'date' : 'text'}
      onChange={e => onChange(e.target.value)}
    />
  )
}
