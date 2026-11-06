// Revisión responsive de todas las pantallas (P-141, convenciones.md → "Responsive").
// Recorre cada pantalla en teléfono, tablet, PC y pantalla ancha, avisa si algo desborda a lo ancho y guarda capturas.
//
//   node scripts/responsive-check.cjs [carpeta-capturas]
//   CELDO_URL=http://localhost:5173 CELDO_USER=admin CELDO_PASSWORD=admin CELDO_CONN=demo CELDO_TABLE=ventas/producto \
//   CELDO_DASHBOARD=<id> CELDO_PUBLIC=<token> node scripts/responsive-check.cjs
//
// Necesita Playwright (npx playwright install chromium). Sale con 1 si alguna pantalla desborda.
const { chromium } = require('playwright')
const fs = require('fs')

const BASE = (process.env.CELDO_URL || 'http://localhost:5173').replace(/\/$/, '')
const USER = process.env.CELDO_USER || 'admin'
const PASS = process.env.CELDO_PASSWORD || 'admin'
const CONN = process.env.CELDO_CONN || 'demo'
const [SCHEMA, TABLE] = (process.env.CELDO_TABLE || 'ventas/producto').split('/')
const DASH = process.env.CELDO_DASHBOARD || ''
const PUB = process.env.CELDO_PUBLIC || ''
const OUT = process.argv[2] || 'responsive'

/** Tamaños del estándar: teléfono chico y común, tablet vertical y horizontal, portátil, PC y pantalla ancha. */
const SIZES = [[360, 740], [390, 844], [768, 1024], [1024, 768], [1366, 768], [1920, 1080], [2560, 1440]]
const t = `/c/${CONN}/t/${SCHEMA}/${TABLE}`
const PAGES = [
  ['login', '/login', false], ['conexiones', '/'], ['grilla', t], ['nuevo', `${t}/new`], ['configurar', `${t}/config`],
  ['config-columnas', `${t}/config`, true, '.tabs .tab >> nth=1'], ['importar', `${t}/import`],
  ['seguridad', '/seguridad'], ['seguridad-usuarios', '/seguridad', true, '.tabs .tab >> nth=1'],
  ['seguridad-apps', '/seguridad', true, '.tabs .tab >> nth=2'], ['auditoria', '/auditoria'], ['tokens', '/tokens'],
  ['plan', '/plan'], ['dashboards', '/dashboards'], ['embed-tabla', `/embed${t}`],
  ...(DASH ? [['dashboard', `/dashboards/${DASH}`], ['editor', `/dashboards/${DASH}?editar=1`], ['embed-dash', `/embed/dashboards/${DASH}`]] : []),
  ...(PUB ? [['publico', `/embed/p/${PUB}`, false]] : []),
]

/** Desborde de la página y elementos que salen de la pantalla sin estar dentro de algo con desplazamiento propio. */
function measure() {
  const vw = window.innerWidth
  const bad = []
  for (const el of document.querySelectorAll('body *')) {
    const r = el.getBoundingClientRect()
    if (r.width === 0 || r.right <= vw + 1 || el.closest('svg, .table-wrap, .modal')) continue
    let p = el.parentElement, clipped = false
    while (p && p !== document.body) { if (['auto', 'hidden', 'scroll', 'clip'].includes(getComputedStyle(p).overflowX)) { clipped = true; break } p = p.parentElement }
    if (!clipped) bad.push(`${el.tagName.toLowerCase()}${typeof el.className === 'string' && el.className ? '.' + el.className.split(' ')[0] : ''} +${Math.round(r.right - vw)}px`)
  }
  return { over: document.documentElement.scrollWidth - vw, bad: [...new Set(bad)].slice(0, 5) }
}

;(async () => {
  fs.mkdirSync(OUT, { recursive: true })
  const browser = await chromium.launch()
  let failures = 0
  for (const [w, h] of SIZES) {
    const ctx = await browser.newContext({ viewport: { width: w, height: h } })
    const page = await ctx.newPage()
    await page.goto(`${BASE}/login`)
    await page.fill('input[autocomplete=username]', USER)
    await page.fill('input[type=password]', PASS)
    await page.click('form.login-form button.primary')
    await page.waitForTimeout(1200)
    for (const [name, url, auth = true, click] of PAGES) {
      const p = auth ? page : await (await browser.newContext({ viewport: { width: w, height: h } })).newPage()
      await p.goto(BASE + url)
      await p.waitForTimeout(1500)
      if (click) await p.locator(click).first().click({ timeout: 3000 }).then(() => p.waitForTimeout(800), () => {})
      const { over, bad } = await p.evaluate(measure)
      await p.screenshot({ path: `${OUT}/${name}-${w}.png`, fullPage: true })
      if (over > 0 || bad.length) { failures++; console.log(`✗ ${w}px ${name}: desborda ${over}px ${bad.join(', ')}`) }
      if (!auth) await p.context().close()
    }
    await ctx.close()
  }
  await browser.close()
  console.log(`${PAGES.length} pantallas × ${SIZES.length} tamaños; ${failures} con desborde. Capturas en ${OUT}/`)
  process.exit(failures ? 1 : 0)
})()
