# Estado del proyecto

**Última actualización:** 2026-10-27

## En una línea

Fase 1 (MVP) terminada y fusionada en `main`. Principio rector: **se adapta a cualquier entorno, todo por configuración** (D-022).
**Cierre de la Fase 1 terminado** (PR #4, #5 y #6). **Bloque M en curso:** M.1 (abstracciones del dialecto) en el PR #7; sigue M.3, el proveedor MySQL/MariaDB (P-025, P-027, P-029). Después, la **Fase 2**.

## Ramas y PR

| Rama | Estado |
|---|---|
| `main` | Rama por defecto. Fase 1 completa: PR #1 a #6. |
| `feature/m1-abstracciones-dialecto` | PR #7: estrategia de inserción, alias cortos, capacidades por columna, números grandes, escape neutro, versión al conectar. |
| `feature/fase1-*` | Ya fusionadas en `main`; se pueden borrar (P-005). |

## Qué funciona hoy

- **Conexiones:** alta, edición, prueba y borrado. La cadena de conexión se guarda cifrada en `data/connections.json` (D-011).
- **Lectura de esquema** en SQL Server y PostgreSQL:
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
- **Login básico:** JWT con usuarios en configuración (D-012).
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

- 63 pruebas unitarias del núcleo (`backend/tests/Mantenedores.Tests`), incluidos el traductor de errores y las capacidades del dialecto (con un dialecto de prueba sin `RETURNING` y con parámetros `:`).
- 7 pruebas de la API en memoria (`HostingTests`): subruta, `<base href>`, health checks y 404 de API.
- Recorrido en navegador con la app publicada en `/mantenedores` contra PostgreSQL 12: login, conexión, alta con FK, recarga directa y salida. Ninguna petición sale de la subruta.
- 65 pruebas de integración (`backend/tests/Mantenedores.IntegrationTests`), en verde contra **PostgreSQL 12 y 16** y **SQL Server 2017 y 2022** (D-035).
- API probada a mano contra PostgreSQL 16 y SQL Server 2022 reales, con las bases de `samples/`.
- Flujo completo en navegador con Playwright (escritorio y móvil).
- CI de GitHub en verde (PR #1 y `main`).
- `docker build` se verifica en la CI (P-001); en el entorno remoto no se puede, por falta de acceso a NuGet dentro del build.

## Limitaciones conocidas

Ver detalle en `pendientes.md`:
- Solo SQL Server y PostgreSQL implementados; MySQL/MariaDB en el bloque M (P-025). Oracle fuera de alcance (D-034).
- Etiquetas y combos solo para FK de una columna (P-030).
- No se detectan índices únicos que no son restricciones (P-031).
- Arreglos, binarios, `geography` y `hierarchyid` no se editan (P-018).

## Notas del entorno de desarrollo remoto (sesiones de Claude Code en la nube)

- .NET 8 no viene instalado: `apt-get update && apt-get install -y dotnet-sdk-8.0`.
- Postgres 16 local disponible en `/usr/lib/postgresql/16/bin` (`initdb` + `pg_ctl` como usuario `postgres`).
- Docker requiere iniciar el daemon a mano (`dockerd &`). Docker Hub responde 429 (límite de descargas): usar el espejo `mirror.gcr.io/library/postgres:12` vía `MANTENEDORES_PG_IMAGE` y `TESTCONTAINERS_RYUK_DISABLED=true`. Las imágenes de SQL Server (mcr.microsoft.com) bajan sin problema.
- Dentro de `docker build` no hay acceso a NuGet, por eso la imagen no se pudo compilar ahí.
- Borrar ramas remotas o hacer force-push está bloqueado por permisos de la sesión: lo hace el dueño desde GitHub.
