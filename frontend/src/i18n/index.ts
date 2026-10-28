import { createContext, useContext } from 'react'
import { en } from './en'
import { es } from './es'
import { ru } from './ru'

/**
 * Idiomas de la interfaz (P-100). El español es el idioma base: los textos se escriben en español en el código y
 * se usan como clave; `en` y `ru` los traducen (`es` solo trae singulares). Si falta una traducción, se muestra el español
 * (`npm run lint` avisa cuáles faltan).
 */
export type Locale = 'es' | 'en' | 'ru'

export const LOCALES: { id: Locale; name: string }[] = [
  { id: 'es', name: 'Español' },
  { id: 'en', name: 'English' },
  { id: 'ru', name: 'Русский' },
]

/** Formato regional de cada idioma para números (es-CL agrupa también los miles: 1.234). */
const REGION: Record<Locale, string> = { es: 'es-CL', en: 'en-US', ru: 'ru-RU' }
/** Y para fechas: es-CL las escribe 28-10-2026 y con hora a. m./p. m.; es-ES da 28/10/2026 14:05 (P-033). */
const DATE_REGION: Record<Locale, string> = { es: 'es-ES', en: 'en-US', ru: 'ru-RU' }

/** Una traducción, o sus formas según la cantidad (`{n}`): una, pocas (ruso), muchas (ruso) y otras. */
export type Message = string | { one?: string; few?: string; many?: string; other: string }
export type Messages = Record<string, Message>

const DICTIONARIES: Record<Locale, Messages> = { es, en, ru }
const STORAGE_KEY = 'mantenedores.locale'

function initialLocale(): Locale {
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved === 'es' || saved === 'en' || saved === 'ru') return saved
  } catch { /* sin almacenamiento: se usa el del navegador */ }
  const nav = (navigator.languages?.[0] ?? navigator.language ?? 'es').slice(0, 2).toLowerCase()
  return nav === 'en' || nav === 'ru' ? nav : 'es'
}

// Para lo que se formatea fuera de React (fechas en la grilla): el idioma vigente.
let current: Locale = initialLocale()

/** Formato regional del idioma vigente ("es-CL", "en-US", "ru-RU"). */
export const currentRegion = (): string => REGION[current]

/** Formato regional de fechas del idioma vigente: 28/10/2026 (es), 10/28/2026 (en), 28.10.2026 (ru). */
export const currentDateRegion = (): string => DATE_REGION[current]

export type Translate = (text: string, vars?: Record<string, string | number>) => string

/** Traduce un texto escrito en español y reemplaza {variables}. Con `n`, elige la forma según la cantidad. */
export function translate(locale: Locale, text: string, vars?: Record<string, string | number>): string {
  const message: Message | undefined = DICTIONARIES[locale][text]
  let out: string
  if (message === undefined) out = text
  else if (typeof message === 'string') out = message
  else {
    const n = Number(vars?.n ?? 0)
    const form = new Intl.PluralRules(REGION[locale]).select(n) as 'one' | 'few' | 'many' | 'other'
    out = message[form] ?? message.other
  }
  if (!vars) return out
  return out.replace(/\{(\w+)\}/g, (m, k: string) => {
    const v = vars[k]
    if (v === undefined) return m
    return typeof v === 'number' ? v.toLocaleString(REGION[locale]) : v
  })
}

/** Fuera de React: con el idioma vigente. */
export const t: Translate = (text, vars) => translate(current, text, vars)

export interface I18n {
  locale: Locale
  setLocale: (l: Locale) => void
  t: Translate
}

export const I18nContext = createContext<I18n>({ locale: current, setLocale: () => {}, t })

/** Cambia el idioma vigente y lo recuerda entre visitas (lo usa el proveedor). */
export function setCurrentLocale(l: Locale) {
  current = l
  try { localStorage.setItem(STORAGE_KEY, l) } catch { /* no se recuerda entre visitas */ }
}

/** El idioma vigente al cargar la página. */
export const currentLocale = (): Locale => current

export function useI18n(): I18n {
  return useContext(I18nContext)
}

/** Atajo: la función de traducción del idioma vigente (se vuelve a renderizar al cambiarlo). */
export function useT(): Translate {
  return useContext(I18nContext).t
}

export { I18nProvider, LanguageSelect } from './components'
