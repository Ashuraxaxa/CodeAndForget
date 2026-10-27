# Bitácora

Una entrada por sesión, la más reciente arriba. Breve: qué se hizo, qué se decidió y qué quedó abierto.

---

## 2026-10-27 · Fase 2: permisos por acción
- El dueño respondió Q-006 y P-083: configurar mantenedores y gestionar conexiones deben tener **cada uno su rol**, asignable a un perfil o usuario. El apartado de seguridad propio se conversará después.
- Permisos `ConfigureTables` y `ManageConnections`, otorgados a roles en `Auth:Permissions` (por defecto `mnt-configurador` y `mnt-conexiones`); los administradores tienen todos (D-053, reemplaza a D-050).
- `/api/auth/me` devuelve `permissions`; la UI oculta Configurar y Nueva/Editar/Eliminar conexión a quien no los tiene.
- 125 unitarias en verde.
- **Siguiente:** P-013, diferencias de esquema. **Pendiente de conversar:** el apartado de seguridad (P-040/P-042).

## 2026-10-27 · Fase 2: editor de ajustes
- PR #11 fusionado. Nuevo botón **⚙ Configurar** (solo admin) con pestañas Tabla y Columnas, vista previa en vivo y "Restablecer a lo automático".
- Endpoint `.../overrides/{schema}/{table}/preview`: aplica sin guardar y devuelve tabla y errores (D-052).
- Probado en navegador (escritorio y teléfono): guardar, reabrir, error en vivo y restablecer.
- **Siguiente:** P-013, diferencias de esquema. **Nuevo pendiente:** P-085 (FK virtuales compuestas y arrastrar columnas).

## 2026-10-27 · Fase 2: ajustes por tabla y columna
- `OverrideApplier` en Core aplica un JSON por tabla sobre el esquema leído; dos cachés (leído y combinado) para aplicar sin volver a leer la base (D-049).
- Migración `002_overrides`, `SqlOverrideStore` y `FileOverrideStore`; endpoints `/api/meta/{conn}/overrides`, que solo cambia un administrador (D-050, provisional hasta Q-006).
- Reglas en el servidor: obligatoria, patrón con tiempo máximo, opciones, rango y largo. Columnas ocultas fuera de lecturas, escrituras y búsqueda (D-051).
- UI: grupos, etiquetas, orden, ayudas, controles (`textarea`, `email`, `money`…) y aviso de ajustes que no aplican. Guía en `docs/ajustes.md`.
- 123 unitarias y 153 de integración en verde en los cuatro motores, versión mínima y actual. Probado en navegador.
- **Siguiente:** P-012, el editor en la UI. **Nuevos pendientes:** P-082 (ajustes con varias instancias), P-083 (conexiones solo admin), P-084.

## 2026-10-27 · Fase 2: metadata configurable
- Nuevo `Mantenedores.Metadata`: conexiones en SQLite (por defecto), Postgres o SQL Server, con un migrador propio en vez de DbUp (D-047).
- Llaves de Data Protection en la base (`KeyStore=Database`) y certificado opcional: dos instancias comparten conexiones (probado).
- Importación automática del `connections.json` de la Fase 1, que se vuelve a cifrar con las llaves nuevas (D-048).
- 106 unitarias y 139 de integración en verde.
- **Siguiente:** P-011, los overrides. **Nuevo pendiente:** P-081, el bloqueo de migraciones con varias instancias.

## 2026-10-27 · Fase 2: login configurable
- `Auth:Mode` = `Local` o `Jwt`, con Authority OIDC, MetadataAddress o SigningKey. Claims normalizados (nombre y roles, incluso anidados), `AdminRoles`, `/api/auth/config` y `/api/auth/me` (D-046). `Auth:JwtKey` solo se exige en `Local`.
- UI: login PKCE con `oidc-client-ts` cuando hay `ClientId`; modo proxy sin login; redirect bajo subruta.
- Probado en vivo con Keycloak 26 (`samples/keycloak/realm-demo.json`): login, roles, API y salida. Bug corregido: al salir se borraba el usuario antes de redirigir, y Keycloak pedía confirmación.
- 10 pruebas nuevas en `AuthTests` (emisor en memoria): audiencia, firma, vencimiento, scope, roles anidados y por defecto, configuración inválida.
- **Pendiente:** prueba en vivo con Duende (P-078). **Siguiente:** P-010, la metadata configurable.

