# Plan: servicio de mantenedores dinámicos

## Viabilidad

Es viable. Ambos motores exponen su estructura completa por SQL (`sys.*` en SQL Server,
`pg_catalog` en PostgreSQL) y el patrón ya está probado en productos como Directus, NocoDB o Hasura.
La dificultad no está en el CRUD sino en los casos borde y en la seguridad.

**Automático**: tablas, vistas, columnas, tipos, nulos, defaults, PK (simples y compuestas),
únicos, FK (1:N, autorreferencia, tablas puente N:M), identity, columnas calculadas.

**Requiere configuración (overrides)**: tablas sin PK, lógica en triggers o procedimientos,
etiquetas amigables, columna a mostrar en combos, bajas lógicas, columnas de auditoría,
tipos especiales (`geography`, `hierarchyid`, arreglos).

**Regla de diseño**: base automática + overrides guardados aparte, que sobreviven a cada
relectura del esquema (el `hash` del esquema permite detectar y mostrar diferencias).

## Escalabilidad

| Capa | Estrategia |
|---|---|
| API | Sin estado → escala horizontal detrás de un balanceador |
| Conexiones a BD destino | Pool por conexión con tope, cierre de pools inactivos, timeouts por consulta |
| Esquema | Caché (memoria en Fase 1 → Redis en Fase 4), relectura a pedido o programada por hash |
| Tablas grandes | Paginación obligatoria con tope (200), paginación por clave y conteos estimados en Fase 4 |
| Multi-tenant | Metadata aislada por tenant y límites de uso por conexión |

El límite real lo pone la capacidad de cada BD destino, no el servicio.

## Seguridad

- Usuario de BD con permisos mínimos por conexión (nunca `sa` / `postgres`).
- Identificadores solo desde la metadata (lista blanca) y delimitados; valores siempre parametrizados. ✅ Fase 1
- Cadenas de conexión cifradas. ✅ Fase 1 (Data Protection) → vault en Fase 3
- RBAC por conexión / tabla / columna / operación y filtros de fila por rol. Fase 3
- Auditoría (quién, qué, valor anterior y nuevo) y concurrencia optimista (`rowversion` / `xmin`). Fase 3

## Fases

| Fase | Duración | Contenido | Estado |
|---|---|---|---|
| 0. Descubrimiento | 1 sem | Inventario de BD reales, casos borde, prueba de concepto | — |
| 1. MVP | 5–6 sem | Conexiones, lectura de esquema PG + MSSQL, CRUD genérico, grilla, formulario, combos FK, login | **Estructura base lista** |
| 2. Personalización | 4 sem | Editor de overrides, maestro-detalle, N:M, árboles, diff de esquema, import/export | Pendiente |
| 3. Seguridad | 3 sem | RBAC, filtros de fila, auditoría, concurrencia, SSO (OIDC / Entra ID) | Pendiente |
| 4. Escala y operación | 3 sem | Redis, gestión de pools, rate limit, OpenTelemetry, Testcontainers, pruebas de carga | Pendiente |
| 5. Extensiones | continuo | Hooks de negocio, procedimientos como acciones, MySQL / Oracle, cargas masivas | Pendiente |

Equipo sugerido: 1 backend, 1 frontend, QA/DevOps parcial. v1 productiva en ~4–5 meses.

## Próximos pasos sugeridos (inicio de Fase 2)

1. Metadata store en PostgreSQL (conexiones + overrides) reemplazando `FileConnectionStore`.
2. Modelo de overrides por tabla/columna y su aplicación sobre `TableInfo`.
3. Pruebas de integración con Testcontainers contra ambos motores usando `samples/`.
4. Maestro-detalle a partir de `ReferencedBy` (la API ya lo expone).
