import { useState } from 'react'
import { api } from '../api/client'
import type { Column } from '../api/types'
import { fileSize } from '../lib/format'
import { useT } from '../i18n'
import { ChosenImage, StoredImage } from './ImagePreview'

/**
 * Columna de archivo (P-018): muestra el tamaño, permite descargar el guardado, elegir otro o quitarlo.
 * El archivo elegido viaja en base64 junto con el resto del formulario.
 * <c>value</c>: undefined = sin cambios; string = nuevo contenido (base64); null = quitar.
 */
export function FileField({ conn, schema, table, recordKey, column: c, size, value, disabled, onChange }: {
  conn: string
  schema: string
  table: string
  /** Clave del registro en edición (vacía en un alta). */
  recordKey: string
  column: Column
  size: unknown
  value: string | null | undefined
  disabled: boolean
  onChange: (value: string | null | undefined) => void
}) {
  const t = useT()
  const [chosen, setChosen] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const stored = size !== null && size !== undefined && value === undefined

  function pick(f: File | null) {
    if (!f) return
    const reader = new FileReader()
    reader.onload = () => { setChosen(f); onChange(String(reader.result)) } // "data:...;base64,..." (el servidor lo acepta)
    reader.readAsDataURL(f)
  }

  async function downloadStored() {
    setBusy(true)
    try { await api.downloadFile(conn, schema, table, recordKey, c.name) }
    catch (e) { alert((e as Error).message) }
    finally { setBusy(false) }
  }

  // Vista previa (P-090): la guardada si es una imagen de hasta 5 MB; la elegida, al momento.
  const preview = stored && recordKey && Number(size) <= 5 * 1024 * 1024
  return (
    <div className="file-field">
      {value && chosen ? <ChosenImage file={chosen} className="file-preview" />
        : preview && <StoredImage conn={conn} schema={schema} table={table} recordKey={recordKey} column={c.name} className="file-preview" />}
      <div className="row">
        {stored && <span>{t('Guardado')} · {fileSize(size)}</span>}
        {stored && recordKey && (
          <button type="button" className="link" disabled={busy} onClick={downloadStored}>{busy ? t('Descargando…') : t('Descargar')}</button>
        )}
        {value && chosen && <span>{t('Nuevo: {name}', { name: chosen.name })} · {fileSize(chosen.size)}</span>}
        {value === null && <span className="muted">{t('Se quitará al guardar')}</span>}
        {!stored && value === undefined && <span className="muted">{t('Sin archivo')}</span>}
      </div>
      {!disabled && (
        <div className="row">
          <input type="file" onChange={e => pick(e.target.files?.[0] ?? null)} />
          {c.isNullable && (stored || value) && (
            <button type="button" className="link danger" onClick={() => { setChosen(null); onChange(null) }}>{t('Quitar')}</button>
          )}
          {value !== undefined && (
            <button type="button" className="link" onClick={() => { setChosen(null); onChange(undefined) }}>{t('Deshacer')}</button>
          )}
        </div>
      )}
    </div>
  )
}
