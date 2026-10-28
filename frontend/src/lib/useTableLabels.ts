import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import { labelOf } from '../api/types'

/**
 * Nombre visible de cada tabla de la conexión (ajuste `label`, P-084), para mostrarlo en las relaciones en vez del
 * nombre de la base. Usa la misma consulta del menú, así que no pide nada nuevo.
 */
export function useTableLabels(conn: string): (schema: string, table: string) => string {
  const tables = useQuery({ queryKey: ['tables', conn], queryFn: () => api.tables(conn), staleTime: 60_000 })
  const labels = new Map((tables.data?.tables ?? []).map(t => [`${t.schema}.${t.name}`.toLowerCase(), labelOf(t)]))
  return (schema, table) => labels.get(`${schema}.${table}`.toLowerCase()) ?? table
}
