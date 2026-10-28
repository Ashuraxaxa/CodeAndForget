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
| P-088 | Importar actualizando por clave (upsert) además de insertar (D-075) | 🟡 | ✅ PR #29 |
| P-100 | Interfaz en español, inglés y ruso, con selector y formato de fechas y números por idioma (D-076) | 🟡 | ✅ PR #30 |
| P-101 | Mensajes del servidor en el idioma elegido: errores de validación, de la base y descripciones de cambios de esquema según `Accept-Language` (hoy llegan en español) | ⚪ | ⏳ |
| P-102 | Nombres visibles por idioma en los ajustes (`label`, `helpText`), si algún equipo los necesita | ⚪ | ⏳ |
| P-089 | Árbol: elegir la FK cuando hay más de una autorreferencia (hoy se usa la primera) y mover nodos arrastrando | ⚪ | ⏳ |
| P-090 | Archivos: vista previa de imágenes en la grilla y en el formulario; editar `geography`/`geometry` como WKT | ⚪ | ⏳ |
| P-085 | Editor: FK virtuales compuestas desde la UI (hoy solo por la API) y arrastrar para ordenar columnas (hoy, botones ↑ ↓) | ⚪ | ⏳ |
| P-084 | UI: nombre visible de la tabla referenciada en "→ tabla", "Referenciada por", N:M y combos (D-074) | ⚪ | ✅ PR #28 |

## 2. Fase 3: seguridad

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-040 | Perfiles propios con usuarios locales (en la metadata) y roles del proveedor de identidad asociados (D-062) | 🔴 | ✅ PR #21 |
| P-042 | Permisos por conexión, tabla y columna (ver, crear, editar, borrar, exportar, importar) con herencia, aplicados en el servidor; pantalla "Seguridad" con el permiso `ManageSecurity` (D-063, D-066, D-067) | 🔴 | ✅ PR #21 |
| P-043 | Filtros de fila por rol | — | ⛔ descartado por el dueño (D-064) |
| P-044 | Auditoría de cambios (quién, cuándo, valor anterior y nuevo), 1 año configurable, con vista en la UI (D-065, D-068) | 🟡 | ✅ PR #22 |
| P-046 | Concurrencia optimista: no pisar un registro que otro cambió (D-069) | 🟡 | ✅ PR #23 |
| P-091 | Login: limitar intentos por IP (D-070) | 🟡 | ✅ PR #24 |
| P-092 | Usuarios propios: cachear unos segundos si siguen activos (hoy se consulta la metadata en cada solicitud del modo Local) | ⚪ | ⏳ |
| P-093 | Seguridad: elegir tablas y columnas de una lista también con la regla `*` (hoy se escriben a mano) y copiar un perfil | ⚪ | ⏳ |
| P-094 | Auditoría: mostrar etiquetas (no solo ids) en FK y vínculos N:M, y exportarla a CSV o Excel | ⚪ | ⏳ |
| P-095 | Auditoría: con `ViewAudit` restringido por perfil, mostrar solo las conexiones y tablas que el usuario ve | ⚪ | ⏳ |
| P-096 | Concurrencia: cerrar la ventana de milisegundos entre comparar y escribir con un bloqueo de fila por dialecto (`FOR UPDATE` / `UPDLOCK`), si algún equipo lo necesita | ⚪ | ⏳ |

## 3. Fase 4: escala y operación

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-050 | Caché del esquema y contadores de límites de uso en Redis, opcional por configuración (D-026, D-070) | ⚪ | ⏳ solo si hay varias instancias |
| P-051 | Pools con tope por conexión, cierre de inactivos y rate limit (D-070) | 🟡 | ✅ PR #24 |
| P-052 | Tablas grandes: total estimado, conteo con tope y turno para consultas pesadas (D-072) | 🟡 | ✅ PR #26 |
| P-098 | Paginación por clave (sin OFFSET), solo si aparecen tablas de decenas de millones de filas | ⚪ | ⏳ |
| P-053 | Observabilidad (OpenTelemetry, solicitudes lentas) y pruebas de carga con k6 (D-071, `docs/carga.md`) | 🟡 | ✅ PR #25 |
| P-097 | Correr el script de k6 (hasta ahora se midió con Node) contra una tabla grande en un servidor como el de producción, y anotar el resultado en `docs/carga.md` | ⚪ | ⏳ |

## 4. Fase 5: extensiones

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-060 | Hooks de negocio antes y después de guardar | — | ⛔ descartado por el dueño (D-073) |
| P-099 | Avisos por correo por tabla, apagados por defecto (D-073) | 🟡 | ✅ PR #27 |
| P-061 | Procedimientos almacenados como acciones | ⚪ | ⏳ |
| P-062 | Otros motores (Oracle, SQLite, Db2, Firebird…) solo si un equipo los pide | ⚪ | ⏳ |
| P-063 | Guía "cómo agregar un motor" | ✅ 2026-10-27 · en `docs/motores.md` (la batería común pasó a P-002) |
| P-064 | Cargar proveedores como plugins desde una carpeta, sin recompilar | ⚪ | ⏳ |

---

## Deuda técnica detectada (Fase 1)

| ID | Pendiente | Prio | Estado |
|---|---|---|---|
| P-030 | Etiquetas de FK, `/lookup` y combo del formulario con FK y PK de varias columnas (D-075) | 🟡 | ✅ PR #29 |
| P-031 | No se detectan índices únicos que no son restricciones (`sys.indexes.is_unique`, `pg_index.indisunique`) | ⚪ | ⏳ |
| P-032 | El filtro de una columna FK acepta el id o el texto que se muestra (D-074) | 🟡 | ✅ PR #28 |
| P-033 | Fechas en formato día/mes/año fijo, sin depender del idioma del navegador (D-074) | ⚪ | ✅ PR #28 |
| P-034 | La búsqueda usa `CAST(... AS nvarchar(max))` / `CAST(... AS text)`, que no aprovecha índices; revisar en tablas grandes | ⚪ | ⏳ (con P-052) |
| P-035 | `COUNT(*)` en cada página puede ser lento en tablas enormes | ⚪ | ✅ PR #26 (total estimado y con tope, D-072) |
| P-073 | Errores de permisos de la base traducidos (PG `42501`, SQL Server `229`/`230`, MySQL `1142`/`1143`) (D-074) | ⚪ | ✅ PR #28 |
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
| P-081 | Migraciones con varias instancias: bloqueo de sesión y verificación dentro de la transacción (D-060) | ✅ 2026-10-28 · PR #20 |
| P-045 | Secretos `${env:MNT_…}` / `${file:…}` en las cadenas de conexión, con prefijo y carpeta permitidos (D-061) | ✅ 2026-10-28 · PR #20 |
| P-018 | Tipos pendientes: archivos (subir en base64, descargar, tamaño), arreglos de PG como lista, `geography`/`geometry`/`hierarchyid` y espaciales de MySQL como texto (D-059) | ✅ 2026-10-27 · PR #19 |
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
