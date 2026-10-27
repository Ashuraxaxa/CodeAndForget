# Pendientes

**Prioridad:** 🔴 alta (bloquea lo siguiente) · 🟡 media · ⚪ baja
**Estado:** ⏳ por hacer · 🚧 en curso · ✅ hecho (fecha · PR) · ⛔ bloqueado (motivo)

El detalle de cada bloque y su criterio de "listo" está en [../siguientes-pasos.md](../siguientes-pasos.md).

---

## 0. Cierre de la Fase 1

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-001 | Job de CI que ejecute `docker build` y valide la imagen | 🔴 | ⏳ |
| P-002 | Pruebas de integración con Testcontainers (PG 16 + MSSQL 2022) usando `samples/` | 🔴 | ⏳ |
| P-003 | Mensajes amigables para errores del motor (único, FK, CHECK, nulo) en ambos motores | 🟡 | ⏳ |
| P-004 | Guía de despliegue `docs/despliegue.md` con recetas (Docker, Kubernetes, IIS, servicio de Windows, systemd) + referencia `docs/configuracion.md` | 🟡 | ⏳ |
| P-007 | Publicar bajo subruta y detrás de proxy: `PathBase`, `ForwardedHeaders`, frontend con base relativa | 🔴 | ⏳ |
| P-008 | Health checks separados `/api/health/live` y `/api/health/ready` | 🟡 | ⏳ |
| P-005 | Borrar la rama `feature/fase1-mantenedores` (ya fusionada) | ⚪ | ⏳ dueño, desde GitHub |

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
| P-062 | Proveedores MySQL/MariaDB y Oracle | 🟡 | ⏳ |
| P-063 | Batería común de pruebas que todo proveedor debe pasar + guía "cómo agregar un motor" | 🟡 | ⏳ (base en P-002) |
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
