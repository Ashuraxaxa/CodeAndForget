import { useState } from 'react'
import { useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { labelOf, type ForeignKey, type Row } from '../api/types'
import { Grid } from '../pages/TablePage'
import { useT } from '../i18n'

/**
 * Maestro-detalle (P-014): una pestaña por tabla hija, con su grilla filtrada por la FK al registro.
 * Desde aquí, "Nuevo" abre el alta con la FK completada y bloqueada, y al guardar vuelve a este registro.
 */
export function ChildTabs({ conn, row, relations }: {
  conn: string
  row: Row
  relations: ForeignKey[]
}) {
  const [active, setActive] = useState(0)
  const location = useLocation()
  const fk = relations[Math.min(active, relations.length - 1)]
  // Si una tabla hija llega por dos FK distintas (p. ej. origen y destino), la pestaña dice cuál.
  const repeated = (f: ForeignKey) => relations.filter(x => x.schema === f.schema && x.table === f.table).length > 1

  return (
    <div className="child-tabs">
      <div className="tabs">
        {relations.map((f, i) => (
          <ChildTabTitle key={f.name} conn={conn} fk={f} detail={repeated(f)} active={f === fk} onClick={() => setActive(i)} />
        ))}
      </div>
      <ChildGrid key={fk.name} conn={conn} row={row} fk={fk} returnTo={location.pathname + location.search} />
    </div>
  )
}

function ChildTabTitle({ conn, fk, detail, active, onClick }: {
  conn: string; fk: ForeignKey; detail: boolean; active: boolean; onClick: () => void
}) {
  // La etiqueta de la hija sale de su definición (ya en caché si se abrió antes).
  const meta = useQuery({ queryKey: ['meta', conn, fk.schema, fk.table], queryFn: () => api.table(conn, fk.schema, fk.table) })
  const name = meta.data ? labelOf(meta.data) : fk.table
  return (
    <button className={active ? 'tab active' : 'tab'} onClick={onClick} title={`${fk.schema}.${fk.table} (${fk.columns.join(', ')})`}>
      {name}{detail && <span className="muted small"> · {fk.columns.join(', ')}</span>}
    </button>
  )
}

function ChildGrid({ conn, row, fk, returnTo }: { conn: string; row: Row; fk: ForeignKey; returnTo: string }) {
  const t = useT()
  const meta = useQuery({ queryKey: ['meta', conn, fk.schema, fk.table], queryFn: () => api.table(conn, fk.schema, fk.table) })
  if (meta.isLoading) return <p className="muted">{t('Cargando…')}</p>
  if (meta.error) return <div className="alert">{(meta.error as Error).message}</div>

  // Valores de la FK: las columnas referenciadas del registro padre (sirve también para FK compuestas).
  const fixed: Record<string, string> = {}
  for (let i = 0; i < fk.columns.length; i++) {
    const value = row[fk.refColumns[i]]
    if (value === null || value === undefined) {
      return <p className="muted">{t('Este registro no tiene valor en {column}: no puede tener {table}.', { column: fk.refColumns[i], table: labelOf(meta.data!) })}</p>
    }
    fixed[fk.columns[i]] = String(value)
  }
  return <Grid conn={conn} meta={meta.data!} fixed={fixed} returnTo={returnTo} />
}
