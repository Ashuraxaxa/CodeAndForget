import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import type { AggregateRow, DateBucket } from '../api/types'
import { useT } from '../i18n'
import { axisLabel, formatNumber, keyLabel, lang } from '../lib/chartFormat'

/**
 * Gráficos de las tarjetas de dashboard (P-129, D-111), en SVG propio. Colores: variables --series-1…8 (paleta
 * categórica validada en los dos temas, orden fijo), texto con los colores de texto, grilla tenue. Cada marca tiene
 * tooltip al pasar el cursor y las tarjetas muestran sus valores, así el color nunca es lo único que informa.
 */

const num = (v: unknown) => { const n = typeof v === 'number' ? v : Number(v); return Number.isFinite(n) ? n : 0 }
const series = (i: number) => `var(--series-${(i % 8) + 1})`

/** Ancho real del contenedor, para dibujar el SVG sin estirar el texto. */
function useWidth<T extends HTMLElement>(): [React.RefObject<T | null>, number] {
  const ref = useRef<T>(null)
  const [w, setW] = useState(0)
  useLayoutEffect(() => {
    if (!ref.current) return
    const ro = new ResizeObserver(([e]) => setW(Math.round(e.contentRect.width)))
    ro.observe(ref.current)
    return () => ro.disconnect()
  }, [])
  return [ref, w]
}

/** Tooltip que sigue al puntero dentro de la tarjeta. */
function useTip() {
  const [tip, setTip] = useState<{ x: number; y: number; content: ReactNode } | null>(null)
  const node = tip && (
    <div className="chart-tip" role="tooltip" style={{ left: tip.x, top: tip.y }}>{tip.content}</div>
  )
  const show = (e: React.PointerEvent | React.FocusEvent, content: ReactNode, box?: DOMRect) => {
    const host = (e.currentTarget as Element).closest('.chart')!.getBoundingClientRect()
    const p = 'clientX' in e ? { x: e.clientX, y: e.clientY } : { x: (box?.left ?? host.left) + (box?.width ?? 0) / 2, y: box?.top ?? host.top }
    setTip({ x: Math.min(p.x - host.left + 12, host.width - 160), y: p.y - host.top - 12, content })
  }
  return { node, show, hide: () => setTip(null) }
}

// ---------- Número ----------

export function NumberTile({ value, caption }: { value: unknown; caption?: string }) {
  return (
    <div className="chart chart-number">
      <span className="chart-number-value">{formatNumber(value)}</span>
      {caption && <span className="muted small">{caption}</span>}
    </div>
  )
}

// ---------- Barras (horizontales: las etiquetas se leen enteras, también en el teléfono) ----------

export function BarChart({ rows, bucket, onPick }: { rows: AggregateRow[]; bucket?: DateBucket | null; onPick?: (r: AggregateRow) => void }) {
  const t = useT()
  const tip = useTip()
  const max = Math.max(0, ...rows.map(r => num(r.value)))
  return (
    <div className="chart chart-bars" role="list">
      {rows.map((r, i) => {
        const label = keyLabel(t, r.key, r.label, bucket)
        const pct = max > 0 ? Math.max(0, num(r.value)) / max * 100 : 0
        return (
          <div key={i} className={`bar-row ${onPick ? 'pickable' : ''}`} role="listitem" tabIndex={0}
            onClick={onPick ? () => onPick(r) : undefined} onKeyDown={onPick ? e => { if (e.key === 'Enter') onPick(r) } : undefined}
            onPointerMove={e => tip.show(e, <><b>{label}</b><span>{formatNumber(r.value)}</span></>)}
            onPointerLeave={tip.hide} onBlur={tip.hide}
            onFocus={e => tip.show(e, <><b>{label}</b><span>{formatNumber(r.value)}</span></>, e.currentTarget.getBoundingClientRect())}>
            <span className="bar-label" title={label}>{label}</span>
            <span className="bar-track"><span className="bar-fill" style={{ width: `${pct}%` }} /></span>
            <span className="bar-value">{formatNumber(r.value, true)}</span>
          </div>
        )
      })}
      {tip.node}
    </div>
  )
}

// ---------- Líneas (series de tiempo: una sola serie, el título la nombra) ----------