## 2026-10-27 · Bloque M.3: MySQL y MariaDB
- Nuevo `Mantenedores.Providers.MySql` (MySqlConnector) para ambos motores, con sus particularidades en el proveedor (D-045).
- Core: UUID de la PK generado por la app si el motor no puede devolverlo (D-044); fechas con zona devueltas como UTC.
- `samples/mysql/01_demo.sql`, equivalente a las otras bases. `docker-compose` con `mysql-demo`.
- La batería común pasó **a la primera** en MySQL 8.0/8.4 y MariaDB 10.6/11.4: 137 pruebas de integración en verde en los cuatro motores.
- CI con MySQL y MariaDB en la matriz; UI con el motor nuevo. Probado en navegador.
- **Bloque M terminado.** Siguiente: Fase 2 (P-041 y P-010).

## 2026-10-27 · Bloque M.1: abstracciones del dialecto
- Estrategia de inserción en tres pasos (devuelta, último id, provista) y prefijo de parámetros en el dialecto (D-040).
- Alias cortos para etiquetas de FK; números de más de 28 dígitos como texto (D-041).
- Capacidades por columna: ordenable y filtrable (D-042).
- Escape de LIKE neutro `!`; versión mínima verificada al conectar, con datos del servidor (D-043).
- Bug encontrado por la batería: Npgsql no podía leer un enum creado después de su primera conexión. Ahora los enums se leen como texto.
- Base de ejemplo: `monto_total` de 38 dígitos en `pedido`.
- 70 unitarias y 65 de integración en verde (PG 12/16, SQL Server 2017/2022). Probado en navegador.
- **Siguiente:** M.3, el proveedor MySQL/MariaDB.

## 2026-10-27 · Cierre Fase 1: errores amigables y despliegue
- Errores de restricciones traducidos por código del motor y nombre de la restricción, sin depender del idioma del servidor (D-038). Probado igual en PG 12/16 y SQL Server 2017/2022.
- `UseWindowsService` y `UseSystemd`. Bug corregido: la carpeta de datos relativa se resolvía contra el directorio de trabajo (D-039).
- Nueva `docs/despliegue.md`: Docker, Compose, Kubernetes, nginx, IIS, servicio de Windows, systemd, respaldo y `GRANT` mínimos.
- **Fase 1 cerrada.** Siguiente: bloque M (P-020 a P-029), empezando por las abstracciones del dialecto.

## 2026-10-27 · Cierre Fase 1: subruta, proxy y health checks
- `Hosting:PathBase` y `Hosting:ForwardedHeaders`. El `index.html` se sirve con `<base href>` ajustada y el frontend usa rutas relativas (D-037).
- Bug detectado por las pruebas: sin `UseRouting()` después de `UsePathBase`, todo caía en el `index.html`, incluso la API sin token.
- Health checks `/api/health/live` y `/ready`. Nuevo `docs/configuracion.md` con todas las claves.
- Probado en navegador bajo `/mantenedores` contra PostgreSQL 12.
- **Siguiente:** P-003 (errores amigables) y P-004 (guía de despliegue).

## 2026-10-27 · Cierre Fase 1: pruebas de integración y versiones mínimas
- El dueño confirmó las versiones mínimas (Q-008 → D-035) y pidió publicarlas en el README ("Requisitos de instalación").
- Nuevo proyecto `Mantenedores.IntegrationTests`: batería común (`ProviderContractTests`) que hereda cada motor. Son 57 pruebas, en verde contra PG 12/16 y SQL Server 2017/2022.
- Problemas que encontró la batería y se corrigieron:
  - La base de ejemplo de PG no funcionaba en PG 12 (faltaba `pgcrypto`).
  - Los motores devolvían fechas y horas en formatos distintos (D-036).
  - Se agregó un trigger a la base de PG para igualarla a la de SQL Server.
