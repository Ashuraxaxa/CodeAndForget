// Portal de ejemplo que embebe Celdo con un token firmado (P-138, docs/embeber.md). Node 18+, sin dependencias.
//
//   CELDO_KID=... CELDO_SECRET=csk_... CELDO_RES=dashboard:ID,table:demo/ventas/producto node samples/embed/server.js
//
// Abre http://localhost:8090. El navegador nunca ve la clave: el servidor firma el token en /celdo-token, y decide
// qué puede abrir (res), qué puede hacer (perm) y qué filas ve (filters) según su propio usuario.
const http = require('http')
const crypto = require('crypto')

const CELDO = (process.env.CELDO_URL || 'http://localhost:5080').replace(/\/$/, '')
const KID = process.env.CELDO_KID || ''
const SECRET = process.env.CELDO_SECRET || ''
const RES = (process.env.CELDO_RES || '').split(',').map(s => s.trim()).filter(Boolean)
const PERM = (process.env.CELDO_PERM || 'view').split(',').map(s => s.trim())
const FILTERS = process.env.CELDO_FILTERS ? JSON.parse(process.env.CELDO_FILTERS) : undefined
const PORT = Number(process.env.PORT || 8090)

const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url')

/** Un JWT HS256 de 10 minutos con la clave de la aplicación (Seguridad → Aplicaciones en Celdo). */
function celdoToken(user) {
  const now = Math.floor(Date.now() / 1000)
  const head = b64({ alg: 'HS256', typ: 'JWT', kid: KID })
  const body = b64({ sub: user.id, name: user.name, res: RES, perm: PERM, filters: FILTERS, iat: now, exp: now + 600 })
  const sig = crypto.createHmac('sha256', SECRET).update(`${head}.${body}`).digest('base64url')
  return `${head}.${body}.${sig}`
}

const page = `<!doctype html>
<html lang="es"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Portal de ejemplo</title>
<style>body{font-family:system-ui,sans-serif;margin:0;background:#eef1f5}header{background:#1d3557;color:#fff;padding:14px 20px}
main{padding:16px 20px;display:grid;gap:16px}section{background:#fff;border-radius:10px;overflow:hidden}
#log{font:12px ui-monospace,monospace;background:#111;color:#9f9;padding:10px;border-radius:8px;max-height:200px;overflow:auto;white-space:pre-wrap}</style>
</head><body><header>Portal de ejemplo · embebe Celdo con embed.js</header><main>
${RES.map((r, i) => `<section id="celdo-${i}"></section>`).join('')}
<div id="log">Eventos de Celdo:\n</div></main>
<script src="${CELDO}/embed.js"></script>
<script>
  const log = e => { document.getElementById('log').textContent += JSON.stringify(e) + '\\n' };
  const getToken = () => fetch('/celdo-token').then(r => r.text());
  const resources = ${JSON.stringify(RES)};
  resources.forEach((r, i) => {
    const [kind, value] = [r.slice(0, r.indexOf(':')), r.slice(r.indexOf(':') + 1)]
    const [c, s, t] = value.split('/').map(encodeURIComponent)
    const url = kind === 'dashboard' ? '/embed/dashboards/' + value : '/embed/c/' + c + '/t/' + s + '/' + t
    Celdo.embed('#celdo-' + i, { url, getToken, tema: 'sistema', onEvent: log })
  })
</script></body></html>`

http.createServer((req, res) => {
  if (req.url === '/celdo-token') {
    // Aquí iría la sesión del portal: quién es y qué cliente ve.
    res.writeHead(200, { 'Content-Type': 'text/plain', 'Cache-Control': 'no-store' })
    return res.end(celdoToken({ id: 'demo@portal', name: 'Usuario de demo' }))
  }
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' })
  res.end(page)
}).listen(PORT, () => {
  if (!KID || !SECRET || RES.length === 0) console.warn('Faltan CELDO_KID, CELDO_SECRET o CELDO_RES: vea el comentario al inicio.')
  console.log(`Portal de ejemplo en http://localhost:${PORT} (Celdo en ${CELDO})`)
})
