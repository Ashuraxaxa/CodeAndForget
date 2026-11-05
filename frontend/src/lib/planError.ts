import { ApiError } from '../api/client'
import type { Translate } from '../i18n'

/** Error de límite o función del plan (403 con tipo mantenedores:plan, P-116). */
export const isPlanError = (e: unknown): boolean => e instanceof ApiError && e.type === 'mantenedores:plan'

/**
 * Para los errores que se avisan con una ventana del navegador: si es del plan, ofrece abrir la página Plan.
 * <paramref name="navigate"/> es el de react-router, para respetar la ruta base de la app.
 */
export function alertError(error: unknown, t: Translate, navigate?: (to: string) => void, text?: string) {
  const message = text ?? (error instanceof Error ? error.message : String(error))
  if (isPlanError(error) && navigate) {
    if (confirm(`${message}\n\n${t('¿Quieres ver los planes?')}`)) navigate('/plan')
  } else alert(message)
}
