// Revisa que cada texto de la interfaz tenga traducción al inglés y al ruso (P-100).
// Los textos se escriben en español dentro de t('…'); en.ts y ru.ts los traducen. Sale con error si falta alguno.
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'

const src = new URL('../src/', import.meta.url).pathname
const files = []
const walk = dir => readdirSync(dir).forEach(f => {
  const p = join(dir, f)
  if (statSync(p).isDirectory()) walk(p)
  else if (/\.(tsx?)$/.test(f) && !/i18n\/(en|es|ru)\.ts$/.test(p)) files.push(p)
})
walk(src)

const unescape = s => s.replace(/\\(['"\\])/g, '$1')
const used = new Map()
const call = /\bt\(\s*(?:'((?:[^'\\\n]|\\.)*)'|"((?:[^"\\\n]|\\.)*)")/g
for (const f of files) {
  for (const m of readFileSync(f, 'utf8').matchAll(call)) {
    const key = unescape(m[1] ?? m[2])
    if (!used.has(key)) used.set(key, f.replace(src, ''))
  }
}

const keysOf = file => {
  const text = readFileSync(join(src, 'i18n', file), 'utf8')
  const keys = new Set()
  for (const m of text.matchAll(/^\s*(?:'((?:[^'\\]|\\.)*)'|"((?:[^"\\]|\\.)*)")\s*:/gm)) keys.add(unescape(m[1] ?? m[2]))
  return keys
}

let failed = false
for (const lang of ['en', 'ru']) {
  const keys = keysOf(`${lang}.ts`)
  const missing = [...used.keys()].filter(k => !keys.has(k))
  const unused = [...keys].filter(k => !used.has(k))
  if (missing.length) {
    failed = true
    console.error(`\n[i18n] ${lang}.ts: faltan ${missing.length} traducciones:`)
    for (const k of missing) console.error(`  ${JSON.stringify(k)}  (${used.get(k)})`)
  }
  if (unused.length) console.warn(`[i18n] ${lang}.ts: ${unused.length} claves sin uso: ${unused.slice(0, 10).map(k => JSON.stringify(k)).join(', ')}${unused.length > 10 ? '…' : ''}`)
}
if (failed) process.exit(1)
console.log(`[i18n] ${used.size} textos, traducidos al inglés y al ruso.`)
