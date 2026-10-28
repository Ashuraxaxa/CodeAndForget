import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { labelOf, type ImportMode, type ImportResult } from '../api/types'
import { useT } from '../i18n'

const ACCEPT = '.csv,.txt,.xlsx,.json,.xml'

/**
 * Importar (P-017): 1) elegir el archivo, 2) revisar la asociación de columnas y los errores por fila,
 * 3) confirmar. Se graba todo en una transacción: o entran todas las filas o ninguna.
 */
export function ImportPage() {
  const { conn = '', schema = '', table = '' } = useParams()
  const qc = useQueryClient()
  const t = useT()
  const meta = useQuery({ queryKey: ['meta', conn, schema, table], queryFn: () => api.table(conn, schema, table) })
  const [file, setFile] = useState<File | null>(null)
  const [mapping, setMapping] = useState<Record<string, string | null> | undefined>(undefined)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [mode, setMode] = useState<ImportMode>('insert')
  const back = `/c/${conn}/t/${encodeURIComponent(schema)}/${encodeURIComponent(table)}`

  const preview = useMutation({
    mutationFn: (args: { file: File; mapping?: Record<string, string | null>; mode?: ImportMode }) =>
      api.previewImport(conn, schema, table, args.file, args.mapping, args.mode ?? mode),
    onSuccess: r => { setResult(r); setMapping(r.mapping) },
  })
  const commit = useMutation({
    mutationFn: () => api.importFile(conn, schema, table, file!, mapping, mode),
    onSuccess: r => {
      setResult(r)
      if (r.imported > 0) qc.invalidateQueries({ queryKey: ['rows', conn, schema, table] })
    },
  })

  function choose(f: File | null) {
    setFile(f)
    setResult(null)
    setMapping(undefined)
    commit.reset()
    if (f) preview.mutate({ file: f })
  }

  function changeMode(m: ImportMode) {
    setMode(m)
    // La asociación automática cambia (en "actualizar" se ofrece la clave): se vuelve a calcular.
    setMapping(undefined)
    if (file) preview.mutate({ file, mode: m })
  }

  function remap(header: string, column: string) {
    const next = { ...mapping, [header]: column || null }
    setMapping(next)
    if (file) preview.mutate({ file, mapping: next })
  }

  const title = meta.data ? labelOf(meta.data) : table
  const r = result
  const done = r && r.imported > 0
  const error = (preview.error ?? commit.error) as Error | null
  const mapped = r ? Object.entries(r.mapping).filter(([, c]) => c).map(([, c]) => c!) : []

  return (
    <div className="page">
      <div className="page-header">
        <div>
          <h1>{t('Importar · {table}', { table: title })}</h1>
          <div className="muted small">{t('CSV, Excel, JSON o XML. La primera fila (o los nombres de los campos) indica las columnas.')}</div>
        </div>
        <Link to={back}>‹ {t('Volver a la lista')}</Link>
      </div>

      <div className="card form">
        <label>
          <span className="field-label">1. {t('Archivo')}</span>
          <input type="file" accept={ACCEPT} onChange={e => choose(e.target.files?.[0] ?? null)} />
        </label>
        <div>
          <span className="field-label">{t('Qué hacer con cada fila')}</span>
          <div className="checks">
            <label className="check">
              <input type="radio" name="mode" checked={mode === 'insert'} onChange={() => changeMode('insert')} />
              {t('Solo agregar registros nuevos')}
            </label>
            <label className="check">
              <input type="radio" name="mode" checked={mode === 'upsert'} onChange={() => changeMode('upsert')} />
              {t('Agregar y actualizar por clave')}
            </label>
          </div>
          {mode === 'upsert' && meta.data && (
            <span className="hint">
              {t('Si la fila trae una clave ({key}) que ya existe, se actualiza con lo que trae el archivo; si no trae clave, se crea.', { key: meta.data.primaryKey.join(', ') })}
            </span>
          )}
        </div>
        {preview.isPending && <p className="muted">{t('Leyendo y validando…')}</p>}
        {error && <div className="alert">{error.message}</div>}
      </div>

      {r && !done && (
        <>
          <div className="card form">
            <span className="field-label">2. {t('Columnas')}</span>
            <div className="table-wrap">
              <table className="grid">
                <thead><tr><th>{t('En el archivo')}</th><th>{t('Se guarda en')}</th></tr></thead>
                <tbody>
                  {r.headers.map(h => (
                    <tr key={h}>
                      <td><code>{h}</code></td>
                      <td>
                        <select value={r.mapping[h] ?? ''} onChange={e => remap(h, e.target.value)}>
                          <option value="">{t('(ignorar)')}</option>
                          {r.targets.map(c => (
                            <option key={c.name} value={c.name}>
                              {c.label ? `${c.label} (${c.name})` : c.name}{c.required ? ' *' : ''}{c.isKey ? ' · ' + t('clave') : ''}
                            </option>
                          ))}
                        </select>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <span className="hint">{t('* obligatoria. Las columnas que genera la base (identity, calculadas) no se importan.')}</span>
          </div>

          <div className="card form">
            <span className="field-label">3. {t('Revisión')}</span>
            <p>
              {t('{n} filas', { n: r.totalRows })} · {t('{n} válidas', { n: r.validRows })}
              {mode === 'upsert' && r.errors.length === 0 && <> · {t('{n} nuevas', { n: r.created })} · {t('{n} se actualizan', { n: r.updated })}</>}
              {r.errors.length > 0 && <> · <span className="field-error">{t('{n} errores', { n: r.errors.length })}</span></>}
            </p>
            {r.errors.length > 0 && (
              <div className="alert">
                {r.errors.map((e, i) => (
                  <div key={i}>{e.line > 0 ? t('Línea {line}', { line: e.line }) : t('General')}{e.column ? ` · ${e.column}` : ''}: {e.message}</div>
                ))}
              </div>
            )}
            {r.sample.length > 0 && (
              <>
                <span className="muted small">{t('Primeras {n} filas, como se van a guardar:', { n: r.sample.length })}</span>
                <div className="table-wrap">
                  <table className="grid">
                    <thead><tr>{mapped.map(c => <th key={c}>{c}</th>)}</tr></thead>
                    <tbody>
                      {r.sample.map((row, i) => (
                        <tr key={i}>{mapped.map(c => <td key={c}><span className="cell">{row[c] ?? ''}</span></td>)}</tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            )}
            <div className="actions">
              <button className="primary" disabled={r.errors.length > 0 || r.totalRows === 0 || commit.isPending || preview.isPending}
                onClick={() => commit.mutate()}>
                {commit.isPending ? t('Importando…') : t('Importar {n} filas', { n: r.totalRows })}
              </button>
              <Link to={back} className="button">{t('Cancelar')}</Link>
            </div>
            {r.errors.length > 0 && <span className="hint">{t('Corrija el archivo o la asociación de columnas: se importa todo o nada.')}</span>}
          </div>
        </>
      )}

      {done && (
        <div className="card">
          <div className="alert ok">
            {r.updated > 0 ? t('Se crearon {created} registros y se actualizaron {updated}.', { created: r.created, updated: r.updated }) : t('Se importaron {n} registros.', { n: r.imported })}
          </div>
          <div className="actions">
            <Link to={back} className="button primary">{t('Ver la lista')}</Link>
            <button onClick={() => choose(null)}>{t('Importar otro archivo')}</button>
          </div>
        </div>
      )}
    </div>
  )
}
