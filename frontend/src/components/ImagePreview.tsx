import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'

/**
 * Imagen guardada en una columna de archivo (P-090): se pide con el token y se muestra como blob. Si el archivo no es
 * una imagen, no se muestra nada. La consulta queda en caché mientras se navega.
 */
export function StoredImage({ conn, schema, table, recordKey, column, className, alt }: {
  conn: string; schema: string; table: string; recordKey: string; column: string; className?: string; alt?: string
}) {
  const img = useQuery({
    queryKey: ['image', conn, schema, table, recordKey, column],
    queryFn: () => api.imageUrl(conn, schema, table, recordKey, column),
    staleTime: 5 * 60_000,
    gcTime: 5 * 60_000,
  })
  // La URL blob se libera cuando la consulta sale de la caché.
  useEffect(() => {
    const url = img.data
    return () => { if (url) setTimeout(() => URL.revokeObjectURL(url), 5 * 60_000) }
  }, [img.data])
  if (!img.data) return null
  return <img className={className} src={img.data} alt={alt ?? column} loading="lazy" />
}

/** Vista previa de un archivo recién elegido (aún sin guardar), si es una imagen. */
export function ChosenImage({ file, className }: { file: File; className?: string }) {
  const [url, setUrl] = useState<string | null>(null)
  useEffect(() => {
    if (!file.type.startsWith('image/')) { setUrl(null); return }
    const u = URL.createObjectURL(file)
    setUrl(u)
    return () => URL.revokeObjectURL(u)
  }, [file])
  return url ? <img className={className} src={url} alt={file.name} /> : null
}