export function LineChart({ rows, bucket }: { rows: AggregateRow[]; bucket?: DateBucket | null }) {
  const t = useT()
  const [ref, width] = useWidth<HTMLDivElement>()
  const [hover, setHover] = useState<number | null>(null)
  const h = 200, padL = 44, padR = 12, padT = 12, padB = 26
  const values = rows.map(r => num(r.value))
  const max = Math.max(...values, 0), min = Math.min(...values, 0)
  const span = max - min || 1
  const w = Math.max(0, width - padL - padR)
  const x = (i: number) => padL + (rows.length <= 1 ? w / 2 : (i / (rows.length - 1)) * w)
  const y = (v: number) => padT + (1 - (v - min) / span) * (h - padT - padB)
  const ticks = [min, min + span / 2, max]
  const path = values.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ')
  // Eje x: etiquetas cortas (la fecha sin "Semana del") y solo las que caben sin pisarse; la última siempre.
  const axisText = (i: number) => axisLabel(t, rows[i].key, rows[i].label, bucket)
  const shown = new Set<number>()
  {
    const width = (i: number) => axisText(i).length * 6.5 + 12
    const last = rows.length - 1
    if (rows.length) { shown.add(0); if (last > 0) shown.add(last) }
    let edge = x(0) + width(0) / 2
    for (let i = 1; i < last; i++) {
      const half = width(i) / 2
      if (x(i) - half > edge && x(i) + half < x(last) - width(last)) { shown.add(i); edge = x(i) + half }
    }
    if (last > 0 && x(last) - width(last) < x(0) + width(0)) shown.delete(last)
  }

  function move(e: React.PointerEvent<SVGRectElement>) {
    const box = e.currentTarget.getBoundingClientRect()
    const rel = (e.clientX - box.left) / Math.max(1, box.width)
    setHover(Math.max(0, Math.min(rows.length - 1, Math.round(rel * (rows.length - 1)))))
  }

  const hv = hover != null ? rows[hover] : null
  return (
    <div className="chart chart-line" ref={ref}>
      {width > 0 && (
        <svg width={width} height={h} role="img" aria-label={t('Gráfico de líneas')}>
          {ticks.map((v, i) => (
            <g key={i}>
              <line x1={padL} x2={width - padR} y1={y(v)} y2={y(v)} className="chart-grid" />
              <text x={padL - 6} y={y(v)} className="chart-axis" textAnchor="end" dominantBaseline="middle">{formatNumber(v, true)}</text>
            </g>
          ))}
          {rows.map((_, i) => shown.has(i) && (
            <text key={i} x={x(i)} y={h - 8} className="chart-axis" textAnchor={i === 0 ? 'start' : i === rows.length - 1 ? 'end' : 'middle'}>
              {axisText(i)}
            </text>
          ))}
          <path d={path} className="chart-line-path" style={{ stroke: series(0) }} />
          {rows.length === 1 && <circle cx={x(0)} cy={y(values[0])} r={4} style={{ fill: series(0) }} />}
          {hover != null && (
            <g>
              <line x1={x(hover)} x2={x(hover)} y1={padT} y2={h - padB} className="chart-crosshair" />
              <circle cx={x(hover)} cy={y(values[hover])} r={5} className="chart-dot" style={{ fill: series(0) }} />
            </g>
          )}
          <rect x={padL} y={padT} width={w} height={h - padT - padB} fill="transparent"
            onPointerMove={move} onPointerLeave={() => setHover(null)} />
        </svg>
      )}
      {hv && hover != null && (
        <div className="chart-tip" role="tooltip" style={{ left: Math.min(x(hover) + 10, width - 160), top: Math.max(0, y(values[hover]) - 44) }}>
          <b>{keyLabel(t, hv.key, hv.label, bucket)}</b><span>{formatNumber(hv.value)}</span>
        </div>
      )}
    </div>
  )
}

// ---------- Dona (hasta 8 porciones; el resto se junta en "Otros") ----------

