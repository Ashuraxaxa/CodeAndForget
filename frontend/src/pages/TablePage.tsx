import { ArrowDown, ArrowUp, Download, Eye, KeyRound, Pencil, Plus, Search, Settings2, Trash2, Upload } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, isConcurrencyError } from '../api/client'
import { LABEL_SUFFIX, SIZE_SUFFIX, VERSION_FIELD, helpTextOf, labelOf, type Column, type Row, type TableMeta, type TransferFormat } from '../api/types'
import { FIXED_PREFIX, RETURN_PARAM, displayValue, keyQuery } from '../lib/format'
import { useDebounce } from '../lib/useDebounce'
import { useCan } from '../lib/useMe'
import { useTableLabels } from '../lib/useTableLabels'
import { TreeView } from '../components/TreeView'
import { useT } from '../i18n'
import { StoredImage } from '../components/ImagePreview'
import { Pager } from '../components/Pager'
import { SearchSelect } from '../components/Combo'

const PAGE_SIZES = [10, 25, 50, 100]
/** Formatos de exportación (D-057). */
const EXPORT_FORMATS = [
  { value: 'csv', label: 'CSV' }, { value: 'xlsx', label: 'Excel' }, { value: 'json', label: 'JSON' }, { value: 'xml', label: 'XML' },
]

