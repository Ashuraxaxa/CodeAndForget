# Pendientes

**Prioridad:** 🔴 alta (bloquea lo siguiente) · 🟡 media · ⚪ baja
**Estado:** ⏳ por hacer · 🚧 en curso · ✅ hecho (fecha · PR) · ⛔ bloqueado (motivo)

El detalle de cada bloque y su criterio de "listo" está en [../siguientes-pasos.md](../siguientes-pasos.md).

---

## 0. Cierre de la Fase 1

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-005 | Borrar la rama `feature/fase1-mantenedores` (ya fusionada) | ⚪ | ⏳ dueño, desde GitHub |

## M. Multi-motor: MySQL/MariaDB (D-034, antes de la Fase 2) · ✅ completo (PR #7 y #8)

Detalle técnico en [../motores.md](../motores.md).

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-026 | Proveedor Oracle | — | ⛔ fuera de alcance (D-034); análisis en `docs/motores.md` |

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
| P-039 | `Program.cs` falla al arrancar sin `Auth:JwtKey` aunque en el futuro el modo `Jwt` no la necesite | 🟡 | ⏳ (con P-041) |
| P-073 | Traducir también errores de permisos (PG `42501`, SQL Server `229`/`230`) a "el usuario de la base no tiene permiso para …" | ⚪ | ⏳ |
| P-074 | Health check del contenedor sin `curl`/`wget` en la imagen (p. ej. subcomando `healthcheck` de la propia app) | ⚪ | ⏳ |
| P-075 | Revisar otros tipos definidos por el usuario en PG (compuestos, rangos) frente a la caché de tipos de Npgsql (D-041); hoy se marcan `Unsupported` | ⚪ | ⏳ |
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
| P-007 | Publicar bajo subruta (`Hosting:PathBase`) y detrás de proxy (`Hosting:ForwardedHeaders`) (D-037) | ✅ 2026-10-27 · PR #5 |
| P-008 | Health checks `/api/health/live` y `/api/health/ready` | ✅ 2026-10-27 · PR #5 |
| P-038 | Frontend con rutas relativas a `<base href>` (funciona bajo subruta) | ✅ 2026-10-27 · PR #5 |
| P-003 | Errores de restricciones traducidos por código + metadata, iguales en ambos motores (D-038) | ✅ 2026-10-27 · PR #6 |
| P-004 | `docs/despliegue.md`: Docker, Compose, Kubernetes, nginx, IIS, servicio de Windows, systemd, respaldo y permisos mínimos (D-039) | ✅ 2026-10-27 · PR #6 |
| P-036 | Credenciales de demo documentadas como solo demo (`configuracion.md`, `despliegue.md`) | ✅ 2026-10-27 · PR #6 |
| P-020 | Estrategia de inserción (devuelta / último id / provista) + prefijo de parámetros en el dialecto (D-040) | ✅ 2026-10-27 · PR #7 |
| P-021 | Alias cortos `__lblN` para etiquetas de FK, renombrados en la respuesta (D-041) | ✅ 2026-10-27 · PR #7 |
| P-022 | Capacidades por columna (ordenable / filtrable) en metadata, `QueryBuilder` y UI (D-042) | ✅ 2026-10-27 · PR #7 |
| P-023 | Números de más de 28 dígitos como texto en PG y SQL Server (D-041) | ✅ 2026-10-27 · PR #7 |
| P-024 | Escape de LIKE neutro `!` + `InitializeSessionAsync` para ajustes de sesión | ✅ 2026-10-27 · PR #7 |
| P-028 | Versión mínima verificada al probar y al leer el esquema, con ajustes del servidor (D-043) | ✅ 2026-10-27 · PR #7 |
| P-025 | Proveedor MySQL/MariaDB (`MySqlConnector`) + `samples/mysql`; batería común en verde en MySQL 8.0/8.4 y MariaDB 10.6/11.4 (D-045) | ✅ 2026-10-27 · PR #8 |
| P-027 | MySQL y MariaDB en la matriz de CI (versión mínima y actual) | ✅ 2026-10-27 · PR #8 |
| P-029 | UI: MySQL/MariaDB en el selector, ejemplo de cadena y ayuda de esquemas según el motor | ✅ 2026-10-27 · PR #8 |
| P-077 | PK UUID con default en motores sin `RETURNING`: la genera la app (D-044) | ✅ 2026-10-27 · PR #8 |
| P-076 | Enums de PG ilegibles si se crean después de la primera conexión (caché de Npgsql) (D-041) | ✅ 2026-10-27 · PR #7 |
| P-072 | `Storage:DataDirectory` relativa se resolvía contra el directorio de trabajo (`System32` como servicio de Windows) | ✅ 2026-10-27 · PR #6 |
