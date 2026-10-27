import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'

/** Usuario actual (nombre, roles, si es administrador). Se pide una vez por sesión. */
export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: api.me, staleTime: Infinity })
}
