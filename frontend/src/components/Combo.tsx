import { ChevronDown, X } from 'lucide-react'
import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../i18n'
import { matches } from '../lib/search'

/** Opción de un combo. <c>hint</c> es un texto secundario (p. ej. el id o el tipo) que también se busca. */
export interface ComboOption<V = string> {
  value: V
  label: string
  hint?: string
  key?: string
  /** Se ve pero no se puede elegir (p. ej. una conexión que ya tiene regla). */
  disabled?: boolean
}

/** Cuántas opciones se dibujan a la vez; con más, se pide seguir escribiendo. */
const MAX_SHOWN = 200

/**
 * Base de todos los combos de la app (P-108, D-096): se escribe para filtrar y se elige con el mouse o el teclado
 * (↑ ↓ Enter, Esc cierra). La lista va en un portal con posición fija, así no la corta una grilla con scroll.
 * Las opciones llegan ya filtradas: <see cref="SearchSelect"/> filtra en el navegador y el de FK pide al servidor.
 */
export function ComboBox<V>({
  id, value, options, query, onQueryChange, onPick, placeholder, disabled, required, loading, clearable, onClear,
  ariaLabel, title, className, emptyText, footer, freeText,
}: {
  id?: string
  /** Texto de la opción elegida ('' si no hay). */
  value: string
  options: ComboOption<V>[]
  query: string
  onQueryChange: (q: string) => void
  onPick: (o: ComboOption<V>) => void
  placeholder?: string
  disabled?: boolean
  required?: boolean
  loading?: boolean
  clearable?: boolean
  onClear?: () => void
  ariaLabel?: string
  title?: string
  className?: string
  emptyText?: string
  footer?: ReactNode
  /** Texto libre con sugerencias: lo escrito es el valor (al abrir no se borra). */
  freeText?: boolean
}) {
  const t = useT()
  const listId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLUListElement>(null)
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [rect, setRect] = useState<DOMRect | null>(null)
  const shown = options.slice(0, MAX_SHOWN)

  // La lista sigue al campo si la página o la grilla se desplazan.
  useLayoutEffect(() => {
    if (!open) return
    const place = () => inputRef.current && setRect(inputRef.current.getBoundingClientRect())
    place()
    window.addEventListener('scroll', place, true)
    window.addEventListener('resize', place)
    return () => { window.removeEventListener('scroll', place, true); window.removeEventListener('resize', place) }
  }, [open])
  useEffect(() => { setActive(0) }, [query, open])
  useEffect(() => {
    listRef.current?.querySelector<HTMLElement>(`[data-i="${active}"]`)?.scrollIntoView({ block: 'nearest' })
  }, [active])

  function openList() {
    if (disabled) return
    if (!freeText) onQueryChange('')
    setOpen(true)
  }
  function close() { setOpen(false) }
  function pick(o: ComboOption<V>) {
    if (o.disabled) return
    onPick(o)
    close()
  }
  /** La siguiente opción que se puede elegir, hacia arriba o hacia abajo. */
  const step = (from: number, dir: 1 | -1) => {
    for (let i = from + dir; i >= 0 && i < shown.length; i += dir) if (!shown[i].disabled) return i
    return from
  }
  function onKeyDown(e: KeyboardEvent<HTMLInputElement>) {
    if (!open && (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ' && !query)) {
      e.preventDefault(); openList(); return
    }
    if (!open) return
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(a => step(a, 1)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => step(a, -1)) }
    else if (e.key === 'Home') { e.preventDefault(); setActive(0) }
    else if (e.key === 'End') { e.preventDefault(); setActive(shown.length - 1) }
    else if (e.key === 'Enter') {
      e.preventDefault()
      if (freeText && !shown[active]) close(); else if (shown[active]) pick(shown[active])
    }
    else if (e.key === 'Escape') { e.preventDefault(); close() }
    else if (e.key === 'Tab') close()
  }

  const below = rect ? window.innerHeight - rect.bottom : 0
  const up = rect ? below < 240 && rect.top > below : false
  const list = open && rect && (options.length > 0 || !freeText) && createPortal(
    <ul
      ref={listRef}
      id={listId}
      role="listbox"
      className="combo-options"
      style={{
        left: Math.max(8, Math.min(rect.left, window.innerWidth - Math.max(rect.width, 220) - 8)),
        minWidth: Math.max(rect.width, 220),
        maxWidth: Math.max(rect.width, 420),
        ...(up ? { bottom: window.innerHeight - rect.top + 4 } : { top: rect.bottom + 4 }),
        maxHeight: Math.max(160, Math.min(320, (up ? rect.top : below) - 16)),
      }}
      onMouseDown={e => e.preventDefault()}
    >
      {loading && options.length === 0 && <li className="combo-note">{t('Buscando…')}</li>}
      {!loading && options.length === 0 && !freeText && <li className="combo-note">{emptyText ?? t('Sin resultados')}</li>}
      {shown.map((o, i) => (
        <li
          key={o.key ?? String(o.value)}
          id={`${listId}-${i}`}
          data-i={i}
          role="option"
          aria-selected={o.label === value}
          aria-disabled={o.disabled || undefined}
          className={`combo-option ${i === active ? 'active' : ''} ${o.label === value ? 'chosen' : ''} ${o.disabled ? 'disabled' : ''}`}
          onMouseEnter={() => setActive(i)}
          onClick={() => pick(o)}
        >
          <span className="combo-label">{o.label}</span>
          {o.hint && <span className="combo-hint">{o.hint}</span>}
        </li>
      ))}
      {options.length > MAX_SHOWN && (
        <li className="combo-note">{t('{n} opciones más: siga escribiendo para acotar', { n: options.length - MAX_SHOWN })}</li>
      )}
      {footer}
    </ul>,
    document.body,
  )

  return (
    <div className={`combo ${className ?? ''} ${disabled ? 'is-disabled' : ''}`}>
      <input
        ref={inputRef}
        id={id}
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-activedescendant={open && shown[active] ? `${listId}-${active}` : undefined}
        aria-label={ariaLabel}
        title={title ?? (value || undefined)}
        autoComplete="off"
        value={open || freeText ? query : value}
        placeholder={open && !freeText ? t('Escribe para buscar…') : placeholder}
        disabled={disabled}
        // Con "required", un combo sin elegir no deja enviar el formulario (el texto visible queda vacío).
        required={required && !(freeText ? query : value)}
        onFocus={openList}
        onClick={() => !open && openList()}
        onBlur={close}
        onChange={e => { onQueryChange(e.target.value); if (!open) setOpen(true) }}
        onKeyDown={onKeyDown}
      />
      {clearable && value && !disabled ? (
        <button type="button" className="combo-clear" tabIndex={-1} title={t('Quitar')} aria-label={t('Quitar')}
          onMouseDown={e => e.preventDefault()} onClick={() => onClear?.()}>
          <X size={14} aria-hidden />
        </button>
      ) : (
        <ChevronDown size={16} className="combo-chevron" aria-hidden onMouseDown={e => { e.preventDefault(); if (!open) inputRef.current?.focus() }} />
      )}
      {list}
    </div>
  )
}

