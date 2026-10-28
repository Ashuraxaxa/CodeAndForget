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
| P-078 | Prueba en vivo con Duende IdentityServer (requiere un host .NET de prueba con Duende; la config está documentada) | 🟡 | ⏳ |
| P-079 | Renovación silenciosa sin refresh token (iframe `silent_redirect_uri`); hoy, sin refresh token, al vencer se vuelve a iniciar sesión | ⚪ | ⏳ |
| P-082 | Ajustes con varias instancias: un ajuste guardado en una instancia solo se ve en las otras al refrescar o reiniciar (caché en memoria). Opciones: TTL corto del esquema combinado o caché compartida (P-050) | 🟡 | ⏳ |
| P-086 | Detectar cambios de esquema sin que nadie pulse ↻ (p. ej. relectura periódica configurable o al fallar una consulta por columna inexistente) | ⚪ | ⏳ |
| P-087 | N:M: ajuste para desactivar el selector de un puente o mostrarlo como pestaña; puentes con columnas extra (p. ej. `cantidad`) como hijas con su propio formulario | ⚪ | ⏳ |
| P-088 | Importar actualizando por clave (upsert) además de insertar; hoy solo inserta y las columnas de solo lectura (identity) se ignoran | 🟡 | ⏳ |
| P-089 | Árbol: elegir la FK cuando hay más de una autorreferencia (hoy se usa la primera) y mover nodos arrastrando | ⚪ | ⏳ |
| P-085 | Editor: FK virtuales compuestas desde la UI (hoy solo por la API) y arrastrar para ordenar columnas (hoy, botones ↑ ↓) | ⚪ | ⏳ |
| P-084 | UI: usar la etiqueta de la tabla referenciada (no su nombre) en "→ categoria" y en "Referenciada por" | ⚪ | ⏳ |
| P-081 | Migraciones con varias instancias arrancando a la vez: bloqueo (p. ej. `pg_advisory_lock`, `sp_getapplock`) para que no corran dos veces en paralelo | 🟡 | ⏳ |
| P-018 | Tipos pendientes: arreglos PG, binarios (subir/bajar), `geography`/`hierarchyid` como texto | ⚪ | ⏳ |

