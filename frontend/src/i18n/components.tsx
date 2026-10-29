import { SearchSelect } from '../components/Combo'
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
    <SearchSelect className="lang" value={locale} ariaLabel={t('Idioma')} onChange={v => setLocale(v as Locale)}
      options={LOCALES.map(l => ({ value: l.id, label: l.name, hint: l.id }))} />
  )
}