export function DonutChart({ rows, bucket, onPick }: { rows: AggregateRow[]; bucket?: DateBucket | null; onPick?: (r: AggregateRow) => void }) {
  const t = useT()
  const tip = useTip()
  const sorted = [...rows].sort((a, b) => num(b.value) - num(a.value))
  const top: { label: string; value: number; row?: AggregateRow }[] = sorted.slice(0, sorted.length > 8 ? 7 : 8).map(r => ({ label: keyLabel(t, r.key, r.label, bucket), value: Math.max(0, num(r.value)), row: r }))
  if (sorted.length > 8) top.push({ label: t('Otros'), value: sorted.slice(7).reduce((s, r) => s + Math.max(0, num(r.value)), 0) })
  const total = top.reduce((s, x) => s + x.value, 0)
  const r = 70, inner = 46, c = 80
  let angle = -Math.PI / 2
  const arcs = top.map((s, i) => {
    const a = total > 0 ? (s.value / total) * Math.PI * 2 : 0
    const start = angle, end = angle + a
    angle = end
    const large = a > Math.PI ? 1 : 0
    const p = (rad: number, rr: number) => `${(c + rr * Math.cos(rad)).toFixed(2)},${(c + rr * Math.sin(rad)).toFixed(2)}`
    // Una sola porción del 100 %: dos medias vueltas, porque un arco no puede empezar y terminar en el mismo punto.
    const d = a >= Math.PI * 2 - 1e-6
      ? `M${p(start, r)} A${r},${r} 0 1 1 ${p(start + Math.PI, r)} A${r},${r} 0 1 1 ${p(start, r)} M${p(start, inner)} A${inner},${inner} 0 1 0 ${p(start + Math.PI, inner)} A${inner},${inner} 0 1 0 ${p(start, inner)} Z`
      : `M${p(start, r)} A${r},${r} 0 ${large} 1 ${p(end, r)} L${p(end, inner)} A${inner},${inner} 0 ${large} 0 ${p(start, inner)} Z`
    return { ...s, d, color: series(i) }
  })
  const pct = (v: number) => total > 0 ? new Intl.NumberFormat(lang(), { style: 'percent', maximumFractionDigits: 1 }).format(v / total) : '—'
  return (
    <div className="chart chart-donut">
      <svg viewBox="0 0 160 160" width="160" height="160" role="img" aria-label={t('Gráfico de dona')}>
        {arcs.map((a, i) => (
          <path key={i} d={a.d} style={{ fill: a.color, cursor: onPick && a.row ? 'pointer' : undefined }} className="chart-slice" fillRule="evenodd"
            onClick={onPick && a.row ? () => onPick(a.row!) : undefined}
            onPointerMove={e => tip.show(e, <><b>{a.label}</b><span>{formatNumber(a.value)} · {pct(a.value)}</span></>)}
            onPointerLeave={tip.hide} />
        ))}
        <text x={c} y={c} textAnchor="middle" dominantBaseline="middle" className="chart-donut-total">{formatNumber(total, true)}</text>
      </svg>
      <ul className="chart-legend">
        {arcs.map((a, i) => (
          <li key={i}><i style={{ background: a.color }} aria-hidden /><span className="legend-label" title={a.label}>{a.label}</span>
            <span className="legend-value">{formatNumber(a.value, true)} · {pct(a.value)}</span></li>
        ))}
      </ul>
      {tip.node}
    </div>
  )
}

// ---------- Top N (tabla con la posición) ----------