## 2. Fase 3: seguridad

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-040 | Usuarios y roles locales en el metadata store (solo modo `Local`) | 🟡 | ⏳ |
| P-042 | RBAC por conexión, tabla, columna y operación, asociado a nombres de rol de los claims (D-028) y aplicado en `QueryBuilder`. Apartado de seguridad propio (perfiles y usuarios) que también asigne los permisos de D-053; **el dueño lo quiere conversar antes de diseñarlo** | 🔴 | ⏳ |
| P-043 | Filtros de fila por rol | 🟡 | ⏳ |
| P-044 | Auditoría (quién, cuándo, valor anterior y nuevo) + vista en la UI | 🟡 | ⏳ depende de Q-007 |
| P-045 | Referencias `${env:VAR}` / `${file:/ruta}` en las cadenas de conexión (D-025) (las llaves en la base y el certificado ya están, PR #10) | 🟡 | ⏳ |
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
| P-030 | Etiquetas de FK y `/lookup` solo funcionan con FK y PK de una columna; extender a compuestas (el filtro de hijas por FK compuesta ya funciona, PR #15) | 🟡 | ⏳ |
| P-031 | No se detectan índices únicos que no son restricciones (`sys.indexes.is_unique`, `pg_index.indisunique`) | ⚪ | ⏳ |
| P-032 | El filtro de una columna FK en la grilla pide el id, no permite buscar por la etiqueta | 🟡 | ⏳ |
| P-033 | Las fechas se muestran con el formato del navegador (en inglés si el navegador está en inglés); fijar `es` | ⚪ | ⏳ |
| P-034 | La búsqueda usa `CAST(... AS nvarchar(max))` / `CAST(... AS text)`, que no aprovecha índices; revisar en tablas grandes | ⚪ | ⏳ (con P-052) |
| P-035 | `COUNT(*)` en cada página puede ser lento en tablas enormes | ⚪ | ⏳ (con P-052) |
| P-073 | Traducir también errores de permisos (PG `42501`, SQL Server `229`/`230`) a "el usuario de la base no tiene permiso para …" | ⚪ | ⏳ |
| P-074 | Health check del contenedor sin `curl`/`wget` en la imagen (p. ej. subcomando `healthcheck` de la propia app) | ⚪ | ⏳ |
| P-075 | Revisar otros tipos definidos por el usuario en PG (compuestos, rangos) frente a la caché de tipos de Npgsql (D-041); hoy se marcan `Unsupported` | ⚪ | ⏳ |

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
| P-010 | Metadata en `Sqlite` (por defecto), `Postgres` o `SqlServer`, con migraciones, `SqlConnectionStore` e importación de `connections.json` (D-047, D-048) | ✅ 2026-10-27 · PR #10 |
| P-080 | Llaves de Data Protection en la base (`DataProtection:KeyStore=Database`) y certificado opcional: varias instancias comparten conexiones | ✅ 2026-10-27 · PR #10 |
| P-016 | Árboles para autorreferencias: vista por niveles, "＋ hijo" y validación de ciclos al guardar (D-058) | ✅ 2026-10-27 · PR #18 |
| P-017 | Exportar (CSV, Excel, JSON, XML, por streaming) e importar los mismos formatos con vista previa, asociación de columnas y transacción (D-057) | ✅ 2026-10-27 · PR #17 |
| P-015 | Relaciones N:M: detección de tablas puente, selector múltiple en el formulario y `PUT /row/links` en transacción (D-056) | ✅ 2026-10-27 · PR #16 |
| P-014 | Maestro-detalle: pestañas de hijas al editar, alta con FK fija y vuelta al padre, filtro exacto `e.`, ajuste `children` (D-055) | ✅ 2026-10-27 · PR #15 |
| P-013 | Diferencias de esquema: foto de referencia, `/diff`, aviso en la UI, aceptar cambios y quitar ajustes huérfanos en el editor (D-054) | ✅ 2026-10-27 · PR #14 |
| P-083 | Permisos por acción: `ConfigureTables` y `ManageConnections`, otorgados a roles por `Auth:Permissions` (D-053) | ✅ 2026-10-27 · PR #13 |
| P-012 | Editor de ajustes en la UI: pestañas Tabla y Columnas, orden, validaciones, PK y FK virtuales, vista previa en vivo y restablecer (D-052) | ✅ 2026-10-27 · PR #12 |
| P-011 | Ajustes por tabla y columna: modelo, `OverrideApplier`, almacén (`002_overrides`), endpoints solo admin, reglas en el servidor, render en la UI (D-049 a D-051) | ✅ 2026-10-27 · PR #11 |
| P-037 | `DisplayColumn` automático mal elegido: se corrige con el ajuste `displayColumn` | ✅ 2026-10-27 · PR #11 |
| P-041 | Autenticación configurable: modo `Local` o `Jwt` (Authority/Metadata/SigningKey), mapeo de claims, `/api/auth/config`, `/api/auth/me`, login PKCE en la UI (D-046) | ✅ 2026-10-27 · PR #9 |
| P-039 | `Auth:JwtKey` se exige solo en modo `Local` | ✅ 2026-10-27 · PR #9 |
| P-077 | PK UUID con default en motores sin `RETURNING`: la genera la app (D-044) | ✅ 2026-10-27 · PR #8 |
| P-076 | Enums de PG ilegibles si se crean después de la primera conexión (caché de Npgsql) (D-041) | ✅ 2026-10-27 · PR #7 |
| P-072 | `Storage:DataDirectory` relativa se resolvía contra el directorio de trabajo (`System32` como servicio de Windows) | ✅ 2026-10-27 · PR #6 |
