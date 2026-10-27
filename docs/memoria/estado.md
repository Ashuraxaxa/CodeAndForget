# Estado del proyecto

**Última actualización:** 2026-10-27

## En una línea

Fase 1 (MVP) terminada y fusionada en `main`. Principio rector: **se adapta a cualquier entorno, todo por configuración** (D-022).
**Fase 1 y bloque M terminados** (PR #1 a #8). **Fase 2 en curso:** login configurable (PR #9, fusionado) y metadata configurable (PR #10). Siguiente: P-011, los ajustes por tabla y columna (overrides).

## Ramas y PR

| Rama | Estado |
|---|---|
| `main` | Rama por defecto. Fase 1, bloque M y login configurable: PR #1 a #9. |
| `feature/f2-metadata-store` | PR #10: metadata en SQLite/Postgres/SQL Server, migraciones, llaves en la base, importación de la Fase 1. |
| `feature/fase1-*`, `feature/m1-*`, `feature/m3-*` | Ya fusionadas en `main`; se pueden borrar (P-005). |

## Qué funciona hoy

- **Conexiones:** alta, edición, prueba y borrado. Se guardan con la cadena cifrada en la metadata del servicio: SQLite por defecto, o Postgres / SQL Server (D-023, D-047).
- **Varias instancias** comparten conexiones y llaves con metadata en Postgres/SQL Server y `DataProtection:KeyStore=Database`.
- **Lectura de esquema** en SQL Server, PostgreSQL, MySQL y MariaDB:
  - Tablas, vistas, columnas y tipos.
  - PK simples y compuestas, restricciones UNIQUE y FK.
  - Identity, serial, columnas calculadas y `rowversion`.
  - Enums de Postgres.
- **CRUD genérico:**
  - Listado con orden, filtros por columna, búsqueda en textos y paginación (máx. 200).
  - Alta, edición y borrado, también con PK compuesta.
- **Etiquetas de FK:** los listados traen `<columna>__label` (D-007) y hay combos con búsqueda (`/lookup`).
- **Solo lectura:** vistas y tablas sin PK (D-009).
- **SQL Server:** la inserción funciona también en tablas con triggers (D-010).
- **Login configurable** (D-024, D-046): modo `Local` (usuarios en configuración) o `Jwt` con cualquier emisor OIDC (Keycloak probado en vivo; Duende y Entra ID documentados), con roles desde los claims, login PKCE en la UI y `/api/auth/me`.
- **UI:**
  - Login, conexiones y menú de tablas.
  - Grilla y formulario dinámico.
  - Funciona en pantallas de teléfono.
- **Despliegue:** una sola app; la API sirve el frontend (D-015), en la raíz o bajo cualquier subruta y detrás de un proxy (D-037).
- **Health checks:** `/api/health/live` y `/api/health/ready`.
- **Errores de la base traducidos** (duplicado, FK, CHECK, nulo) con el campo afectado, iguales en ambos motores (D-038).
- **Corre como** contenedor, IIS, servicio de Windows o systemd (D-039); guía en `docs/despliegue.md`.
- **Prueba de conexión** con producto y versión del servidor; los servidores por debajo del mínimo se rechazan (D-043).
- **Números de más de 28 dígitos** y columnas no ordenables (xml, json…) soportados (D-041, D-042).

## Cómo se verificó (2026-10-27)

- 82 pruebas unitarias del núcleo, 17 de la API en memoria (`HostingTests`, `AuthTests`) y 7 de la metadata en SQLite (`MetadataStoreTests`), entre ellas dos instancias de la API que comparten base y llaves. (`backend/tests/Mantenedores.Tests`), incluidos el traductor de errores y las capacidades del dialecto (con un dialecto de prueba sin `RETURNING` y con parámetros `:`).
- Recorrido en navegador con la app publicada en `/mantenedores` contra PostgreSQL 12: login, conexión, alta con FK, recarga directa y salida. Ninguna petición sale de la subruta.
- 139 pruebas de integración (incluye el almacén de metadata en PG 12/16 y SQL Server 2017/2022) (`backend/tests/Mantenedores.IntegrationTests`), en verde contra **PostgreSQL 12 y 16**, **SQL Server 2017 y 2022**, **MySQL 8.0 y 8.4** y **MariaDB 10.6 y 11.4** (D-035).
- Login con **Keycloak 26** en el navegador: PKCE, vuelta a la app, API con el token, roles anidados (`realm_access.roles`), cierre de sesión sin confirmación; el token no queda en `localStorage`.
- Recorrido en navegador contra MySQL 8.0: prueba de conexión con versión, alta con combo FK (`LAST_INSERT_ID`), duplicado traducido en el campo y cliente con UUID generado por la app.
- API probada a mano contra PostgreSQL 16 y SQL Server 2022 reales, con las bases de `samples/`.
- Flujo completo en navegador con Playwright (escritorio y móvil).
- CI de GitHub en verde (PR #1 y `main`).
- `docker build` se verifica en la CI (P-001); en el entorno remoto no se puede, por falta de acceso a NuGet dentro del build.

## Limitaciones conocidas

Ver detalle en `pendientes.md`:
- Oracle fuera de alcance (D-034).
- Etiquetas y combos solo para FK de una columna (P-030).
- No se detectan índices únicos que no son restricciones (P-031).
- Arreglos, binarios, `geography` y `hierarchyid` no se editan (P-018).

## Notas del entorno de desarrollo remoto (sesiones de Claude Code en la nube)

- .NET 8 no viene instalado: `apt-get update && apt-get install -y dotnet-sdk-8.0`.
- Postgres 16 local disponible en `/usr/lib/postgresql/16/bin` (`initdb` + `pg_ctl` como usuario `postgres`).
- quay.io está bloqueado: Keycloak se baja como `mirror.gcr.io/keycloak/keycloak:26.0`.
- Docker requiere iniciar el daemon a mano (`dockerd &`). Docker Hub responde 429 (límite de descargas): usar el espejo `mirror.gcr.io/library/<imagen>` (postgres, mysql, mariadb) vía `MANTENEDORES_*_IMAGE` y `TESTCONTAINERS_RYUK_DISABLED=true`. Las imágenes de SQL Server (mcr.microsoft.com) bajan sin problema.
- Dentro de `docker build` no hay acceso a NuGet, por eso la imagen no se pudo compilar ahí.
- Borrar ramas remotas o hacer force-push está bloqueado por permisos de la sesión: lo hace el dueño desde GitHub.