export function TopTable({ rows, bucket, groupLabel, valueLabel, onPick }: { rows: AggregateRow[]; bucket?: DateBucket | null; groupLabel: string; valueLabel: string; onPick?: (r: AggregateRow) => void }) {
  const t = useT()
  return (
    <div className="chart chart-table">
      <table className="grid compact">
        <thead><tr><th>#</th><th>{groupLabel}</th><th className="num">{valueLabel}</th></tr></thead>
        <tbody>
          {rows.map((r, i) => (
            <tr key={i} className={onPick ? 'pickable' : undefined} onClick={onPick ? () => onPick(r) : undefined}><td className="muted">{i + 1}</td><td>{keyLabel(t, r.key, r.label, bucket)}</td><td className="num">{formatNumber(r.value)}</td></tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ---------- Tabla dinámica: filas por la primera agrupación, columnas por la segunda ----------

export function PivotTable({ rows, bucket, bucket2 }: { rows: AggregateRow[]; bucket?: DateBucket | null; bucket2?: DateBucket | null }) {
  const t = useT()
  const id = (k: unknown) => JSON.stringify(k ?? null)
  const rowKeys = new Map<string, string>(), colKeys = new Map<string, string>()
  const cells = new Map<string, unknown>()
  for (const r of rows) {
    rowKeys.set(id(r.key), keyLabel(t, r.key, r.label, bucket))
    colKeys.set(id(r.key2), keyLabel(t, r.key2, r.label2, bucket2))
    cells.set(`${id(r.key)}|${id(r.key2)}`, r.value)
  }
  return (
    <div className="chart chart-table">
      <table className="grid compact">
        <thead><tr><th />{[...colKeys.values()].map((c, i) => <th key={i} className="num">{c}</th>)}</tr></thead>
        <tbody>
          {[...rowKeys].map(([rk, label]) => (
            <tr key={rk}><th scope="row">{label}</th>
              {[...colKeys.keys()].map(ck => <td key={ck} className="num">{cells.has(`${rk}|${ck}`) ? formatNumber(cells.get(`${rk}|${ck}`)) : ''}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ---------- Mapa (P-131): puntos por latitud y longitud, sobre una cuadrícula propia, sin servicios externos ----------

export function MapChart({ rows }: { rows: AggregateRow[] }) {
  const t = useT()
  const [ref, width] = useWidth<HTMLDivElement>()
  const tip = useTip()
  const pts = rows
    .map(r => ({ lat: num(r.key), lon: num(r.key2), v: Math.max(0, num(r.value)), label: r.label ?? r.label2 }))
    .filter(p => Math.abs(p.lat) <= 90 && Math.abs(p.lon) <= 180 && !(p.lat === 0 && p.lon === 0))
  const h = 260, pad = 16
  if (pts.length === 0) return <p className="muted small">{t('No hay latitudes y longitudes válidas.')}</p>
  // Encuadre: los puntos con un margen, sin deformar (misma escala en x e y, proyección equirectangular).
  let [minLat, maxLat] = [Math.min(...pts.map(p => p.lat)), Math.max(...pts.map(p => p.lat))]
  let [minLon, maxLon] = [Math.min(...pts.map(p => p.lon)), Math.max(...pts.map(p => p.lon))]
  const spanLat = Math.max(maxLat - minLat, 0.02), spanLon = Math.max(maxLon - minLon, 0.02)
  minLat -= spanLat * 0.1; maxLat += spanLat * 0.1; minLon -= spanLon * 0.1; maxLon += spanLon * 0.1
  const w = Math.max(0, width - pad * 2)
  const scale = Math.min(w / (maxLon - minLon), (h - pad * 2) / (maxLat - minLat))
  const ox = pad + (w - (maxLon - minLon) * scale) / 2, oy = pad + (h - pad * 2 - (maxLat - minLat) * scale) / 2
  const x = (lon: number) => ox + (lon - minLon) * scale
  const y = (lat: number) => oy + (maxLat - lat) * scale
  const vmax = Math.max(...pts.map(p => p.v), 1)
  const r = (v: number) => 3 + Math.sqrt(v / vmax) * 12
  // Líneas de latitud y longitud cada "paso" redondo (1, 2, 5, 10…), como referencia.
  const step = (span: number) => { const raw = span / 4; const p = 10 ** Math.floor(Math.log10(raw)); return [1, 2, 5, 10].map(m => m * p).find(s => s >= raw) ?? raw }
  const sl = step(maxLat - minLat), so = step(maxLon - minLon)
  const lats: number[] = [], lons: number[] = []
  for (let v = Math.ceil(minLat / sl) * sl; v <= maxLat; v += sl) lats.push(v)
  for (let v = Math.ceil(minLon / so) * so; v <= maxLon; v += so) lons.push(v)
  const fmt = (v: number) => new Intl.NumberFormat(lang(), { maximumFractionDigits: 4 }).format(v)
  return (
    <div className="chart chart-map" ref={ref}>
      {width > 0 && (
        <svg width={width} height={h} role="img" aria-label={t('Mapa de puntos')}>
          <rect x={0} y={0} width={width} height={h} className="chart-map-bg" rx={6} />
          {lats.map(v => <g key={`a${v}`}><line x1={0} x2={width} y1={y(v)} y2={y(v)} className="chart-grid" /><text x={4} y={y(v) - 3} className="chart-axis">{fmt(v)}°</text></g>)}
          {lons.map(v => <g key={`o${v}`}><line y1={0} y2={h} x1={x(v)} x2={x(v)} className="chart-grid" /><text x={x(v) + 3} y={h - 4} className="chart-axis">{fmt(v)}°</text></g>)}
          {[...pts].sort((a, b) => b.v - a.v).map((p, i) => (
            <circle key={i} cx={x(p.lon)} cy={y(p.lat)} r={r(p.v)} className="chart-map-dot" style={{ fill: 'var(--series-1)' }}
              onPointerMove={e => tip.show(e, <><b>{p.label ?? `${fmt(p.lat)}, ${fmt(p.lon)}`}</b><span>{formatNumber(p.v)}</span></>)}
              onPointerLeave={tip.hide} />
          ))}
        </svg>
      )}
      {tip.node}
    </div>
  )
}
