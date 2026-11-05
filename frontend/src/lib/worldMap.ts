import { feature } from 'topojson-client'
import type { Topology, GeometryCollection } from 'topojson-specification'

/**
 * Contornos de los países para la tarjeta de mapa (P-131), de Natural Earth 1:50m (dominio público, paquete
 * world-atlas), con sus nombres en español, inglés y ruso (de i18n-iso-countries, por código ISO numérico). Van con
 * la app, sin servicios externos (D-022), y se descargan solo cuando un dashboard tiene un mapa.
 */
export type Bbox = [number, number, number, number]
export interface Ring { points: [number, number][]; bbox: Bbox }
export interface Country { id: string; name: string; rings: Ring[] }

let cache: Promise<Country[]> | null = null

const bboxOf = (pts: [number, number][]): Bbox => {
  let a = Infinity, b = Infinity, c = -Infinity, d = -Infinity
  for (const [x, y] of pts) { if (x < a) a = x; if (y < b) b = y; if (x > c) c = x; if (y > d) d = y }
  return [a, b, c, d]
}

export function loadCountries(language: string): Promise<Country[]> {
  cache ??= (async () => {
    const [{ default: url }, { default: names }] = await Promise.all([
      import('world-atlas/countries-50m.json?url'),
      import('./countryNames.json'),
    ])
    const topo = (await (await fetch(url)).json()) as Topology<{ countries: GeometryCollection<{ name: string }> }>
    const idx = language.startsWith('en') ? 1 : language.startsWith('ru') ? 2 : 0
    return feature(topo, topo.objects.countries).features.map(f => {
      const g = f.geometry
      const polys: number[][][][] = g?.type === 'Polygon' ? [g.coordinates] : g?.type === 'MultiPolygon' ? g.coordinates : []
      const rings = polys.flat().map(r => {
        const points = r.map(([x, y]) => [x, y] as [number, number])
        return { points, bbox: bboxOf(points) }
      })
      const id = String(f.id ?? '')
      const local = (names as Record<string, string[]>)[id]?.[idx]
      return { id, name: local ?? f.properties?.name ?? '', rings }
    })
  })().catch(e => { cache = null; throw e })
  return cache
}
