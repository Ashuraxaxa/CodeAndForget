// Páginas para embeber en otras aplicaciones (P-134, D-113): las mismas pantallas bajo /embed, sin menú.
import { setTheme, type ThemePreference } from '../theme/theme'
import { setCurrentLocale, type Locale } from '../i18n'

/** Subruta donde está publicada la app, tomada de <base href> (p. ej. "/mantenedores"). */
export const appBase = new URL(document.baseURI).pathname.replace(/\/$/, '')

const rest = location.pathname.slice(appBase.length)
/** La página se abrió bajo /embed. */
export const isEmbed = rest === '/embed' || rest.startsWith('/embed/')
/** Base del router: con /embed, los enlaces de las pantallas (/dashboards/…, /c/…) quedan dentro de /embed. */
export const routerBase = isEmbed ? `${appBase}/embed` : appBase

const THEMES: Record<string, ThemePreference> = { claro: 'light', light: 'light', oscuro: 'dark', dark: 'dark', sistema: 'system', system: 'system' }

/**
 * Tema e idioma pedidos por la app anfitriona (?tema=oscuro&idioma=en). Se aplican solo a esta página, sin cambiar lo
 * que el usuario eligió en Celdo: con el mismo sitio, el almacenamiento es compartido.
 */
function applyParams() {
  const q = new URLSearchParams(location.search)
  const theme = THEMES[(q.get('tema') ?? q.get('theme') ?? '').toLowerCase()]
  if (theme) setTheme(theme, false)
  const lang = (q.get('idioma') ?? q.get('lang') ?? '').slice(0, 2).toLowerCase()
  if (lang === 'es' || lang === 'en' || lang === 'ru') setCurrentLocale(lang as Locale, false)
}

/** Avisa el alto del contenido a la app anfitriona, para que el iframe no tenga barra de desplazamiento propia. */
function reportHeight() {
  if (window.parent === window) return
  const root = document.getElementById('root')
  if (!root || typeof ResizeObserver === 'undefined') return
  let last = 0
  new ResizeObserver(() => {
    const height = Math.ceil(root.getBoundingClientRect().height)
    if (height === last) return
    last = height
    // El alto no es un dato sensible: se avisa a quien embeba (solo los sitios permitidos pueden, por frame-ancestors).
    window.parent.postMessage({ type: 'celdo:resize', height }, '*')
  }).observe(root)
}

/** Se llama una vez al cargar, antes de dibujar. */
export function startEmbed() {
  if (!isEmbed) return
  document.documentElement.dataset.embed = ''
  applyParams()
  reportHeight()
}

/**
 * Token firmado por la app anfitriona (forma B, P-135): llega en el fragmento (#token=…), que no viaja al servidor ni
 * queda en sus registros. Se lee una vez y se quita de la URL.
 */
export const signedToken: string | null = (() => {
  if (!isEmbed) return null
  const hash = location.hash.replace(/^#/, '')
  const params = new URLSearchParams(hash)
  const token = params.get('token')
  if (!token) return null
  params.delete('token')
  const rest = params.toString()
  try { history.replaceState(history.state, '', location.pathname + location.search + (rest ? `#${rest}` : '')) } catch { /* sin history */ }
  return token
})()

/** Dentro de un iframe (de otro sitio o del mismo). */
export const inFrame = (() => { try { return window.self !== window.top } catch { return true } })()
