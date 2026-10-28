// Prueba de carga del CRUD con k6 (P-053). Ver docs/carga.md.
//
//   k6 run -e BASE_URL=http://localhost:8080 -e CONN=demo -e TABLE=producto tests/load/crud.js
//
// Cada usuario virtual lista páginas (con y sin búsqueda), abre registros y, en una parte de las iteraciones,
// los edita con su versión (If-Match). Con un solo usuario de login, los límites de uso (RateLimit) cortarían la
// prueba: corra la app con RateLimit__Enabled=false o use varios usuarios (USERS=ana:clave,luis:clave).
import http from 'k6/http'
import { check, sleep } from 'k6'

const BASE = __ENV.BASE_URL || 'http://localhost:8080'
const CONN = __ENV.CONN || 'demo'
const SCHEMA = __ENV.SCHEMA || 'ventas'
const TABLE = __ENV.TABLE || 'producto'
const WRITE_RATIO = Number(__ENV.WRITE_RATIO || '0.1')
const USERS = (__ENV.USERS || `${__ENV.USER || 'admin'}:${__ENV.PASSWORD || 'admin'}`).split(',').map(u => u.split(':'))

export const options = {
  scenarios: {
    crud: {
      executor: 'ramping-vus',
      stages: [
        { duration: __ENV.RAMP || '30s', target: Number(__ENV.VUS || '200') },
        { duration: __ENV.HOLD || '2m', target: Number(__ENV.VUS || '200') },
        { duration: '15s', target: 0 },
      ],
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{op:list}': ['p(95)<800'],
    'http_req_duration{op:get}': ['p(95)<300'],
    'http_req_duration{op:update}': ['p(95)<500'],
  },
}

export function setup() {
  const tokens = USERS.map(([username, password]) => {
    const res = http.post(`${BASE}/api/auth/login`, JSON.stringify({ username, password }),
      { headers: { 'Content-Type': 'application/json' } })
    check(res, { 'login 200': r => r.status === 200 })
    return res.json('token')
  })
  const meta = http.get(`${BASE}/api/meta/${CONN}/tables/${SCHEMA}/${TABLE}`, { headers: auth(tokens[0]) }).json()
  return { tokens, pk: meta.primaryKey, editable: meta.columns.filter(c => c.type === 'String' && !c.isReadOnly && !c.isPrimaryKey).map(c => c.name) }
}

const auth = token => ({ Authorization: `Bearer ${token}` })
const path = `${BASE}/api/data/${CONN}/${SCHEMA}/${TABLE}`

export default function (data) {
  const token = data.tokens[__VU % data.tokens.length]
  const page = 1 + Math.floor(Math.random() * 20)
  const q = Math.random() < 0.3 ? `&q=${String.fromCharCode(97 + Math.floor(Math.random() * 26))}` : ''
  const list = http.get(`${path}?page=${page}&pageSize=25${q}`, { headers: auth(token), tags: { op: 'list' } })
  check(list, { 'list 200': r => r.status === 200 })
  const rows = list.status === 200 ? list.json('rows') : []
  if (rows.length === 0) { sleep(1); return }

  const row = rows[Math.floor(Math.random() * rows.length)]
  const key = data.pk.map(k => `${encodeURIComponent(k)}=${encodeURIComponent(row[k])}`).join('&')
  const get = http.get(`${path}/row?${key}`, { headers: auth(token), tags: { op: 'get' } })
  check(get, { 'get 200': r => r.status === 200 })

  if (get.status === 200 && data.editable.length > 0 && Math.random() < WRITE_RATIO) {
    const current = get.json()
    const col = data.editable[0]
    const res = http.put(`${path}/row?${key}`, JSON.stringify({ [col]: current[col] }), {
      headers: { ...auth(token), 'Content-Type': 'application/json', 'If-Match': `"${current.__version}"` },
      tags: { op: 'update' },
      responseCallback: http.expectedStatuses(200, 409),
    })
    // 409 = otro usuario virtual editó la misma fila entre medio: es el comportamiento esperado (D-069).
    check(res, { 'update 200 o 409': r => r.status === 200 || r.status === 409 })
  }
  sleep(0.5 + Math.random())
}
