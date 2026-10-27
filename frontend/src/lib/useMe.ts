import { useQuery } from '@tanstack/react-query'
import { api, type Permission } from '../api/client'

/** Usuario actual (nombre, roles y permisos). Se pide una vez por sesión. */
export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: api.me, staleTime: Infinity })
}

/**
 * ¿El usuario tiene el permiso? (D-053). Solo decide qué se muestra:
 * el servidor rechaza igual la acción si no lo tiene.
 */
export function useCan(permission: Permission): boolean {
  return useMe().data?.permissions.includes(permission) === true
}
