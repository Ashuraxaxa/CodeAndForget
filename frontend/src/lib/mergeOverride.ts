import type { TableOverride } from '../api/types'

/**
 * Suma un ajuste parcial (una sugerencia) al actual, igual que OverrideSuggester.Merge en el servidor:
 * relaciones y filtros se agregan, y en las columnas se completa lo que el usuario no había decidido.
 */
export function mergeOverride(current: TableOverride, patch: TableOverride): TableOverride {
  const vfks = [...(current.virtualForeignKeys ?? [])]
  for (const v of patch.virtualForeignKeys ?? [])
    if (!vfks.some(x => x.columns.join().toLowerCase() === v.columns.join().toLowerCase())) vfks.push(v)
  const filters = { ...(current.lookupFilters ?? {}) }
  for (const [k, map] of Object.entries(patch.lookupFilters ?? {})) filters[k] = { ...(filters[k] ?? {}), ...map }
  const columns = { ...(current.columns ?? {}) }
  for (const [name, co] of Object.entries(patch.columns ?? {})) {
    const c = columns[name] ?? {}
    columns[name] = { ...c, ...Object.fromEntries(Object.entries(co).filter(([k, v]) => v != null && (c as Record<string, unknown>)[k] == null)) }
  }
  return {
    ...current,
    virtualForeignKeys: vfks.length ? vfks : undefined,
    lookupFilters: Object.keys(filters).length ? filters : undefined,
    columns: Object.keys(columns).length ? columns : undefined,
  }
}
