import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { I18nContext, LOCALES, currentLocale, setCurrentLocale, translate, useI18n, type I18n, type Locale } from '.'

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(currentLocale)
  const setLocale = useCallback((l: Locale) => {
    setCurrentLocale(l)
    setLocaleState(l)
  }, [])
  useEffect(() => { document.documentElement.lang = locale }, [locale])
  const value = useMemo<I18n>(() => ({
    locale,
    setLocale,
    t: (text, vars) => translate(locale, text, vars),
  }), [locale, setLocale])
  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>
}

/** Selector de idioma (barra superior y login). */
export function LanguageSelect() {
  const { locale, setLocale, t } = useI18n()
  return (
    <select className="lang" value={locale} aria-label={t('Idioma')} onChange={e => setLocale(e.target.value as Locale)}>
      {LOCALES.map(l => <option key={l.id} value={l.id}>{l.name}</option>)}
    </select>
  )
}
