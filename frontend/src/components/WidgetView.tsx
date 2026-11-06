import { Download } from 'lucide-react'
import { isEmbed, notifyHost } from '../lib/embed'
import { useNavigate } from 'react-router-dom'
import type { AggregateResult, AggregateRow, DashboardWidget } from '../api/types'
import { useT, type Translate } from '../i18n'
import { keyLabel, metricLabel } from '../lib/chartFormat'
import { ErrorText } from './ErrorText'
import { BarChart, DonutChart, LineChart, MapChart, NumberTile, PivotTable, TopTable } from './charts'

/**
 * Una tarjeta ya calculada: la usan el dashboard y la vista previa del editor (P-129, P-130). En el dashboard además
 * descarga sus datos en CSV y, en barras, dona y top N, un clic abre la grilla filtrada por ese grupo (P-131).
 */
export function WidgetView({ widget, data, isLoading, error, interactive = false }: {
  widget: DashboardWidget; data?: AggregateResult; isLoading?: boolean; error?: unknown; interactive?: boolean
}) {
  const t = useT()
  const navigate = useNavigate()
  const bucket = widget.query.groupBy[0]?.bucket
  const metric = metricLabel(t, widget)
  const rows = data?.rows ?? []
  // Solo se filtra la grilla por un grupo que es una columna tal cual (no un período de fechas).
  // Embebida (P-138), el clic en un grupo se avisa a la app anfitriona en vez de abrir la grilla.
  const drill = interactive && widget.query.groupBy.length === 1 && !bucket
    ? (r: AggregateRow) => navigate(gridUrl(widget, r))
    : isEmbed && widget.query.groupBy.length > 0
      ? (r: AggregateRow) => notifyHost('celdo:click', { widget: widget.id, title: widget.title, key: r.key, label: r.label, value: r.value })
      : undefined

  let body: React.ReactNode = null
  if (isLoading) body = <div className="chart-skeleton" aria-busy="true" />
  else if (error) body = <div className="widget-error"><ErrorText error={error} /></div>
  else if (!data) body = null
  else if (widget.type !== 'Number' && rows.length === 0) body = <p className="muted small">{t('Sin datos en este período.')}</p>
  else switch (widget.type) {
    case 'Number': body = <NumberTile value={rows[0]?.value} caption={metric} />; break
    case 'Bar': body = <BarChart rows={rows} bucket={bucket} onPick={drill} />; break
    case 'Line': body = <LineChart rows={rows} bucket={bucket} />; break
    case 'Pie': body = <DonutChart rows={rows} bucket={bucket} onPick={drill} />; break
    case 'Top': body = <TopTable rows={rows} bucket={bucket} groupLabel={widget.query.groupBy[0]?.column ?? ''} valueLabel={metric} onPick={drill} />; break
    case 'Pivot': body = <PivotTable rows={rows} bucket={bucket} bucket2={widget.query.groupBy[1]?.bucket} />; break
    case 'Map': body = <MapChart rows={rows} />; break
  }

  return (
    <section className={`card widget widget-${widget.size.toLowerCase()}`}>
      <header className="widget-head">
        <div>
          <h2 title={widget.title}>{widget.title || metric}</h2>
          {widget.type !== 'Number' && <span className="muted small">{metric}</span>}
        </div>
        {interactive && data && rows.length > 0 && (
          <button type="button" className="icon-button" title={t('Descargar CSV')} aria-label={t('Descargar CSV')}
            onClick={() => downloadCsv(t, widget, rows, metric)}><Download size={15} aria-hidden /></button>
        )}
      </header>
      {body}
      {data?.truncated && widget.type !== 'Top' && <p className="muted small">{t('Se muestran los primeros {n} grupos.', { n: rows.length })}</p>}
      {drill && rows.length > 0 && <p className="muted small">{t('Toca un grupo para ver sus registros.')}</p>}
    </section>
  )
}

/** La grilla de la tabla con los filtros de la tarjeta y el grupo elegido (f.<columna>=valor, como la grilla). */
function gridUrl(w: DashboardWidget, r: AggregateRow): string {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(w.query.filters)) if (k) p.set(`f.${k}`, v)
  p.set(`f.${w.query.groupBy[0].column}`, r.key == null ? '' : String(r.key))
  return `/c/${encodeURIComponent(w.connection)}/t/${encodeURIComponent(w.schema)}/${encodeURIComponent(w.table)}?${p}`
}

/** CSV con separador ";" y BOM, que Excel abre bien en español. */
function downloadCsv(t: Translate, w: DashboardWidget, rows: AggregateRow[], metric: string) {
  const g = w.query.groupBy
  const head = [...g.map(x => x.column), metric]
  const cell = (v: unknown) => { const s = v == null ? '' : String(v); return /[";\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s }
  const lines = [head, ...rows.map(r => [
    ...(g[0] ? [keyLabel(t, r.key, r.label, g[0].bucket)] : []),
    ...(g[1] ? [keyLabel(t, r.key2, r.label2, g[1].bucket)] : []),
    r.value,
  ])].map(l => l.map(cell).join(';'))
  const blob = new Blob(['\ufeff' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' })
  const a = document.createElement('a')
  a.href = URL.createObjectURL(blob)
  a.download = `${(w.title || metric).replace(/[\\/:*?"<>|]+/g, '_')}.csv`
  // En el documento: algunos navegadores ignoran el nombre de un enlace suelto.
  document.body.appendChild(a)
  a.click()
  a.remove()
  setTimeout(() => URL.revokeObjectURL(a.href), 1000)
}
