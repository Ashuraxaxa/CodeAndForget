import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useT } from '../i18n'
import { pageWindow } from '../lib/pageWindow'

/**
 * Paginador con primera, anterior, números, siguiente, última e "Ir a página" (P-107).
 * Con <c>lastUnknown</c> (total estimado o con tope, D-072) no se ofrece la última: no se sabe cuál es.
 */
export function Pager({ page, pages, lastUnknown = false, busy = false, onGo, children }: {
  page: number
  pages: number
  lastUnknown?: boolean
  busy?: boolean
  onGo: (page: number) => void
  children?: React.ReactNode
}) {
  const t = useT()
  const [target, setTarget] = useState('')
  const go = (n: number) => { if (n >= 1 && (lastUnknown || n <= pages) && n !== page) onGo(n) }

  function submit(e: FormEvent) {
    e.preventDefault()
    const n = Math.floor(Number(target))
    if (!Number.isFinite(n) || n < 1) return
    go(lastUnknown ? n : Math.min(n, pages))
    setTarget('')
  }

  return (
    <nav className="pager" aria-label={t('Paginación')}>
      <div className="pager-buttons">
        <button className="icon-button" disabled={page <= 1} onClick={() => go(1)} title={t('Primera página')} aria-label={t('Primera página')}>
          <ChevronsLeft size={16} aria-hidden />
        </button>
        <button className="icon-button" disabled={page <= 1} onClick={() => go(page - 1)} title={t('Página anterior')} aria-label={t('Página anterior')}>
          <ChevronLeft size={16} aria-hidden />
        </button>
        {pageWindow(page, pages, lastUnknown).map((n, i) => n === null
          ? <span key={`gap${i}`} className="pager-gap" aria-hidden>…</span>
          : (
            <button key={n} className={`pager-num ${n === page ? 'on' : ''}`} aria-current={n === page ? 'page' : undefined}
              aria-label={t('Página {page}', { page: n })} onClick={() => go(n)}>
              {n}
            </button>
          ))}
        {lastUnknown && <span className="pager-gap" aria-hidden>…</span>}
        <button className="icon-button" disabled={page >= pages} onClick={() => go(page + 1)} title={t('Página siguiente')} aria-label={t('Página siguiente')}>
          <ChevronRight size={16} aria-hidden />
        </button>
        <button className="icon-button" disabled={lastUnknown || page >= pages} onClick={() => go(pages)}
          title={lastUnknown ? t('La tabla es grande y no se sabe cuál es la última página: vaya a una página concreta o filtre.') : t('Última página')}
          aria-label={t('Última página')}>
          <ChevronsRight size={16} aria-hidden />
        </button>
      </div>
      <form className="pager-go" onSubmit={submit}>
        <label>
          <span className="pager-go-label">{t('Ir a página')}</span>
          <input type="number" inputMode="numeric" min={1} max={lastUnknown ? undefined : pages} value={target}
            placeholder={String(page)} aria-label={t('Ir a página')} onChange={e => setTarget(e.target.value)} />
        </label>
        <button type="submit" disabled={!target}>{t('Ir')}</button>
      </form>
      <span className="pager-info">
        {lastUnknown ? t('Página {page} de {pages}+', { page, pages }) : t('Página {page} de {pages}', { page, pages })}
        {busy && <span className="spinner sm" role="status" aria-label={t('Cargando…')} />}
      </span>
      {children}
    </nav>
  )
}