- CI: unitarias separadas, matriz de integración (versión mínima y actual), job que compila y arranca la imagen Docker.
- **Siguiente:** P-007 y P-008 (subruta, proxy y health checks), luego P-003 y P-004.

## 2026-10-27 · Oracle fuera de alcance
- El dueño decidió dejar Oracle fuera y mantener MySQL/MariaDB (D-034 reemplaza a D-029).
- P-026 queda ⛔. Se quitó `EmptyStringIsNull` de P-024. El prefijo de parámetros en el dialecto se mantiene, porque es barato y deja la puerta abierta.
- El análisis de Oracle se conservó al final de `docs/motores.md`.
- Bloque M: 2,5 semanas. Total: 17–18 semanas.

## 2026-10-27 · MySQL/MariaDB y Oracle en el plan
- El dueño pidió incluir MySQL y Oracle desde ya para no generar deuda técnica.
- Se registraron D-029 a D-033: cuatro familias de motores con versiones mínimas, drivers, el dialecto absorbe las diferencias, qué es un "esquema" en cada motor y la batería común de pruebas.
- Nuevo bloque **M** (4 semanas) antes de la Fase 2, con P-020 a P-029. Total: 19–20 semanas.
- Se creó `docs/motores.md` con las diferencias técnicas y la guía para agregar un motor.
- Se detectaron 4 supuestos del código actual que fallan en otros motores (prefijo `@`, alias largos, escape `\`, clave como filas) y uno que ya puede fallar en PG (`numeric` grande, P-023).
- **Abierto:** Q-008 (versiones de cada motor en el equipo).

## 2026-10-27 · Decisiones de entorno
- El dueño respondió Q-001 a Q-003:
  - Despliegue en cualquier entorno (servidor propio o nube).
  - Uso por un solo equipo interno.
  - Autenticación libre con cualquier JWT, como Duende IdentityServer.
- Se registraron D-022 a D-028. Principio rector: **todo por configuración y sin dependencias externas por defecto**. La metadata puede estar en SQLite, Postgres o SQL Server; Redis y los vaults son opcionales.
- La autenticación configurable (P-041) se adelantó a la Fase 2. Se agregaron P-007, P-008, P-038, P-039, P-063 y P-064.
- Se ajustaron `docs/siguientes-pasos.md` y `docs/plan.md`. Fase 2: 5–6 semanas; total: 15–16 semanas.

## 2026-10-27 · Memoria del proyecto y hoja de ruta
- Se creó `CLAUDE.md` y la carpeta `docs/memoria/` (estado, decisiones, pendientes, preguntas, convenciones, bitácora) (D-021).
- Se escribió `docs/siguientes-pasos.md` con el cierre de la Fase 1 y el detalle de las Fases 2 a 5.
- Se registraron 21 decisiones tomadas en la Fase 1 y 7 preguntas abiertas para el dueño.
- **Abierto:** responder Q-001 a Q-003 antes de P-010; fusionar el PR de documentación (P-006).

## 2026-10-27 · Fase 1: estructura base
- Análisis de viabilidad, escalabilidad y plan por fases (`docs/plan.md`).
- Se construyó el backend .NET 8 (Core, proveedores SQL Server y Postgres, API), el frontend React, las bases de ejemplo, Docker y la CI.
- Probado contra PostgreSQL 16 y SQL Server 2022 reales, y en navegador con Playwright.
- Bug encontrado por las pruebas y corregido: la conversión de enteros siempre devolvía `long`.
- PR #1 fusionado en `main` con la CI en verde.
- El repo no tenía `main`: se creó con un commit inicial vacío y se cambió la rama por defecto a `main`.
- **Abierto:** validar `docker build` (P-001) y pruebas de integración (P-002).
