import { Database } from 'lucide-react'

/** Logo de la app: el mismo dibujo que public/favicon.svg. */
export function Brand() {
  return <span className="brand"><span className="brand-mark" aria-hidden><Database size={17} strokeWidth={2.4} /></span><span className="brand-name">Mantenedores</span></span>
}
