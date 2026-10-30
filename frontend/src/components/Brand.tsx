/** El símbolo de Celdo (D-103): una tabla de 2 × 2 con la celda activa en amarillo. El mismo de public/favicon.svg. */
export function CeldoMark({ className = 'brand-mark' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 64 64" aria-hidden>
      <rect className="mark-bg" width="64" height="64" rx="15" />
      <rect x="13" y="13" width="17" height="17" rx="4" fill="#fff" fillOpacity=".22" />
      <rect x="34" y="13" width="17" height="17" rx="4" fill="#fff" fillOpacity=".22" />
      <rect x="13" y="34" width="17" height="17" rx="4" fill="#fff" fillOpacity=".22" />
      <rect x="34" y="34" width="17" height="17" rx="4" fill="#FFD23F" />
    </svg>
  )
}

/** Logo de la app: símbolo y nombre en minúsculas. */
export function Brand() {
  return <span className="brand"><CeldoMark /><span className="brand-name">celdo</span></span>
}
