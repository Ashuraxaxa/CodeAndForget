# Pruebas de carga

Script de [k6](https://k6.io) en `tests/load/crud.js` (P-053). Cada usuario virtual:

1. lista páginas, con y sin búsqueda;
2. abre registros;
3. en el 10 % de las iteraciones, edita uno con su versión (`If-Match`). Un `409` (otro usuario editó la misma fila) cuenta como respuesta esperada.

## Cómo correrla

```bash
# la app con los límites de uso apagados (un solo usuario de login los alcanzaría enseguida)
RateLimit__Enabled=false docker compose up --build
k6 run -e BASE_URL=http://localhost:8080 -e CONN=demo -e TABLE=producto tests/load/crud.js
```

| Variable | Por defecto | Qué es |
|---|---|---|
| `BASE_URL` | `http://localhost:8080` | Dirección de la app. |
| `CONN`, `SCHEMA`, `TABLE` | `demo`, `ventas`, `producto` | Tabla a cargar. |
| `USERS` | `admin:admin` | Usuarios `nombre:clave` separados por coma. Con varios, se pueden dejar los límites de uso encendidos. |
| `VUS`, `RAMP`, `HOLD` | `200`, `30s`, `2m` | Usuarios virtuales, subida y duración. |
| `WRITE_RATIO` | `0.1` | Fracción de iteraciones que editan. |

Umbrales (la prueba falla si no se cumplen):
- menos de 1 % de errores;
- p95 del listado bajo 800 ms;
- p95 del detalle bajo 300 ms;
- p95 de la edición bajo 500 ms.

## Resultados

| Fecha | Escenario | Resultado |
|---|---|---|
| 2026-10-28 | 200 usuarios concurrentes, 20 s, listado y detalle de `producto` (decenas de filas), PostgreSQL 16 y la API en un contenedor de desarrollo. Generador en Node, no k6 (el entorno no permitía bajar k6). | 2.255 solicitudes/s, 0 errores. Listado p50 30 ms, p95 83 ms, p99 126 ms. Detalle p50 27 ms, p95 77 ms. |

Pendiente: repetir con k6 contra una tabla de 10 millones de filas junto con P-052 (paginación por clave). Con
`OFFSET` y `COUNT(*)` exactos, las páginas lejanas de una tabla así se vuelven lentas.
