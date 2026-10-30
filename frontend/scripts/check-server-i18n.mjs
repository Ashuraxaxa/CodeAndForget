// Revisa que cada mensaje en español que el servidor puede mandarle al usuario tenga traducción (P-101, D-080, D-102).
// Lee los textos de backend/src (*.cs), los pasa por translateServer y falla si alguno queda igual o con palabras en
// español. Si no está el backend (p. ej. al compilar solo el frontend en Docker), no revisa nada.
import { build } from 'esbuild'
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'

const backend = new URL('../../backend/src/', import.meta.url).pathname
if (!existsSync(backend)) {
  console.log('[i18n servidor] sin backend/src: no se revisa.')
  process.exit(0)
}

// Archivos cuyos textos no van a la pantalla del usuario: errores de arranque y de configuración (los lee quien
// opera el servicio, en el registro), textos para la IA del servidor MCP y textos que el servidor ya traduce solo.
const SKIP_FILES = [
  /Mantenedores\.Api\/Mcp\//, // para la IA: los entiende en español y responde en el idioma del usuario
  /Mantenedores\.Api\/Auth\/AuthSetup\.cs$/, // arranque; el scope que falta va al registro
  /Mantenedores\.Api\/Auth\/AccessTokenAuth\.cs$/, // el motivo del 401 va al registro, no a la respuesta
  /Mantenedores\.Api\/Infrastructure\/(ProviderPlugins|SharedCache|HostingSetup|HealthChecks|SpaIndex|Telemetry\w*)\.cs$/,
  /Mantenedores\.Api\/Notifications\//, // el correo se traduce en el servidor (Notifications:Language)
  /Mantenedores\.Api\/Endpoints\/AuditLabels\.cs$/, // encabezados de la exportación: los traduce el servidor
  /Mantenedores\.Api\/Infrastructure\/ServerText\.cs$/,
  /Mantenedores\.Metadata\/(MetadataDatabase|MetadataSetup)\.cs$/, // arranque de la metadata
  /Mantenedores\.Providers\.\w+\/\w+Provider\.cs$/, // cadenas de conexión de ejemplo
  /Program\.cs$/,
]
// Textos sueltos que tampoco llegan al usuario (errores internos o de arranque).
const SKIP_TEXTS = [
  /^Nombre de parámetro inesperado/, /^Fecha inesperada/, /^Dos proveedores dicen ser/, /^Motor no soportado/,
]

const SPANISH = /[áéíóúñ¿¡]|\b(el|la|los|las|del|una?|es|se|que|para|con|por|ya|sin|debe|hay|puede|existe|tiene|elija|falta|columna|tabla|registro|valor|usuario|conexi[oó]n|archivo|motor|filas?|desactivad[oa]|inexistente|inv[aá]lid[oa]|v[aá]lid[oa]s?|obligatori[oa])\b/i

