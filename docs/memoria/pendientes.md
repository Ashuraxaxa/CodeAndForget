# Pendientes

**Prioridad:** 🔴 alta (bloquea lo siguiente) · 🟡 media · ⚪ baja
**Estado:** ⏳ por hacer · 🚧 en curso · ✅ hecho (fecha · PR) · ⛔ bloqueado (motivo)

El detalle de cada bloque y su criterio de "listo" está en [../siguientes-pasos.md](../siguientes-pasos.md).

---

## 0. Cierre de la Fase 1

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-003 | Mensajes amigables para errores del motor (único, FK, CHECK, nulo) en ambos motores | 🟡 | ⏳ |
| P-004 | Guía de despliegue `docs/despliegue.md` con recetas (Docker, Kubernetes, IIS, servicio de Windows, systemd) + referencia `docs/configuracion.md` | 🟡 | ⏳ |
| P-007 | Publicar bajo subruta y detrás de proxy: `PathBase`, `ForwardedHeaders`, frontend con base relativa | 🔴 | ⏳ |
| P-008 | Health checks separados `/api/health/live` y `/api/health/ready` | 🟡 | ⏳ |
| P-005 | Borrar la rama `feature/fase1-mantenedores` (ya fusionada) | ⚪ | ⏳ dueño, desde GitHub |

## M. Multi-motor: MySQL/MariaDB (D-034, antes de la Fase 2)

Detalle técnico en [../motores.md](../motores.md).

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-020 | Dialecto: estrategia de inserción (filas, último id, valores provistos) + prefijo de parámetros fuera de `QueryBuilder` (D-031) | 🔴 | ⏳ |
| P-021 | Alias cortos (`l0`…) para etiquetas de FK, mapeados a `<columna>__label` (evita el corte de 63 bytes de PG) | 🔴 | ⏳ |
| P-022 | Capacidades por columna (ordenable / filtrable / buscable) en metadata, `QueryBuilder` y UI | 🟡 | ⏳ |
| P-023 | Números fuera del rango de `decimal` (.NET) como texto (PG `numeric`, MySQL `DECIMAL(65)`) | 🟡 | ⏳ hoy ya puede fallar en PG |
| P-024 | Escape neutro de LIKE (`!`) y ajustes de sesión (zona UTC en MySQL) | 🔴 | ⏳ |
| P-025 | Proveedor MySQL/MariaDB (`MySqlConnector`) + `samples/mysql` | 🔴 | ⏳ depende de P-020 a P-024 |
| P-026 | Proveedor Oracle | — | ⛔ fuera de alcance (D-034); análisis en `docs/motores.md` |
| P-027 | Agregar MySQL 8.0/8.4 y MariaDB 10.6/11.4 a la matriz de CI (la matriz con SQL Server y PG ya existe) | 🔴 | ⏳ depende de P-002 |
| P-028 | Verificar la versión mínima (D-035) al probar y al abrir una conexión, y mostrar ajustes relevantes (`sql_mode`, intercalación) | 🔴 | ⏳ (las pruebas ya la verifican; falta en el producto) |
| P-029 | UI: nuevos motores en el selector, ejemplos de cadena y controles según capacidades | 🟡 | ⏳ |

## 1. Fase 2: configuración y personalización

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-041 | Autenticación configurable: `Auth:Mode` = `Local` · `Jwt` (OIDC o llave/JWKS), mapeo de claims, `/api/auth/config`, login PKCE en la SPA. Probar con Duende y Keycloak (D-024) | 🔴 | ⏳ adelantado desde Fase 3 |
| P-010 | Metadata store configurable (`Sqlite` · `Postgres` · `SqlServer`) + migraciones DbUp + `SqlConnectionStore` + importar `connections.json` (D-023) | 🔴 | ⏳ |
| P-011 | Modelo de overrides (tabla y columna) + `OverrideApplier` + endpoints | 🔴 | ⏳ depende de P-010 |
| P-012 | Editor de overrides en la UI con vista previa | 🟡 | ⏳ depende de P-011 |
| P-013 | Diferencias de esquema (snapshot + `/diff` + aviso de overrides huérfanos) | 🟡 | ⏳ depende de P-010 |
| P-014 | Maestro-detalle con pestañas de tablas hijas y FK precompletada | 🟡 | ⏳ |
| P-015 | N:M: detección de tablas puente + selector múltiple + sincronización en transacción | 🟡 | ⏳ |
| P-016 | Árboles para autorreferencias + validación de ciclos | ⚪ | ⏳ |
| P-017 | Exportar (CSV/Excel por streaming) e importar con vista previa y transacción | 🟡 | ⏳ |
| P-018 | Tipos pendientes: arreglos PG, binarios (subir/bajar), `geography`/`hierarchyid` como texto | ⚪ | ⏳ |

