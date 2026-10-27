import { useMemo, useState } from 'react'
import { NavLink, Outlet, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { labelOf } from '../api/types'
import { SchemaChangesBanner } from '../components/SchemaChangesBanner'

/** Barra lateral con las tablas descubiertas de la conexión + área de trabajo. */
export function ConnectionLayout() {
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
    for (const t of data?.tables ?? []) {
      if (t.hidden) continue
      if (f && !`${t.schema}.${t.name} ${t.label ?? ''}`.toLowerCase().includes(f)) continue
      // El grupo del ajuste reemplaza al esquema en el menú.
      const g = t.group ?? t.schema
      map.set(g, [...(map.get(g) ?? []), t])
    }
    for (const list of map.values()) list.sort((a, b) => labelOf(a).localeCompare(labelOf(b)))
    return [...map.entries()].sort(([a], [b]) => a.localeCompare(b))
  }, [data, filter])

  return (
    <div className={`workspace ${menuOpen ? 'menu-open' : ''}`}>
      <aside className="sidebar">
        <div className="sidebar-head">
          <input placeholder="Buscar tabla…" value={filter} onChange={e => setFilter(e.target.value)} />
          <button title="Volver a leer el esquema" onClick={() => refresh.mutate()} disabled={refresh.isPending}>
            {refresh.isPending ? '…' : '↻'}
          </button>
        </div>
        {isLoading && <p className="muted pad">Leyendo esquema…</p>}
        {error && <div className="alert pad">{(error as Error).message}</div>}
        {data && <p className="muted small pad">{data.tables.filter(t => !t.hidden).length} tablas y vistas</p>}
        <nav>
          {groups.map(([group, tables]) => (
            <div key={group} className="nav-group">
              <div className="nav-schema">{group}</div>
              {tables.map(t => (
                <NavLink
                  key={`${t.schema}.${t.name}`}
                  title={`${t.schema}.${t.name}`}
                  to={`/c/${conn}/t/${encodeURIComponent(t.schema)}/${encodeURIComponent(t.name)}`}
                  className="nav-item"
                  onClick={() => setMenuOpen(false)}
                >
                  <span>{labelOf(t)}</span>
                  {t.kind === 'View' && <span className="tag">vista</span>}
                  {t.kind === 'Table' && !t.hasPrimaryKey && <span className="tag">sin PK</span>}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
      </aside>
      <section className="content">
        <button className="menu-toggle" onClick={() => setMenuOpen(o => !o)}>☰ Tablas</button>
        <SchemaChangesBanner conn={conn} />
        <Outlet />
      </section>
    </div>
  )
}

export function PickTable() {
  return <div className="empty">Elige una tabla en el menú para ver su mantenedor.</div>
}
