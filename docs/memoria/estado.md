# Estado del proyecto

**Última actualización:** 2026-10-28

## En una línea

Fase 1 (MVP) terminada y fusionada en `main`. Principio rector: **se adapta a cualquier entorno, todo por configuración** (D-022).
**Fase 1 y bloque M terminados** (PR #1 a #8). **Fase 2 en curso:** login configurable (PR #9), metadata configurable (PR #10) y ajustes por tabla y columna (PR #11) su editor en la UI (PR #12) permisos por acción (PR #13) aviso de cambios del esquema (PR #14), maestro-detalle (PR #15), relaciones N:M (PR #16), exportar/importar (PR #17) árboles (PR #18) y tipos pendientes (PR #19). **Fase 2 completa.** Fase 3 decidida con el dueño (D-062 a D-066) y en curso: migraciones con bloqueo y secretos (PR #20), perfiles y permisos (PR #21) auditoría de cambios (PR #22) y concurrencia optimista (PR #23). **Fase 3 completa.** Fase 4 en curso: tope del pool y límites de uso (PR #24), observabilidad y pruebas de carga (PR #25). tablas grandes (PR #26). Fase 5: avisos por correo por tabla (PR #27). Tanda 1 de mejoras: filtro de FK por nombre, permisos de la base, fechas y nombres visibles (PR #28). Importar con actualización y FK compuestas (PR #29). Interfaz en español, inglés y ruso (PR #30). Siguiente: tanda 2 (operación: P-082, P-074, P-092).

## Ramas y PR

| Rama | Estado |
|---|---|
| `main` | Rama por defecto. Fases 1 a 4 completas y Fase 5 con avisos por correo: PR #1 a #27. |
| `feature/t1-ajustes` | PR #28: filtro de FK por nombre, permisos de la base, fechas y nombres visibles. |
| `feature/t1-importar-fk` | PR #29 (sale de la rama del #28): importar actualizando por clave y FK de varias columnas. |
| `feature/i18n` | PR #30 (sale de la rama del #29): interfaz en español, inglés y ruso, con fechas por idioma. |
| `feature/fase1-*`, `feature/m1-*`, `feature/m3-*`, `feature/f2-*`, `feature/f3-*`, `feature/f4-*`, `feature/f5-avisos` | Ya fusionadas en `main`; se pueden borrar (P-005). |

## Qué funciona hoy

- **Conexiones:** alta, edición, prueba y borrado. Se guardan con la cadena cifrada en la metadata del servicio: SQLite por defecto, o Postgres / SQL Server (D-023, D-047).
- **Secretos fuera de la metadata** (D-061): `Password=${env:MNT_…}` o `${file:…}` en la cadena de conexión.
- **Varias instancias** migran sin chocar (D-060) y comparten conexiones y llaves con metadata en Postgres/SQL Server y `DataProtection:KeyStore=Database`.
- **Lectura de esquema** en SQL Server, PostgreSQL, MySQL y MariaDB:
  - Tablas, vistas, columnas y tipos.
  - PK simples y compuestas, restricciones UNIQUE y FK.
  - Identity, serial, columnas calculadas y `rowversion`.
  - Enums de Postgres.
- **CRUD genérico:**
  - Listado con orden, filtros por columna, búsqueda en textos y paginación (máx. 200).
  - Alta, edición y borrado, también con PK compuesta.
- **Etiquetas de FK:** los listados traen `<columna>__label` (D-007) y hay combos con búsqueda (`/lookup`).
- **Solo lectura:** vistas y tablas sin PK (D-009), salvo que un ajuste les dé una PK virtual.
- **Ajustes sin tocar código** (D-049, `docs/ajustes.md`): etiquetas, grupos del menú, columnas ocultas, orden, solo lectura, obligatorias, patrón, rango, lista de opciones, control de la UI, columna a mostrar, orden por defecto, PK y FK virtuales. Se aplican al instante, los cambia quien tiene el permiso `ConfigureTables` (D-053) y lo que deja de aplicar se avisa en el menú. Se editan con **⚙ Configurar** (pestañas Tabla y Columnas, vista previa en vivo, restablecer) o por la API.
- **SQL Server:** la inserción funciona también en tablas con triggers (D-010).
- **Seguridad propia** (D-062 a D-067, `docs/seguridad.md`): perfiles con roles y usuarios, permisos de la app y permisos sobre los datos por conexión, tabla y columna (ver, crear, editar, borrar, exportar, importar). Usuarios propios con contraseña en hash. Sin perfiles, todo sigue abierto. Pantalla **Seguridad** con el permiso `ManageSecurity`.
- **Idiomas** (D-076): la interfaz en español, inglés o ruso, con selector en la barra superior y en el login; fechas y números con el formato de cada idioma (28/10/2026, 10/28/2026, 28.10.2026). Los mensajes que arma el servidor siguen en español (P-101).
- **Importar actualizando** (D-075): además de solo agregar, "agregar y actualizar por clave", con la vista previa de cuántas se crean y cuántas se actualizan.
- **FK de varias columnas** (D-075): etiqueta en la grilla y un solo combo en el formulario (p. ej. bodega → sucursal por país + código).
- **Avisos por correo** (D-073): una tabla crítica puede avisar a quien se configure cuando se crea, edita o borra un registro, con quién, cuándo y qué cambió. Apagado por defecto; requiere SMTP en `Notifications`.
- **Tablas grandes** (D-072): en tablas de 500.000 filas o más, el total se estima sin filtros ("≈") y se cuenta hasta 1.000 con filtros ("más de 1.000"); las búsquedas pesadas corren de a 4 por conexión para no trabar el resto. Probado con 1 millón de filas.
- **Observabilidad** (D-071): trazas y métricas por OTLP si se configura un destino (incluye el pool de PostgreSQL y MySQL); solicitudes lentas en el log; logs en JSON por configuración; script de carga k6.
- **Tope y límites** (D-070): máximo de conexiones por base y cierre de inactivas, configurables; 600 solicitudes por minuto por usuario, 10 exportaciones o importaciones y 10 intentos de login por minuto (por IP); `429` al pasarse.
- **Edición simultánea** (D-069): si otra persona guardó el registro mientras se editaba, no se pisa. El formulario muestra qué cambió y deja ver lo actual o guardar igual; borrar desde una lista vieja también se rechaza.
- **Auditoría** (D-065, D-068): cada alta, edición (solo lo que cambió), baja, fila importada y cambio N:M con quién y cuándo; 1 año configurable con limpieza automática. Página **Auditoría** con filtros y **Historial** desde cada registro, con el permiso `ViewAudit`.
- **Permisos por acción** (D-053, D-066): `ConfigureTables`, `ManageConnections`, `ManageSecurity` y `ViewAudit`, cada uno otorgado a roles por configuración (por defecto `mnt-configurador`, `mnt-conexiones`, `mnt-seguridad` y `mnt-auditor`; los administradores tienen todos) o a perfiles. La UI oculta lo que el usuario no puede hacer.
- **Tipos especiales** (D-059): archivos (subir, descargar, tamaño y tope), arreglos de PostgreSQL como lista, y `geography`/`geometry`/`hierarchyid` y tipos espaciales de MySQL como texto de solo lectura.
- **Árboles** (D-058): las tablas con autorreferencia (p. ej. empleado → jefe) tienen vista de árbol por niveles, "＋ hijo" y validación de ciclos al guardar.
- **Exportar e importar** (D-057): CSV, Excel, JSON y XML del listado filtrado, por streaming; importación de los mismos formatos con asociación de columnas, errores por fila y una sola transacción. 5.000 productos: validación en 0,4 s e importación en 2,3 s (PostgreSQL 16).
- **Relaciones N:M** (D-056): las tablas puente se detectan solas; en el formulario, un selector múltiple (p. ej. las etiquetas de un producto) que se guarda con el registro, en una transacción.
- **Maestro-detalle** (D-055): al editar, pestañas con las tablas hijas filtradas por su FK (también compuestas o virtuales); "Nuevo" llega con la FK completada y vuelve al padre. El ajuste `children` elige cuáles y en qué orden.
- **Cambios de esquema** (D-054): al refrescar se compara con una foto de referencia; aviso con el detalle (tablas, columnas, tipos, nulos, PK, FK) y los ajustes sin efecto, hasta que alguien con `ConfigureTables` lo acepta. Borrar una columna en la base no rompe el mantenedor.
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

- 158 pruebas unitarias (`backend/tests/Mantenedores.Tests`): núcleo (incluidos los ajustes, el traductor de errores y las capacidades del dialecto), API en memoria (`HostingTests`, `AuthTests`, con el permiso de administrador para los ajustes) y metadata en SQLite y archivo (`MetadataStoreTests`), entre ellas dos instancias de la API que comparten base y llaves.
- Recorrido en navegador con la app publicada en `/mantenedores` contra PostgreSQL 12: login, conexión, alta con FK, recarga directa y salida. Ninguna petición sale de la subruta.
- 239 pruebas de integración (incluye ajustes, CRUD con un perfil restringido, usuario de la base de solo lectura, filtro de FK por etiqueta, concurrencia optimista, total estimado y con tope, auditoría del CRUD y su almacén en PG y SQL Server, cambios de esquema, filtro exacto, N:M, exportar/importar, árboles y archivos en la batería común; arreglos, geografía y jerarquía en las pruebas propias de cada motor y el almacén de metadata en PG 12/16 y SQL Server 2017/2022) (`backend/tests/Mantenedores.IntegrationTests`), en verde contra **PostgreSQL 12 y 16**, **SQL Server 2017 y 2022**, **MySQL 8.0 y 8.4** y **MariaDB 10.6 y 11.4** (D-035).
- Login con **Keycloak 26** en el navegador: PKCE, vuelta a la app, API con el token, roles anidados (`realm_access.roles`), cierre de sesión sin confirmación; el token no queda en `localStorage`.
- Avisos en navegador contra PostgreSQL 16 y un SMTP de prueba: configurar el aviso de `categoria` desde el editor, editar un registro y recibir el correo con el cambio y el enlace.
- Tabla de 1 millón de filas en PostgreSQL 16: última página 0,2 s; con 50 usuarios buscando, abrir un registro sigue en p95 13 ms (`docs/carga.md`).
- Carga (Node, 200 usuarios, 20 s, PostgreSQL 16): 2.255 solicitudes/s, 0 errores, listado p95 83 ms (`docs/carga.md`).
- Concurrencia en navegador contra PostgreSQL 16, con dos sesiones: el segundo en guardar ve el aviso con lo que cambió el otro y guarda igual solo sus campos; un borrado desde una lista vieja se rechaza.
- Auditoría en navegador contra PostgreSQL 16: editar un precio y verlo en "Historial" del registro; cambiar solo una etiqueta N:M; filtros y vista en teléfono.
- Seguridad en navegador contra PostgreSQL 16: alta de un usuario propio y de un perfil con regla de conexión, tabla y columna; ese usuario ve solo lo permitido (sin Nuevo ni Eliminar, precio bloqueado, "Ver" en tablas de consulta) y no entra a Seguridad.
- Tipos en navegador contra PostgreSQL 16: teléfonos de un cliente como lista y un ícono subido, guardado y descargado.
- Árbol en navegador contra PostgreSQL 16: jerarquía de empleados, alta de un hijo, ciclo rechazado en el campo y vuelta a la vista de árbol.
- Exportar e importar en navegador contra PostgreSQL 16: descarga a Excel desde la grilla; importación con un error por fila, asociación manual de columnas y confirmación.
- N:M en navegador contra PostgreSQL 16: cambiar las etiquetas de un producto y crear uno con etiquetas desde su formulario.
- Maestro-detalle en navegador contra PostgreSQL 16: pedido con su pestaña de líneas, alta de línea con el pedido fijo y vuelta al pedido; alta de categoría que queda en su edición con la pestaña de productos.
- Cambios de esquema en navegador contra PostgreSQL 16: columna borrada y nueva, tipo cambiado y tabla nueva; aviso con detalle, ajuste sin efecto, quitarlo desde el editor y aceptar.
- Editor de ajustes en navegador contra PostgreSQL 16 (escritorio y teléfono, sin desborde): cambiar nombre, grupo, orden por defecto, orden de columnas, ocultar, patrón y control; error en vivo; guardar, reabrir y restablecer.
- Recorrido en navegador con ajustes contra PostgreSQL 16: grupo y etiquetas en el menú, tabla oculta, orden de columnas, orden por defecto, columnas ocultas y solo de formulario, ayudas y error del patrón en el campo.
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
- `geography`/`geometry` y `hierarchyid` se ven pero no se editan (P-090).

## Notas del entorno de desarrollo remoto (sesiones de Claude Code en la nube)

- .NET 8 no viene instalado: `apt-get update && apt-get install -y dotnet-sdk-8.0`.
- Postgres 16 local disponible en `/usr/lib/postgresql/16/bin` (`initdb` + `pg_ctl` como usuario `postgres`).
- quay.io está bloqueado: Keycloak se baja como `mirror.gcr.io/keycloak/keycloak:26.0`.
- Docker requiere iniciar el daemon a mano (`dockerd &`); si no arranca por "process with PID … is still running", borrar `/var/run/docker.pid`. Docker Hub responde 429 (límite de descargas): usar el espejo `mirror.gcr.io/library/<imagen>` (postgres, mysql, mariadb) vía `MANTENEDORES_*_IMAGE` y `TESTCONTAINERS_RYUK_DISABLED=true`. Las imágenes de SQL Server (mcr.microsoft.com) bajan sin problema.
- Dentro de `docker build` no hay acceso a NuGet, por eso la imagen no se pudo compilar ahí.
- Borrar ramas remotas o hacer force-push está bloqueado por permisos de la sesión: lo hace el dueño desde GitHub.
