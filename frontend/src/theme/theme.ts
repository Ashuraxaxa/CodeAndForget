// Tema de la interfaz: Auto (el del sistema), Día o Noche. Se recuerda en el navegador (D-093).
// index.html aplica el guardado antes de dibujar la página, para que no parpadee en claro al abrir de noche.
import { useEffect, useSyncExternalStore } from 'react'

export type ThemePreference = 'system' | 'light' | 'dark'
export const THEME_KEY = 'mantenedores.theme'

const media = () => window.matchMedia('(prefers-color-scheme: dark)')
const listeners = new Set<() => void>()

function read(): ThemePreference {
  try {
    const v = localStorage.getItem(THEME_KEY)
    return v === 'light' || v === 'dark' ? v : 'system'
  } catch {
    return 'system'
  }
}

let current = read()

/** Pone data-theme="light|dark" en <html>: los colores de index.css salen de ahí. */
function apply() {
  const dark = current === 'dark' || (current === 'system' && media().matches)
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
}

export function setTheme(pref: ThemePreference) {
  current = pref
  try {
    if (pref === 'system') localStorage.removeItem(THEME_KEY)
    else localStorage.setItem(THEME_KEY, pref)
  } catch { /* no se recuerda entre visitas */ }
  apply()
  listeners.forEach(l => l())
}

function subscribe(l: () => void) {
  listeners.add(l)
  return () => { listeners.delete(l) }
}

export function useTheme(): [ThemePreference, (p: ThemePreference) => void] {
  const pref = useSyncExternalStore(subscribe, () => current)
  // En Auto, sigue al sistema si cambia mientras la app está abierta.
  useEffect(() => {
    if (pref !== 'system') return
    const m = media()
    m.addEventListener('change', apply)
    return () => m.removeEventListener('change', apply)
  }, [pref])
  return [pref, setTheme]
}

apply()