/**
 * Combo de una lista conocida (reemplaza a &lt;select&gt;): se filtra al escribir, sin tildes ni mayúsculas, por el
 * texto y por la pista. Las opciones con valor '' (p. ej. "Todos" o "(automática)") se eligen como cualquier otra.
 */
export function SearchSelect({ value, options, onChange, placeholder, ...rest }: {
  value: string
  options: ComboOption[]
  onChange: (value: string) => void
  placeholder?: string
  id?: string
  disabled?: boolean
  required?: boolean
  ariaLabel?: string
  title?: string
  className?: string
  emptyText?: string
}) {
  const [query, setQuery] = useState('')
  const filtered = useMemo(() => options.filter(o => matches(query, o.label, o.hint, o.value)), [options, query])
  const current = options.find(o => o.value === value)
  // Un valor vacío muestra su opción como texto de ayuda, así "required" sigue funcionando.
  const shown = current && current.value !== '' ? current.label : ''
  return (
    <ComboBox
      {...rest}
      value={shown}
      placeholder={current?.value === '' ? current.label : placeholder ?? (value && !current ? value : undefined)}
      options={filtered}
      query={query}
      onQueryChange={setQuery}
      onPick={o => onChange(o.value)}
    />
  )
}

/**
 * Texto libre con sugerencias (reemplaza a &lt;input list&gt;): se escribe cualquier cosa (p. ej. "*" en los perfiles)
 * y la lista sugiere los valores conocidos que coinciden.
 */
export function SuggestInput({ value, suggestions, onChange, ...rest }: {
  value: string
  suggestions: ComboOption[]
  onChange: (value: string) => void
  placeholder?: string
  id?: string
  disabled?: boolean
  required?: boolean
  ariaLabel?: string
  className?: string
}) {
  const filtered = useMemo(() => suggestions.filter(o => o.value !== value && matches(value, o.label, o.hint, o.value)), [suggestions, value])
  return (
    <ComboBox {...rest} freeText value={value} query={value} onQueryChange={onChange} options={filtered} onPick={o => onChange(o.value)} />
  )
}
