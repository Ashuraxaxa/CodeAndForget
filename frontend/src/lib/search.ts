/** "Lácteos" → "lacteos": para comparar sin tildes ni mayúsculas. */
export function normalize(text: string): string {
  return text.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
}

/** ¿Todas las palabras de la búsqueda aparecen en alguno de los textos? Búsqueda vacía = sí. */
export function matches(query: string, ...texts: (string | undefined | null)[]): boolean {
  const words = normalize(query).split(/\s+/).filter(Boolean)
  if (words.length === 0) return true
  const hay = normalize(texts.filter(Boolean).join(' '))
  return words.every(w => hay.includes(w))
}
