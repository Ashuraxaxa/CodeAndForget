import type { PlanName } from '../api/types'
import type { Translate } from '../i18n'

/** Dónde se compran los planes (D-105). */
export const PRICING_URL = 'https://celdo.app/#precios'

export function planLabel(t: Translate, plan: PlanName): string {
  return plan === 'Free' ? t('Gratis') : plan === 'Pro' ? 'Pro' : t('Empresa')
}
