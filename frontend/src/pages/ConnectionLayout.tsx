import { useMemo, useState } from 'react'
import { Eye, Menu, RefreshCw, Search, Table2 } from 'lucide-react'
import { NavLink, Outlet, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { labelOf } from '../api/types'
import { SchemaChangesBanner } from '../components/SchemaChangesBanner'
import { useT } from '../i18n'

/** Barra lateral con las tablas descubiertas de la conexión + área de trabajo. */
export function ConnectionLayout() {
  const t = useT()
  const { conn = '' } = useParams()
  const qc = useQueryClient()
  const [filter, setFilter] = useState('')
  const [menuOpen, setMenuOpen] = useState(false)
  const { data, isLoading, error } = useQuery({ queryKey: ['tables', conn], queryFn: () => api.tables(conn) })

  const refresh = useMutation({
    mutationFn: () => api.refresh(conn),
    onSuccess: s => {
      qc.setQueryData(['tables', conn], s)
      qc.invalidateQueries({ queryKey: ['meta', conn] })
      qc.invalidateQueries({ queryKey: ['diff', conn] })
    },
  })

  const groups = useMemo(() => {
    const f = filter.toLowerCase()
    const map = new Map<string, NonNullable<typeof data>['tables']>()
    for (const tb of data?.tables ?? []) {
      if (tb.hidden) continue
      if (f && !`${tb.schema}.${tb.name} ${labelOf(tb)}`.toLowerCase().includes(f)) continue
      // El grupo del ajuste reemplaza al esquema en el menú.
      const g = tb.group ?? tb.schema
      map.set(g, [...(map.get(g) ?? []), tb])
    }
    for (const list of map.values()) list.sort((a, b) => labelOf(a).localeCompare(labelOf(b)))
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b))
  }, [data, filter])

  return (
    <div className={`workspace ${menuOpen ? 'menu-open' : ''}`}>
      <aside className="sidebar">
        <div className="sidebar-head">
          <label className="input-icon">
            <Search size={15} aria-hidden />
            <input placeholder={t('Buscar tabla…')} value={filter} onChange={e => setFilter(e.target.value)} />
          </label>
          <button className="icon-button" title={t('Volver a leer el esquema')} aria-label={t('Volver a leer el esquema')}
            onClick={() => refresh.mutate()} disabled={refresh.isPending}>
            <RefreshCw size={16} className={refresh.isPending ? 'spin' : ''} aria-hidden />
          </button>
        </div>
        {isLoading && <p className="muted pad">{t('Leyendo esquema…')}</p>}
        {error && <div className="alert pad">{(error as Error).message}</div>}
        {data && <p className="muted small pad">{t('{n} tablas y vistas', { n: data.tables.filter(tb => !tb.hidden).length })}</p>}
        <nav>
          {groups.map(([group, tables]) => (
            <div key={group} className="nav-group">
              <div className="nav-schema">{group}</div>
              {tables.map(tb => (
                <NavLink
                  key={`${tb.schema}.${tb.name}`}
                  title={`${tb.schema}.${tb.name}`}
                  to={`/c/${conn}/t/${encodeURIComponent(tb.schema)}/${encodeURIComponent(tb.name)}`}
                  className="nav-item"
                  onClick={() => setMenuOpen(false)}
                >
                  {tb.kind === 'View' ? <Eye size={15} className="nav-icon" aria-hidden /> : <Table2 size={15} className="nav-icon" aria-hidden />}
                  <span className="nav-label">{labelOf(tb)}</span>
                  {tb.kind === 'View' && <span className="tag">{t('vista')}</span>}
                  {tb.kind === 'Table' && !tb.hasPrimaryKey && <span className="tag">{t('sin PK')}</span>}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
      </aside>
      <section className="content">
        <button className="menu-toggle" onClick={() => setMenuOpen(o => !o)}><Menu size={16} aria-hidden />{t('Tablas')}</button>
        <SchemaChangesBanner conn={conn} />
        <Outlet />
      </section>
    </div>
  )
}

export function PickTable() {
  const t = useT()
  return <div className="empty">{t('Elige una tabla en el menú para ver su mantenedor.')}</div>
}