/** Literales de C# con sus agujeros de interpolación como "Xyz"; une los que se concatenan con +. */
function literals(src) {
  const out = []
  let i = 0
  const n = src.length
  let line = 1
  const push = (text, at, joinable, log) => {
    const prev = out[out.length - 1]
    if (joinable && prev && prev.open) { prev.text += text; prev.open = false; return }
    out.push({ text, line: at, open: false, log })
  }
  // ¿El literal es argumento de un registro (logger.LogWarning(…), Warn(…))? Ese texto va al registro, no al usuario.
  const LOG = /\b(?:Log(?:Information|Warning|Error|Debug|Critical|Trace)|Warn)\s*\((?:[^;{}"]|"[^"]*")*$/
  const readString = (interp, verbatim) => {
    // i apunta a la comilla de apertura
    i++
    let text = ''
    while (i < n) {
      const c = src[i]
      if (c === '\n') line++
      if (!verbatim && c === '\\') { text += src[i + 1] === 'n' ? ' ' : src[i + 1]; i += 2; continue }
      if (c === '"') {
        if (verbatim && src[i + 1] === '"') { text += '"'; i += 2; continue }
        i++
        return text
      }
      if (interp && c === '{') {
        if (src[i + 1] === '{') { text += '{'; i += 2; continue }
        // agujero: hasta su } de cierre, saltando cadenas anidadas
        let depth = 1
        i++
        while (i < n && depth > 0) {
          const d = src[i]
          if (d === '"') { const nestedVerbatim = src[i - 1] === '@'; readString(false, nestedVerbatim); continue }
          if (d === '\'') { i += src[i + 1] === '\\' ? 4 : 3; continue }
          if (d === '{') depth++
          else if (d === '}') depth--
          if (d === '\n') line++
          i++
        }
        text += 'Xyz'
        continue
      }
      if (interp && c === '}' && src[i + 1] === '}') { text += '}'; i += 2; continue }
      text += c
      i++
    }
    return text
  }
  let lastEnd = -1
  while (i < n) {
    const c = src[i]
    if (c === '\n') { line++; i++; continue }
    if (src.startsWith('//', i)) { while (i < n && src[i] !== '\n') i++; continue }
    if (src.startsWith('/*', i)) { const j = src.indexOf('*/', i); for (const ch of src.slice(i, j)) if (ch === '\n') line++; i = j + 2; continue }
    if (src.startsWith('"""', i)) { const j = src.indexOf('"""', i + 3); for (const ch of src.slice(i, j)) if (ch === '\n') line++; i = j + 3; continue }
    if (c === '\'') { i += src[i + 1] === '\\' ? 4 : 3; continue }
    if (c === '"') {
      const prefix = src.slice(Math.max(0, i - 2), i)
      const interp = prefix.endsWith('$') || prefix === '$@' || prefix === '@$'
      const verbatim = prefix.endsWith('@')
      const at = line
      // ¿Viene después de "literal +"? Entonces es la continuación de la misma frase.
      const joinable = lastEnd >= 0 && /^\s*\+\s*[$@]*$/.test(src.slice(lastEnd, i))
      const log = LOG.test(src.slice(Math.max(0, i - 300), i))
      const text = readString(interp, verbatim)
      push(text, at, joinable, log)
      out[out.length - 1].open = true
      lastEnd = i
      continue
    }
    i++
  }
  return out
}

const files = []
const walk = dir => readdirSync(dir).forEach(f => {
  const p = join(dir, f)
  if (statSync(p).isDirectory()) { if (f !== 'bin' && f !== 'obj') walk(p) }
  else if (f.endsWith('.cs')) files.push(p)
})
walk(backend)

// translateServer tal como corre en el navegador.
const bundle = await build({
  entryPoints: [new URL('../src/i18n/index.ts', import.meta.url).pathname],
  bundle: true, write: false, format: 'esm', platform: 'node', logLevel: 'silent',
  jsx: 'automatic',
})
const { translateServer } = await import('data:text/javascript;base64,' + Buffer.from(bundle.outputFiles[0].text).toString('base64'))

// Casos difíciles del traductor: variable con puntos al final, mensaje anidado, {verb} y varias oraciones.
const CASES = [
  ["expresión regular inválida: Invalid pattern '[' at offset 1. Unterminated [] set.", "invalid regular expression: Invalid pattern '[' at offset 1. Unterminated [] set."],
  ['no se pudo leer el archivo como json: se esperaba un arreglo de objetos', 'the file could not be read as json: an array of objects was expected'],
  ['No tiene permiso para editar registros de ventas.producto.', 'You do not have permission to edit records of ventas.producto.'],
  ['La estructura de ventas.producto cambió en la base desde que se leyó. Ya se volvió a leer: recargue la página e intente de nuevo.',
    'The structure of ventas.producto changed in the database since it was read. It has been reloaded: refresh the page and try again.'],
]
for (const [es, en] of CASES) {
  const out = translateServer(es, 'en')
  if (out !== en) { console.error(`[i18n servidor] translateServer(${JSON.stringify(es)})\n  da       ${JSON.stringify(out)}\n  esperado ${JSON.stringify(en)}`); process.exit(1) }
}

const missing = []
let checked = 0
for (const f of files) {
  const rel = relative(backend, f)
  if (SKIP_FILES.some(re => re.test(rel))) continue
  for (const { text, line, log } of literals(readFileSync(f, 'utf8'))) {
    if (log) continue
    const s = text.replace(/\s+/g, ' ').trim()
    if (s.length < 4 || !/[a-záéíóúñ]{3}/i.test(s) || !SPANISH.test(s.replace(/Xyz/g, ''))) continue
    if (SKIP_TEXTS.some(re => re.test(s))) continue
    checked++
    for (const lang of ['en', 'ru']) {
      // Un agujero que es el verbo de "No tiene permiso para {verb} …" se prueba con uno de los verbos conocidos.
      const out = translateServer(s.replace(/para Xyz /g, 'para leer '), lang)
      if (out === s || SPANISH.test(out.replace(/Xyz/g, ''))) { missing.push(`  ${rel}:${line} (${lang})  ${JSON.stringify(s)}`); break }
    }
  }
}

if (missing.length) {
  console.error(`\n[i18n servidor] ${missing.length} mensajes del servidor sin traducción en frontend/src/i18n/server.ts:`)
  for (const m of missing) console.error(m)
  console.error('\nAgregue la plantilla [es, en, ru] (las partes variables entre llaves) o, si el texto nunca llega al usuario, excluya el archivo en scripts/check-server-i18n.mjs.')
  process.exit(1)
}
console.log(`[i18n servidor] ${checked} mensajes del servidor, traducidos al inglés y al ruso.`)
