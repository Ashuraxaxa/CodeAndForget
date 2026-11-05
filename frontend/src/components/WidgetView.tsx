import type { AggregateResult, DashboardWidget } from '../api/types'
import { useT } from '../i18n'
import { metricLabel } from '../lib/chartFormat'
import { ErrorText } from './ErrorText'
import { BarChart, DonutChart, LineChart, NumberTile, PivotTable, TopTable } from './charts'

/** Una tarjeta ya calculada: la usan el dashboard y la vista previa del editor (P-129, P-130). */
export function WidgetView({ widget, data, isLoading, error }: {
  widget: DashboardWidget; data?: AggregateResult; isLoading?: boolean; error?: unknown
}) {
  const t = useT()
  const bucket = widget.query.groupBy[0]?.bucket
  const metric = metricLabel(t, widget)
  const rows = data?.rows ?? []

  let body: React.ReactNode = null
  if (isLoading) body = <div className="chart-skeleton" aria-busy="true" />
  else if (error) body = <div className="widget-error"><ErrorText error={error} /></div>
  else if (!data) body = null
  else if (widget.type !== 'Number' && rows.length === 0) body = <p className="muted small">{t('Sin datos en este período.')}</p>
  else switch (widget.type) {
    case 'Number': body = <NumberTile value={rows[0]?.value} caption={metric} />; break
    case 'Bar': body = <BarChart rows={rows} bucket={bucket} />; break
    case 'Line': body = <LineChart rows={rows} bucket={bucket} />; break
    case 'Pie': body = <DonutChart rows={rows} bucket={bucket} />; break
    case 'Top': body = <TopTable rows={rows} bucket={bucket} groupLabel={widget.query.groupBy[0]?.column ?? ''} valueLabel={metric} />; break
    case 'Pivot': body = <PivotTable rows={rows} bucket={bucket} bucket2={widget.query.groupBy[1]?.bucket} />; break
    default: body = <p className="muted small">{t('Este tipo de tarjeta llega en una próxima versión.')}</p>
  }

  return (
    <section className={`card widget widget-${widget.size.toLowerCase()}`}>
      <header className="widget-head">
        <h2 title={widget.title}>{widget.title || metric}</h2>
        {widget.type !== 'Number' && <span className="muted small">{metric}</span>}
      </header>
      {body}
      {data?.truncated && widget.type !== 'Top' && <p className="muted small">{t('Se muestran los primeros {n} grupos.', { n: rows.length })}</p>}
    </section>
  )
}
