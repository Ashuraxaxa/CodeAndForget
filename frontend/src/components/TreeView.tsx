import { useState, type DragEvent } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, ApiError } from '../api/client'
import type { TableMeta, TreeNode } from '../api/types'
import { FIXED_PREFIX, RETURN_PARAM } from '../lib/format'
import { useT } from '../i18n'

const DRAG_TYPE = 'application/x-mantenedores-nodo'

/**
 * Vista de árbol para tablas con autorreferencia (P-016), p. ej. empleados y su jefe.
 * Carga un nivel por vez: los hijos se piden al expandir un nodo. Con permiso de edición, un nodo se mueve
 * arrastrándolo sobre otro (P-089); el servidor rechaza un ciclo.
 */
export function TreeView({ conn, meta }: { conn: string; meta: TableMeta }) {
  const t = useT()
  const qc = useQueryClient()
  const roots = useQuery({
    queryKey: ['tree', conn, meta.schema, meta.name, null],
    queryFn: () => api.tree(conn, meta.schema, meta.name),
  })
  const parentCol = meta.selfReference!.columns[0]
  const parent = meta.columns.find(c => c.name === parentCol)
  const canMove = meta.canUpdate && parent !== undefined && !parent.isReadOnly
  const [overRoot, setOverRoot] = useState(false)

  const move = useMutation({
    mutationFn: ({ node, to }: { node: string; to: unknown }) =>
      api.update(conn, meta.schema, meta.name, new URLSearchParams({ [meta.primaryKey[0]]: node }).toString(), { [parentCol]: to }),
    onSettled: () => {
      qc.invalidateQueries({ queryKey: ['tree', conn, meta.schema, meta.name] })
      qc.invalidateQueries({ queryKey: ['rows', conn, meta.schema, meta.name] })
    },
    // Un ciclo llega como error del campo padre: se muestra ese motivo, no el genérico.
    onError: e => alert((e instanceof ApiError && Object.values(e.fieldErrors)[0]) || (e as Error).message),
  })
  const drop = (to: unknown) => (e: DragEvent) => {
    e.preventDefault()
    e.stopPropagation()
    setOverRoot(false)
    const node = e.dataTransfer.getData(DRAG_TYPE)
    if (node && String(to) !== node) move.mutate({ node, to })
  }

  if (roots.isLoading) return <p className="muted">{t('Cargando…')}</p>
  if (roots.error) return <div className="alert">{(roots.error as Error).message}</div>
  if (roots.data?.length === 0) return <div className="empty">{t('Sin registros')}</div>
  return (
    <>
      {canMove && (
        <p className={`muted small tree-root${overRoot ? ' over' : ''}`}
          onDragOver={e => { if (parent!.isNullable && e.dataTransfer.types.includes(DRAG_TYPE)) { e.preventDefault(); setOverRoot(true) } }}
          onDragLeave={() => setOverRoot(false)} onDrop={drop(null)}>
          {parent!.isNullable
            ? t('Arrastre un registro sobre otro para moverlo debajo, o hasta aquí para dejarlo en el primer nivel.')
            : t('Arrastre un registro sobre otro para moverlo debajo.')}
        </p>
      )}
      <ul className={`tree${move.isPending ? ' loading' : ''}`}>
        {roots.data?.map(n => <Node key={String(n.value)} conn={conn} meta={meta} node={n} canMove={canMove} onDrop={drop} />)}
      </ul>
    </>
  )
}

function Node({ conn, meta, node, canMove, onDrop }: {
  conn: string; meta: TableMeta; node: TreeNode; canMove: boolean; onDrop: (to: unknown) => (e: DragEvent) => void
}) {
  const t = useT()
  const [open, setOpen] = useState(false)
  const [over, setOver] = useState(false)
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
      <div className={`tree-node${over ? ' over' : ''}`} draggable={canMove}
        onDragStart={e => { e.stopPropagation(); e.dataTransfer.setData(DRAG_TYPE, String(node.value)); e.dataTransfer.effectAllowed = 'move' }}
        onDragOver={e => { if (canMove && e.dataTransfer.types.includes(DRAG_TYPE)) { e.preventDefault(); e.stopPropagation(); setOver(true) } }}
        onDragLeave={() => setOver(false)}
        onDrop={e => { setOver(false); onDrop(node.value)(e) }}>
        <button className="tree-toggle" disabled={!node.hasChildren} onClick={() => setOpen(o => !o)}
          aria-label={open ? t('Contraer') : t('Expandir')}>
          {node.hasChildren ? (open ? '▾' : '▸') : '·'}
        </button>
        {meta.isEditable
          ? <Link to={`${base}/edit?${key}`} draggable={false}>{node.label ?? String(node.value)}</Link>
          : <span>{node.label ?? String(node.value)}</span>}
        {meta.canCreate && <Link className="muted small" to={`${base}/new?${child}`} draggable={false} title={t('Agregar un registro debajo de este')}>＋ {t('hijo')}</Link>}
      </div>
      {open && node.hasChildren && (
        children.isLoading ? <p className="muted small tree-indent">{t('Cargando…')}</p>
          : <ul className="tree">{children.data?.map(n => <Node key={String(n.value)} conn={conn} meta={meta} node={n} canMove={canMove} onDrop={onDrop} />)}</ul>
      )}
    </li>
  )
}
