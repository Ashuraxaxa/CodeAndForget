/** 1 … 28 29 [30] 31 32 … 4000: la primera, la última y dos a cada lado de la actual. */
export function pageWindow(page: number, pages: number, lastUnknown = false): (number | null)[] {
  const last = Math.max(1, pages)
  const from = Math.max(1, page - 2)
  const to = Math.min(last, page + 2)
  const out: (number | null)[] = []
  if (from > 1) out.push(1)
  if (from > 2) out.push(null)
  for (let n = from; n <= to; n++) out.push(n)
  if (!lastUnknown) {
    if (to < last - 1) out.push(null)
    if (to < last) out.push(last)
  }
  return out
}
