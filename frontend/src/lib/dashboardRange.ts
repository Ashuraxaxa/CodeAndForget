/** Período del dashboard (P-129). */
export type RangeId = 'all' | '7d' | '30d' | '90d' | 'month' | 'year'

/** Desde (incluido) y hasta (excluido), como fechas locales AAAA-MM-DD. */
export function rangeOf(id: RangeId): { from?: string; to?: string } {
  if (id === 'all') return {}
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const today = new Date()
  const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1)
  const from = id === 'month' ? new Date(today.getFullYear(), today.getMonth(), 1)
    : id === 'year' ? new Date(today.getFullYear(), 0, 1)
    : new Date(today.getFullYear(), today.getMonth(), today.getDate() - (id === '7d' ? 6 : id === '30d' ? 29 : 89))
  return { from: iso(from), to: iso(tomorrow) }
}

