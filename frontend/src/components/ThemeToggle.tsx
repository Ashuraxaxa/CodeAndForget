import { Monitor, Moon, Sun } from 'lucide-react'
import { useT } from '../i18n'
import { useTheme, type ThemePreference } from '../theme/theme'

/** Selector Auto / Día / Noche (D-093). */
export function ThemeToggle() {
  const t = useT()
  const [pref, setPref] = useTheme()
  const options: { id: ThemePreference; label: string; icon: typeof Sun }[] = [
    { id: 'system', label: t('Tema automático (el del sistema)'), icon: Monitor },
    { id: 'light', label: t('Tema de día'), icon: Sun },
    { id: 'dark', label: t('Tema de noche'), icon: Moon },
  ]
  return (
    <div className="segmented" role="radiogroup" aria-label={t('Tema')}>
      {options.map(o => (
        <button key={o.id} type="button" role="radio" aria-checked={pref === o.id} title={o.label} aria-label={o.label}
          className={pref === o.id ? 'on' : ''} onClick={() => setPref(o.id)}>
          <o.icon size={15} aria-hidden />
        </button>
      ))}
    </div>
  )
}
