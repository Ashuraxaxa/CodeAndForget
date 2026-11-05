import { BadgeCheck } from 'lucide-react'
import { Link } from 'react-router-dom'
import { useT } from '../i18n'
import { isPlanError } from '../lib/planError'

/**
 * El texto de un error y, si es del plan, el botón "Ver planes" que lleva a la página Plan (P-122): ahí se ve qué
 * incluye cada plan, se activa la licencia o se empieza la prueba Pro.
 */
export function ErrorText({ error }: { error: unknown }) {
  const t = useT()
  if (!error) return null
  const message = error instanceof Error ? error.message : String(error)
  if (!isPlanError(error)) return <>{message}</>
  return (
    <span className="plan-error">
      <span>{message}</span>
      <Link to="/plan" className="button small"><BadgeCheck size={15} aria-hidden />{t('Ver planes')}</Link>
    </span>
  )
}
