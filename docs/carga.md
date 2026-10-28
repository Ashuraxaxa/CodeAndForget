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

### Tabla de 1 millón de filas (P-052, D-072)

`ventas.grande` en PostgreSQL 16, mismo contenedor de 4 núcleos compartidos entre la base, la API y el generador.

Una solicitud a la vez:

| Consulta | Antes | Después |
|---|---|---|
| Página 1 (total) | 26 ms, COUNT exacto | 12 ms, total estimado |
| Página 2.000 | 36 ms | 20 ms |
| Página 39.999 (la última) | 229 ms | 197 ms |
| Búsqueda "abc" | 318 ms | 110 ms ("más de 1.000") |
| Orden por columna sin índice, página 1.000 | 1.061 ms | 751 ms |

Con carga (generador en Node; cada usuario repite listar y abrir sin pausa). Los usuarios buscan una letra al azar, así
que muchas búsquedas no encuentran nada y recorren la tabla entera.

| Escenario | Sin protección | Con turno para consultas pesadas |
|---|---|---|
| 200 usuarios, 5 % busca | — | 633 sol/s; listado p50 9 ms; abrir p95 81 ms |
| 50 usuarios, 30 % busca | abrir p95 1.480 ms | abrir p95 13 ms; las búsquedas esperan su turno (p95 5,6 s) |
| 200 usuarios, 30 % busca | 37 sol/s; listado p50 8 s; abrir p50 5 s | 112 sol/s; listado p50 38 ms; abrir p95 248 ms; búsquedas hasta 20 s |
| 200 usuarios, sin búsquedas | 2.883 sol/s; listado p95 39 ms | (igual) |

En resumen:
- **Paginar y abrir registros en una tabla de 1 millón va bien.**
- **La búsqueda de texto sin índice es el límite:** cada una recorre la tabla. El turno evita que esas búsquedas traben todo lo demás.
- Si se busca mucho en tablas así, conviene pedir al DBA un índice de texto (ver `configuracion.md`, sección `Crud`).

Pendiente: correr el script de k6 en un servidor como el de producción (P-097).
