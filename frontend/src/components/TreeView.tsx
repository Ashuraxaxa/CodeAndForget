import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { TableMeta, TreeNode } from '../api/types'
import { FIXED_PREFIX, RETURN_PARAM } from '../lib/format'

/**
 * Vista de árbol para tablas con autorreferencia (P-016), p. ej. empleados y su jefe.
 * Carga un nivel por vez: los hijos se piden al expandir un nodo.
 */
export function TreeView({ conn, meta }: { conn: string; meta: TableMeta }) {
  const roots = useQuery({
    queryKey: ['tree', conn, meta.schema, meta.name, null],
    queryFn: () => api.tree(conn, meta.schema, meta.name),
  })
  if (roots.isLoading) return <p className="muted">Cargando…</p>
  if (roots.error) return <div className="alert">{(roots.error as Error).message}</div>
  if (roots.data?.length === 0) return <div className="empty">Sin registros</div>
  return (
    <ul className="tree">
      {roots.data?.map(n => <Node key={String(n.value)} conn={conn} meta={meta} node={n} />)}
    </ul>
  )
}

function Node({ conn, meta, node }: { conn: string; meta: TableMeta; node: TreeNode }) {
  const [open, setOpen] = useState(false)
  const location = useLocation()
  const children = useQuery({
    queryKey: ['tree', conn, meta.schema, meta.name, node.value],
    queryFn: () => api.tree(conn, meta.schema, meta.name, node.value),
    enabled: open && node.hasChildren,
  })

  const base = `/c/${conn}/t/${encodeURIComponent(meta.schema)}/${encodeURIComponent(meta.name)}`
  const back = location.pathname + location.search
  const key = new URLSearchParams({ [meta.primaryKey[0]]: String(node.value), [RETURN_PARAM]: back })
  const child = new URLSearchParams({ [FIXED_PREFIX + meta.selfReference!.columns[0]]: String(node.value), [RETURN_PARAM]: back })

  return (
    <li>
      <div className="tree-node">
        <button className="tree-toggle" disabled={!node.hasChildren} onClick={() => setOpen(o => !o)}
          aria-label={open ? 'Contraer' : 'Expandir'}>
          {node.hasChildren ? (open ? '▾' : '▸') : '·'}
        </button>
        {meta.isEditable
          ? <Link to={`${base}/edit?${key}`}>{node.label ?? String(node.value)}</Link>
          : <span>{node.label ?? String(node.value)}</span>}
        {meta.canCreate && <Link className="muted small" to={`${base}/new?${child}`} title="Agregar un registro debajo de este">＋ hijo</Link>}
      </div>
      {open && node.hasChildren && (
        children.isLoading ? <p className="muted small tree-indent">Cargando…</p>
          : <ul className="tree">{children.data?.map(n => <Node key={String(n.value)} conn={conn} meta={meta} node={n} />)}</ul>
      )}
    </li>
  )
}