/** Grilla genérica: orden, filtros por columna, búsqueda, paginación y acciones por fila. */
export function TablePage() {
  const { conn = '', schema = '', table = '' } = useParams()
  const meta = useQuery({ queryKey: ['meta', conn, schema, table], queryFn: () => api.table(conn, schema, table) })
  const t = useT()

  if (meta.isLoading) return <p className="muted">{t('Cargando definición…')}</p>
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
  const t = useT()
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
  // Paginación por clave (P-098): con el orden de la PK, la página siguiente se pide desde la última fila de la actual.
  // Se guarda el "desde" de cada página para volver atrás; cambiar filtros, orden o tamaño lo reinicia.
  const [cursors, setCursors] = useState<Record<number, string>>({})
  useEffect(() => { setPage(1); setCursors({}) }, [debouncedSearch, debouncedFilters, pageSize, sort])
  const keyset = !sort && !meta.defaultSort && meta.primaryKey.length === 1

  // El orden viene del servidor (ajuste Order); las ocultas y las que van solo en el formulario no se muestran.
  const columns = meta.columns.filter(c => c.isSelectable && !c.hidden && c.showInGrid && !(fixed && c.name in fixed))
  // Archivos con el control "image" (P-090): miniatura en la grilla.
  const imageCols = meta.columns.filter(c => c.isFile && !c.hidden && c.showInGrid && c.widget === 'image')
  const fkByColumn = new Map(meta.foreignKeys.filter(f => f.columns.length === 1).map(f => [f.columns[0], f]))
  // La etiqueta de una FK, también compuesta (P-030), va en su columna de etiqueta.
  const fkByLabelColumn = new Map(meta.foreignKeys.map(f => [f.labelColumn, f]))
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
    after: keyset && page > 1 ? cursors[page] : undefined,
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
        alert(t('Otra persona cambió este registro mientras lo veía. La lista se actualizó: revíselo y vuelva a intentar.'))
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
    ? t('más de {n} registros', { n: total })
    : `${rows.data?.totalIsEstimate ? '≈ ' : ''}${t('{n} registros', { n: total })}`
  const capped = rows.data?.totalIsCapped === true
  // Con el total cortado o estimado (D-072) no se sabe cuál es la última página: se puede seguir mientras la
  // página venga llena, y "Última" no se ofrece (P-107).
  const lastUnknown = capped || rows.data?.totalIsEstimate === true
  const full = (rows.data?.rows.length ?? 0) === pageSize
  const pages = lastUnknown
    ? (full ? Math.max(page + 1, Math.ceil(total / pageSize)) : page)
    : Math.max(1, Math.ceil(total / pageSize))

  /** Ir a una página. La siguiente usa la clave de la última fila (D-089); un salto pide la página directo. */
  function goTo(n: number) {
    const list = rows.data?.rows ?? []
    const last = list[list.length - 1]
    if (n === page + 1 && keyset && last) setCursors(c => ({ ...c, [page + 1]: String(last[meta.primaryKey[0]]) }))
    setPage(n)
  }
  // El indicador aparece solo si la espera se nota (la clase lo muestra con un pequeño retraso).
  const loading = rows.isFetching
  const openHint = meta.isEditable ? (meta.canUpdate ? t('Doble clic para editar') : t('Doble clic para ver')) : undefined

  return (
    <div className={embedded ? 'embedded-grid' : 'page'}>
      {embedded ? (
        <div className="page-header">
          <div className="muted small">{totalText}</div>
          <div className="actions">
            <label className="input-icon search"><Search size={15} aria-hidden /><input placeholder={t('Buscar en textos…')} value={search} onChange={e => setSearch(e.target.value)} /></label>
            {meta.canCreate && <Link className="button primary" to={newUrl}><Plus size={16} aria-hidden />{t('Nuevo')}</Link>}
          </div>
        </div>
      ) : (
      <div className="page-header">
        <div>
          <h1>{labelOf(meta)}</h1>
          <div className="muted small">
            {meta.schema} · <span title={rows.data?.totalIsEstimate ? t('Estimado por la base de datos: la tabla es grande y contar todas sus filas sería lento')
              : capped ? t('La tabla es grande: se deja de contar en este número. Afine la búsqueda o los filtros.') : undefined}>{totalText}</span>
            {meta.kind === 'View' && <span className="tag">{t('vista · solo lectura')}</span>}
            {meta.kind === 'Table' && !meta.hasPrimaryKey && <span className="tag">{t('sin PK · solo lectura')}</span>}
            {meta.kind === 'Table' && meta.hasPrimaryKey && meta.forcedReadOnly && <span className="tag">{t('solo lectura')}</span>}
            {meta.isEditable && !meta.canCreate && !meta.canUpdate && !meta.canDelete && <span className="tag">{t('solo consulta')}</span>}
          </div>
        </div>
        <div className="actions">
          <label className="input-icon search"><Search size={15} aria-hidden /><input placeholder={t('Buscar en textos…')} value={search} onChange={e => setSearch(e.target.value)} /></label>
          {meta.canExport && <label className="input-icon export-wrap"><Download size={15} aria-hidden />
            <SearchSelect className="export" value="" disabled={exporting} title={t('Exportar lo que muestra la grilla (filtros, búsqueda y orden)')}
              placeholder={exporting ? t('Exportando…') : t('Exportar')} ariaLabel={t('Exportar')}
              onChange={v => v && exportAs(v as TransferFormat)} options={EXPORT_FORMATS} /></label>}
          {meta.canImport && <Link className="button" to={`${base}/import`}><Upload size={16} aria-hidden />{t('Importar')}</Link>}
          {canConfigure && <Link className="button" to={`${base}/config`} title={t('Ajustar etiquetas, columnas y validaciones')}><Settings2 size={16} aria-hidden />{t('Configurar')}</Link>}
          {meta.canCreate && <Link className="button primary" to={newUrl}><Plus size={16} aria-hidden />{t('Nuevo')}</Link>}
        </div>
      </div>
      )}

      {!embedded && meta.selfReference && (
        <div className="tabs">
          <button className={view === 'grid' ? 'tab active' : 'tab'} onClick={() => setView('grid')}>{t('Grilla')}</button>
          <button className={view === 'tree' ? 'tab active' : 'tab'} onClick={() => setView('tree')}>
            {t('Árbol')} <span className="muted small">· {meta.selfReference.columns[0]}</span>
          </button>
        </div>
      )}

      {showTree ? <TreeView conn={conn} meta={meta} /> : <>
      {!embedded && meta.referencedBy.length > 0 && (
        <div className="related muted small">
          {t('Referenciada por:')}{' '}
          {meta.referencedBy.map(r => (
            <Link key={r.name} className="chip" to={`/c/${conn}/t/${encodeURIComponent(r.schema)}/${encodeURIComponent(r.table)}`}>
              {tableLabel(r.schema, r.table)}
            </Link>
          ))}
        </div>
      )}

      {rows.error && <div className="alert">{(rows.error as Error).message}</div>}

      <div className={`grid-area ${loading ? 'is-loading' : ''}`} aria-busy={loading}>
      {loading && <div className="grid-loader" role="status"><span className="grid-loader-pill"><span className="spinner" />{t('Cargando…')}</span></div>}
      <div className="table-wrap">
        <table className="grid">
          <thead>
            <tr>
              {meta.isEditable && <th className="actions-col no-sort" aria-label={t('Acciones')} />}
              {columns.map(c => (
                <th
                  key={c.name}
                  onClick={() => c.isSortable && toggleSort(c.name)}
                  className={c.isSortable ? undefined : 'no-sort'}
                  title={`${c.label ? c.name + ' · ' : ''}${c.nativeType}${c.isNullable ? '' : ' · ' + t('obligatorio')}${c.isSortable ? '' : ' · ' + t('no se puede ordenar')}${helpTextOf(c) ? '\n' + helpTextOf(c) : ''}`}
                >
                  {c.isPrimaryKey && <span className="pk" title={t('Clave primaria')}><KeyRound size={13} aria-hidden /></span>}
                  {labelOf(c)}
                  {sort?.col === c.name && (sort.desc ? <ArrowDown size={13} className="sort-icon" aria-hidden /> : <ArrowUp size={13} className="sort-icon" aria-hidden />)}
                </th>
              ))}
              {imageCols.map(c => <th key={c.name} className="no-sort">{labelOf(c)}</th>)}
            </tr>
            <tr className="filters">
              {meta.isEditable && <th className="actions-col" />}
              {columns.map(c => (
                <th key={c.name}>
                  {c.isFilterable && (
                    <FilterInput column={c} isForeignKey={fkByColumn.has(c.name)} value={filters[c.name] ?? ''} onChange={v => setFilters(f => ({ ...f, [c.name]: v }))} />
                  )}
                </th>
              ))}
              {imageCols.map(c => <th key={c.name} />)}
            </tr>
          </thead>
          <tbody className={loading ? 'loading' : undefined}>
            {rows.data?.rows.map((row, i) => (
              <tr key={meta.hasPrimaryKey ? keyQuery(meta, row) : i} title={openHint}
                  onDoubleClick={() => meta.isEditable && navigate(editUrl(row))}>
                {meta.isEditable && (
                  <td className="actions-col">
                    {/* Sin permiso de edición el formulario se abre de solo lectura (y muestra las pestañas hijas). */}
                    <span className="row-actions">
                      <Link className="button icon-button sm" to={editUrl(row)} title={meta.canUpdate ? t('Editar') : t('Ver')}
                        aria-label={meta.canUpdate ? t('Editar') : t('Ver')}>
                        {meta.canUpdate ? <Pencil size={15} aria-hidden /> : <Eye size={15} aria-hidden />}
                      </Link>
                      {meta.canDelete && (
                        <button
                          className="icon-button sm danger"
                          title={t('Eliminar')} aria-label={t('Eliminar')}
                          onClick={() => confirm(t('¿Eliminar este registro?')) && remove.mutate(row)}
                        >
                          <Trash2 size={15} aria-hidden />
                        </button>
                      )}
                    </span>
                  </td>
                )}
                {columns.map(c => {
                  const fk = fkByLabelColumn.get(c.name)
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
                {imageCols.map(c => (
                  <td key={c.name} className="t-image">
                    {row[c.name + SIZE_SUFFIX] != null && meta.hasPrimaryKey &&
                      <StoredImage conn={conn} schema={meta.schema} table={meta.name} recordKey={keyQuery(meta, row)} column={c.name} className="thumb" />}
                  </td>
                ))}
              </tr>
            ))}
            {rows.data?.rows.length === 0 && (
              <tr><td colSpan={columns.length + imageCols.length + 1} className="empty">{t('Sin registros')}</td></tr>
            )}
          </tbody>
        </table>
      </div>
      </div>

      <Pager page={page} pages={pages} lastUnknown={lastUnknown} busy={loading} onGo={goTo}>
        <SearchSelect className="narrow" value={String(pageSize)} ariaLabel={t('Registros por página')} onChange={v => setPageSize(Number(v))}
          options={PAGE_SIZES.map(n => ({ value: String(n), label: t('{n} por página', { n }) }))} />
      </Pager>
      </>}
    </div>
  )
}

function FilterInput({ column: c, isForeignKey, value, onChange }: {
  column: Column; isForeignKey: boolean; value: string; onChange: (v: string) => void
}) {
  const t = useT()
  if (c.type === 'Boolean') {
    return (
      <SearchSelect value={value} onChange={onChange} ariaLabel={labelOf(c)}
        options={[{ value: '', label: t('Todos') }, { value: 'true', label: t('Sí') }, { value: 'false', label: t('No') }]} />
    )
  }
  if (c.type === 'Enum') {
    return (
      <SearchSelect value={value} onChange={onChange} ariaLabel={labelOf(c)}
        options={[{ value: '', label: t('Todos') }, ...(c.enumValues ?? []).map(v => ({ value: v, label: v }))]} />
    )
  }
  const contains = c.type === 'String' || c.type === 'Text'
  return (
    <input
      value={value}
      // Una FK se filtra por el id o por el texto que muestra (P-032).
      placeholder={contains ? t('contiene…') : isForeignKey ? t('id o nombre…') : '='}
      type={c.type === 'Date' ? 'date' : 'text'}
      onChange={e => onChange(e.target.value)}
    />
  )
}