## 2. Fase 3: seguridad

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-040 | Usuarios y roles locales en el metadata store (solo modo `Local`) | 🟡 | ⏳ |
| P-042 | RBAC por conexión, tabla, columna y operación, asociado a nombres de rol de los claims (D-028) y aplicado en `QueryBuilder` | 🔴 | ⏳ |
| P-043 | Filtros de fila por rol | 🟡 | ⏳ |
| P-044 | Auditoría (quién, cuándo, valor anterior y nuevo) + vista en la UI | 🟡 | ⏳ depende de Q-007 |
| P-045 | Llaves en la metadata (`DataProtection:KeyStore`), certificado opcional y referencias `${env:}` / `${file:}` en cadenas de conexión (D-025) | 🟡 | ⏳ |
| P-046 | Concurrencia optimista (`rowversion` / `xmin`) | 🟡 | ⏳ |

## 3. Fase 4: escala y operación

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-050 | Caché del esquema en Redis, opcional por configuración (D-026) | ⚪ | ⏳ solo si hay varias instancias |
| P-051 | Pools con tope por conexión, cierre de inactivos y rate limit | 🟡 | ⏳ |
| P-052 | Paginación por clave y conteo estimado para tablas grandes | 🟡 | ⏳ depende de Q-004 |
| P-053 | Observabilidad (OpenTelemetry, consultas lentas) y pruebas de carga con k6 | 🟡 | ⏳ |

## 4. Fase 5: extensiones

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-060 | Hooks de negocio antes y después de guardar | ⚪ | ⏳ depende de Q-005 |
| P-061 | Procedimientos almacenados como acciones | ⚪ | ⏳ |
| P-062 | Otros motores (Oracle, SQLite, Db2, Firebird…) solo si un equipo los pide | ⚪ | ⏳ |
| P-063 | Guía "cómo agregar un motor" | ✅ 2026-10-27 · en `docs/motores.md` (la batería común pasó a P-002) |
| P-064 | Cargar proveedores como plugins desde una carpeta, sin recompilar | ⚪ | ⏳ |

---

## Deuda técnica detectada (Fase 1)

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-030 | Etiquetas de FK y `/lookup` solo funcionan con FK y PK de una columna; extender a compuestas | 🟡 | ⏳ (con P-014) |
| P-031 | No se detectan índices únicos que no son restricciones (`sys.indexes.is_unique`, `pg_index.indisunique`) | ⚪ | ⏳ |
| P-032 | El filtro de una columna FK en la grilla pide el id, no permite buscar por la etiqueta | 🟡 | ⏳ |
| P-033 | Las fechas se muestran con el formato del navegador (en inglés si el navegador está en inglés); fijar `es` | ⚪ | ⏳ |
| P-034 | La búsqueda usa `CAST(... AS nvarchar(max))` / `CAST(... AS text)`, que no aprovecha índices; revisar en tablas grandes | ⚪ | ⏳ (con P-052) |
| P-035 | `COUNT(*)` en cada página puede ser lento en tablas enormes | ⚪ | ⏳ (con P-052) |
| P-036 | Credenciales de demo (`admin`/`admin`, hash y JwtKey) en `appsettings.Development.json` y `docker-compose.yml`: documentar que se cambian en producción | 🟡 | ⏳ (con P-004) |
| P-038 | El frontend usa rutas absolutas (`/api`, `/c/...`): no funciona bajo una subruta | 🔴 | ⏳ (con P-007) |
| P-039 | `Program.cs` falla al arrancar sin `Auth:JwtKey` aunque en el futuro el modo `Jwt` no la necesite | 🟡 | ⏳ (con P-041) |
| P-037 | El `DisplayColumn` automático elige mal en algunos casos (p. ej. `ventas.Pedido` usa `Estado` en SQL Server) | ⚪ | ⏳ se resuelve con P-011 |

---

## Hechos

| ID | Pendiente | Estado |
|---|---|---|
| P-000 | Estructura base de la Fase 1: backend, proveedores, API, UI, ejemplos, CI | ✅ 2026-10-27 · PR #1 |
| P-006 | Hoja de ruta + memoria del proyecto | ✅ 2026-10-27 · PR #2 |
| P-001 | Job de CI que compila la imagen Docker y verifica que arranca | ✅ 2026-10-27 · PR #4 |
| P-002 | Batería común por proveedor (57 pruebas, Testcontainers) contra PG 12/16 y SQL Server 2017/2022 | ✅ 2026-10-27 · PR #4 |
| P-070 | La base de ejemplo de PG no funcionaba en PG 12 (`pgcrypto`) y no tenía trigger como la de SQL Server | ✅ 2026-10-27 · PR #4 |
| P-071 | Fechas y horas con formato distinto según el motor (D-036) | ✅ 2026-10-27 · PR #4 |
