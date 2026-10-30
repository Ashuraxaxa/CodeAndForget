# Configuración

Toda la configuración se lee de `appsettings.json`, de `appsettings.{Entorno}.json` y de variables
de entorno. En una variable de entorno, cada `:` se escribe como `__` (doble guion bajo):
`Hosting:PathBase` → `Hosting__PathBase`.

**Principio (D-022):** sin configurar nada, la app arranca en la raíz, sin proxy, con datos en `data/`
y sin servicios externos. En modo `Local` (el de por defecto) solo es obligatoria `Auth:JwtKey`.

> Regla del proyecto: toda clave nueva se documenta aquí, en el mismo PR que la agrega.

## Publicación (`Hosting`)

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Hosting:PathBase` | `""` (raíz) | Subruta donde se publica la app, p. ej. `/mantenedores`. El mismo build del frontend funciona en cualquier subruta: la API ajusta el `<base href>` del `index.html` (D-037). |
| `Hosting:ForwardedHeaders:Enabled` | `false` | Lee `X-Forwarded-For`, `X-Forwarded-Proto` y `X-Forwarded-Host`. Activar cuando un proxy termina HTTPS (nginx, IIS, Traefik, balanceador). |
| `Hosting:ForwardedHeaders:KnownProxies` | `[]` (solo localhost) | IP de los proxies de confianza, p. ej. `["10.0.0.5"]`. |
| `Hosting:ForwardedHeaders:KnownNetworks` | `[]` | Redes de confianza en CIDR, p. ej. `["10.0.0.0/8"]` (Docker, Kubernetes). |
| `Hosting:ForwardedHeaders:TrustAllProxies` | `false` | Confía en cualquier origen. **Solo** si la app no es accesible sin pasar por el proxy. |
| `ASPNETCORE_URLS` | `http://+:8080` en la imagen | Dirección y puerto donde escucha (variable estándar de ASP.NET Core). |

Ejemplo detrás de nginx en `https://intranet.local/mantenedores`, con nginx en la red de Docker:

```bash
Hosting__PathBase=/mantenedores
Hosting__ForwardedHeaders__Enabled=true
Hosting__ForwardedHeaders__KnownNetworks__0=172.16.0.0/12
```

El proxy debe reenviar la ruta **completa**, incluida la subruta (en nginx, `proxy_pass http://app:8080;` sin barra final).

## Health checks

No se configuran; siempre están disponibles y no requieren autenticación (P-008).

| Ruta | Responde 200 cuando | Uso típico |
|---|---|---|
| `/api/health/live` | El proceso está vivo | *Liveness probe* (Kubernetes), reinicio automático |
| `/api/health/ready` | Además puede leer su almacén de conexiones | *Readiness probe*, balanceador |
| `/api/health` | Igual que `live` | Compatibilidad |

Bajo una subruta van precedidos por ella: `/mantenedores/api/health/ready`.

La imagen de Docker no trae `curl`: su `HEALTHCHECK` es `dotnet Mantenedores.Api.dll healthcheck`, que consulta
`/api/health/ready` de la propia instancia (según `ASPNETCORE_URLS` y `Hosting__PathBase`) y sale con 0 o 1 (P-074).
Acepta otra URL como argumento.

## Cambios de esquema (`Schema`)

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Schema:RefreshMinutes` | `60` | Cada cuánto se vuelve a leer el catálogo de las conexiones abiertas para detectar cambios sin pulsar ↻ (P-086). Si nada cambió no toca nada; si cambió, aparece el aviso de cambios. `0` = nunca. |

Además, si una consulta falla porque una tabla o columna ya no existe, el esquema se descarta al momento (en todas las
instancias) y el usuario ve "La estructura de … cambió; recargue la página".

## Datos (`Storage`)

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Storage:DataDirectory` | `data` (en la imagen: `/app/data`) | Carpeta de la metadata en SQLite (`mantenedores.db`) y de las llaves de cifrado (`keys/`), cuando se usan los valores por defecto. Una ruta relativa se resuelve contra la carpeta de la app. **Debe persistir y respaldarse.** |

## Metadata del servicio (`Metadata`)

Dónde guarda la app su propia configuración: las conexiones y los ajustes de cada mantenedor ([ajustes.md](ajustes.md)) (D-023).
Las tablas llevan el prefijo `mnt_` y se crean solas al arrancar (migraciones en `backend/src/Mantenedores.Metadata/Migrations/`).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Metadata:Provider` | `Sqlite` | `Sqlite` (un archivo, nada que instalar) · `Postgres` · `SqlServer` · `File` (archivos JSON `connections.json`, `overrides.json` y `snapshots/`, solo para desarrollo). |
| `Metadata:ConnectionString` | `Data Source=<datos>/mantenedores.db` en `Sqlite` | Obligatoria en `Postgres` y `SqlServer`. El usuario necesita crear tablas la primera vez (o se crean a mano con los scripts). |
| `Metadata:SyncSeconds` | `5` | Cada cuántos segundos una instancia revisa si otra cambió ajustes, el esquema de una conexión, perfiles o usuarios (D-077). Es una consulta liviana a `mnt_change_stamp`. `0` = no revisar. Sin efecto con `File`. |

**Varias instancias** (Kubernetes con más de una réplica, granja de IIS): usar `Postgres` o `SqlServer` **y** `DataProtection:KeyStore=Database`,
para que todas compartan conexiones y llaves sin un volumen común. Cada instancia guarda el esquema y los ajustes en
memoria; cuando una cambia algo (un ajuste, ↻ en una conexión, un perfil o un usuario), las demás lo ven en
`Metadata:SyncSeconds` segundos, sin Redis (D-077).

Si al arrancar existe `data/connections.json` (Fase 1) y la base está vacía, sus conexiones se importan una sola vez
y el archivo se renombra a `connections.json.importado`.

Con varias instancias arrancando a la vez, solo una aplica las migraciones: Postgres usa `pg_advisory_lock` y SQL Server
`sp_getapplock`; las demás esperan y encuentran todo aplicado (P-081).

## Secretos en las cadenas de conexión (`Secrets`)

Una cadena de conexión puede referirse a secretos del entorno en vez de guardar la contraseña (P-045). En la metadata
queda la referencia; el valor se lee recién al conectar y nunca se muestra en un error.

```
Host=db;Database=ventas;Username=app;Password=${env:MNT_VENTAS_PASSWORD}
Server=sql;Database=rrhh;User Id=app;Password=${file:rrhh-password}
```

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Secrets:EnvPrefix` | `MNT_` | Solo se leen variables de entorno con este prefijo, para que nadie pueda pedir, p. ej., la llave de la app. Vacío = cualquiera (no recomendado). |
| `Secrets:FilesDirectory` | `/run/secrets` | Carpeta de los archivos de secretos (la de Docker y Kubernetes). `${file:x}` lee `<carpeta>/x`, nunca fuera de ella. Vacío = desactivado. El salto de línea final se quita. |

## Llaves de cifrado (`DataProtection`)

Las cadenas de conexión se guardan cifradas. Estas llaves son las que permiten leerlas: **si se pierden, hay que volver a ingresar las conexiones** (D-025).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `DataProtection:KeyStore` | `FileSystem` | `FileSystem`: carpeta `<datos>/keys`. `Database`: tabla `mnt_dataprotection_key` de la metadata (requiere `Metadata:Provider` distinto de `File`). |
| `DataProtection:CertificatePath` | — | Certificado `.pfx` para cifrar las llaves en reposo. Opcional. |
| `DataProtection:CertificatePassword` | — | Contraseña del certificado. |

## Avisos por correo (`Notifications`)

Para tablas críticas: avisar por correo cuando alguien crea, edita o borra un registro (D-073). **Apagado por defecto.**
Se activa aquí con un servidor SMTP, y cada tabla elige a quién avisar en su ajuste `notify` (*⚙ Configurar → Tabla →
Avisos por correo*).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Notifications:Enabled` | `false` | Enviar los avisos que configuren las tablas. |
| `Notifications:From` | — | Remitente, p. ej. `Mantenedores <no-responder@empresa.cl>`. |
| `Notifications:Smtp:Host` | — | Servidor SMTP (Exchange, Microsoft 365, Gmail, un relay interno…). |
| `Notifications:Smtp:Port` | `587` | Puerto. |
| `Notifications:Smtp:EnableSsl` | `true` | STARTTLS/SSL. Apagarlo solo con un relay interno de confianza. |
| `Notifications:Smtp:User` / `Password` | — | Credenciales, si el servidor las pide. La clave, por variable de entorno (`Notifications__Smtp__Password`) o secreto del orquestador. |
| `Notifications:Smtp:TimeoutSeconds` | `30` | Tiempo máximo por envío. |
| `Notifications:SubjectPrefix` | `[Mantenedores]` | Inicio del asunto. |
| `Notifications:AppUrl` | — | Dirección pública de la app: si se da, el correo trae un enlace al registro. |
| `Notifications:MaxRowsPerMessage` | `20` | En una importación, cuántas filas se detallan; del resto se dice cuántas son. |
| `Notifications:Language` | `es` | Idioma del correo: `es`, `en` o `ru` (D-102). Es uno para todos, porque lo leen otras personas y no quien hizo el cambio. |

El correo dice quién, cuándo, en qué tabla y registro, y (si la tabla lo pide) los valores antes y después, con las
mismas reglas de la auditoría: sin columnas ocultas ni el contenido de archivos. Se envía en segundo plano después de
guardar, con 3 reintentos; si no se puede, queda en el log, y el cambio igual queda en la auditoría.

## Conexiones a las bases (`Database:Pool`)

Cada conexión configurada tiene su propio pool. Estos valores se agregan a la cadena de conexión **solo si esta no los
trae**: si la cadena dice `Max Pool Size=50`, manda la cadena (D-070).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Database:Pool:MaxSize` | `20` | Máximo de conexiones abiertas a la vez hacia una misma base. `0` = el valor del driver (100). |
| `Database:Pool:IdleSeconds` | `300` | Segundos sin uso antes de cerrar una conexión. `0` = el valor del driver. En SQL Server no se aplica: SqlClient las cierra solo, a los 4 a 8 minutos. |

Con varias instancias, el máximo hacia una base es `MaxSize × instancias`.

## Observabilidad (`Telemetry`)

Trazas y métricas con OpenTelemetry, **apagadas mientras no se configure un destino** (D-071). Con `OtlpEndpoint` se
envían por OTLP a lo que use el entorno: OpenTelemetry Collector, Jaeger, Grafana (Tempo, Mimir), Azure Monitor,
Datadog, etc.

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Telemetry:OtlpEndpoint` | — (apagado) | Destino OTLP, p. ej. `http://otel-collector:4317`. |
| `Telemetry:OtlpProtocol` | `Grpc` | `Grpc` (4317) o `HttpProtobuf` (4318; se agrega `/v1/traces` y `/v1/metrics`). |
| `Telemetry:OtlpHeaders` | — | Encabezados para el destino, p. ej. `api-key=…`. |
| `Telemetry:ServiceName` | `mantenedores` | Nombre del servicio en las trazas y métricas. |
| `Telemetry:SlowRequestMilliseconds` | `2000` | Las solicitudes a la API más lentas que esto se anotan en el log como advertencia, con método, ruta (sin la query), estado, usuario y duración. Funciona sin OpenTelemetry. `0` = no se anotan. |

Qué se envía:
- **Trazas:** cada solicitud HTTP (sin los health checks) y, dentro, las consultas de PostgreSQL y MySQL/MariaDB, que publican sus propios drivers.
- **Métricas:** duración y cantidad de solicitudes, rechazos por límites de uso, runtime de .NET (memoria, GC, hilos) y uso del pool de PostgreSQL y MySQL/MariaDB.

Las consultas de SQL Server no aparecen como trazas propias: SqlClient no publica en el formato estándar sin un paquete
aparte. Su tiempo queda dentro de la traza de la solicitud.

**Logs en JSON** (para Loki, Elastic, CloudWatch…), sin nada extra: `Logging__Console__FormatterName=json`.

**Pruebas de carga:** ver [`carga.md`](carga.md).

## Límites de uso (`RateLimit`)

Protegen a las bases de un uso excesivo y el login de intentos repetidos (P-051, P-091). Al pasarse, la API responde
`429` con `Retry-After`. Los contadores están en la memoria de cada instancia; con `Cache:Provider=Redis` se
comparten, y el tope es el mismo con una instancia o con varias (P-050).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `RateLimit:Enabled` | `true` | Aplicar los límites. |
| `RateLimit:PermitsPerMinute` | `600` | Solicitudes a la API por minuto y por usuario (por IP sin sesión). No cuentan los health checks ni los archivos del frontend. `0` = sin límite. |
| `RateLimit:HeavyPerMinute` | `10` | Exportaciones e importaciones (incluida la vista previa) por minuto y por usuario. |
| `RateLimit:LoginPerMinute` | `10` | Intentos de inicio de sesión por minuto y por IP. |

Detrás de un proxy, la IP es la del cliente solo si el proxy está configurado en `Hosting` (P-007).

## Servidor MCP (`Mcp`)

Para que una IA (Claude, Cursor, etc.) revise el esquema y proponga ajustes (P-114, D-101). Detalle y cómo conectarla:
[mcp.md](mcp.md).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Mcp:Enabled` | `false` | Publica `/api/mcp` (HTTP sin estado). Entra quien tenga `ConfigureTables`, con un token de acceso o un JWT. Solo lee y deja propuestas; nunca guarda ajustes. |

## Caché compartida (`Cache`)

Solo hace falta con varias instancias (P-050, D-026, D-091). Por defecto cada instancia guarda lo suyo en memoria y
no se necesita nada más. Con Redis (probado con Redis 6.2 y Valkey 8):
- **El esquema leído del catálogo** se comparte: la instancia que lo lee lo deja en Redis y las demás lo toman de ahí
  sin volver a la base. ↻ lo borra, y la relectura periódica (`Schema:RefreshMinutes`) lo reemplaza si cambió.
- **Los contadores de los límites de uso** (`RateLimit`) suman entre todas las instancias.

Los avisos entre instancias (ajustes, ↻, perfiles) siguen yendo por la base de metadata (D-077), con o sin Redis.

Si Redis no responde, nada se corta: cada instancia lee el catálogo y cuenta los límites en memoria, como sin Redis,
y lo anota en el log (una vez por minuto como mucho). Cuando Redis vuelve, se usa otra vez solo.

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Cache:Provider` | `Memory` | `Memory` o `Redis`. Otro valor detiene el arranque. |
| `Cache:Redis` | — | Configuración de StackExchange.Redis: `redis:6379`, `redis:6379,password=…,ssl=true`. Acepta `${env:…}` y `${file:…}` (ver `Secrets`). Obligatoria con `Redis`. |
| `Cache:KeyPrefix` | `mnt:` | Prefijo de las claves, para compartir un Redis con otras apps o con otra copia del servicio. |
| `Cache:SchemaMinutes` | `60` | Cuánto vive el esquema en Redis. |
| `Cache:TimeoutMs` | `1000` | Tope de cada operación en Redis; si tarda más, se sigue sin la caché. |

En Redis no queda ninguna contraseña: la clave del esquema lleva un hash del servidor, la base y los esquemas de la
conexión, sin la contraseña. Lo que sí queda es la estructura de las bases (tablas y columnas): un Redis con clave
y solo accesible desde la red de la app.

```bash
Cache__Provider=Redis
Cache__Redis='redis:6379,password=${env:MNT_REDIS_PASSWORD}'   # o ${file:redis-password}
```

## Motores como plugins (`Providers`)

Un motor que la app no trae se instala copiando su plugin a una carpeta, sin recompilar (P-064, D-092). Apagado por
defecto. Cómo se escribe un plugin: [motores.md](motores.md#motores-como-plugins).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Providers:PluginsDirectory` | — (sin plugins) | Carpeta con un plugin por subcarpeta: `<carpeta>/<Nombre>/<Nombre>.dll` y sus dependencias. Ruta relativa = desde la carpeta de la app. |

- Se cargan al arrancar; para agregar, quitar o actualizar uno hay que reiniciar.
- Un plugin que no carga (falta su `.dll`, está compilado para otra versión, repite un motor que ya existe)
  **detiene el arranque** con el motivo. Así no queda un motor a medias con conexiones que fallan.
- El log dice qué plugins se cargaron y qué motores trae cada uno; `GET /api/engines` los lista y la interfaz los
  muestra al crear una conexión.
- **Seguridad:** un plugin es código que corre con los permisos de la app. La carpeta solo debe poder escribirla
  quien administra el servidor (en un contenedor, móntela de solo lectura).

## Consultas (`Crud`)

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Crud:CommandTimeoutSeconds` | `30` | Tiempo máximo por consulta a la base administrada. |
| `Crud:ExportMaxRows` | `100000` | Máximo de filas por exportación (CSV, Excel, JSON o XML). Si el filtro trae más, se pide filtrar. |
| `Crud:FileMaxMegabytes` | `10` | Tamaño máximo de un archivo guardado en una columna binaria. Viaja en base64 dentro del JSON (≈ 1,35 veces su tamaño): por encima de ~20 MB hay que subir también el límite de la solicitud (`Kestrel__Limits__MaxRequestBodySize`) y el del proxy. |
| `Crud:ImportMaxRows` | `10000` | Máximo de filas por archivo importado. Al importar se elige: **solo agregar**, o **agregar y actualizar por clave** (las filas con una clave que ya existe se actualizan; requiere permiso de editar, P-088). |
| `Crud:ImportMaxMegabytes` | `20` | Tamaño máximo del archivo a importar. Si se sube, revisar también el límite del proxy (p. ej. `client_max_body_size` en nginx, `maxAllowedContentLength` en IIS). |
| `Crud:EstimateCountAbove` | `500000` | Desde cuántas filas (según las estadísticas del motor) una tabla es **grande**. Sin filtros, su total es la estimación ("≈ 1.000.000 registros") en vez de contar (D-072). `0` = contar siempre. |
| `Crud:FilteredCountCap` | `1000` | En tablas grandes con búsqueda o filtros se cuenta hasta este número ("más de 1.000 registros"). `0` = contar todo. |
| `Crud:MaxConcurrentHeavyQueries` | `4` | En tablas grandes, cuántas búsquedas, filtros u órdenes por otra columna corren a la vez por conexión. Las demás esperan sin ocupar el pool, así abrir o paginar no se traba. `0` = sin tope. |
| `Crud:HeavyQueueSeconds` | `30` | Cuánto espera su turno una de esas consultas antes de responder `503` ("intente de nuevo"). |

**Búsquedas en tablas grandes.** La búsqueda de texto es "contiene" (`LIKE '%texto%'`): sin un índice adecuado,
recorre la tabla. Con tablas de millones de filas donde se busca seguido, conviene que el DBA cree un índice que acelere
ese tipo de búsqueda; por ejemplo, en PostgreSQL, `CREATE EXTENSION pg_trgm` y un índice GIN `gin_trgm_ops` sobre las
columnas de texto. La app lo aprovecha sin cambios.

## Auditoría (`Audit`)

Registra cada alta, edición, baja, fila importada y cambio de vínculos N:M: quién, cuándo, tabla, clave y valores antes
y después (en una edición, solo lo que cambió). Las lecturas no se registran (D-065). Se guarda en la metadata del
servicio (tabla `mnt_audit`) y se consulta en **Auditoría** o con **Historial** desde un registro, con el permiso
`ViewAudit`.

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Audit:Enabled` | `true` | Registrar los cambios. |
| `Audit:RetentionDays` | `365` | Días que se guardan. `0` = para siempre. |
| `Audit:PurgeIntervalHours` | `24` | Cada cuántas horas se borran las entradas vencidas (también al arrancar). |
| `Audit:ExportMaxRows` | `100000` | Tope de filas al exportar la auditoría a CSV o Excel con **⤓ Exportar** (D-081). |

Con un perfil (y el permiso `ViewAudit` dado por ese perfil), cada usuario ve solo las tablas y columnas que su perfil le
deja ver; los administradores ven todo. Las FK y los vínculos N:M se muestran con su texto, p. ej. "Bebidas (1)".

El registro se escribe después de confirmar el cambio en la base de datos. Si la metadata no responde en ese momento,
el cambio queda hecho y el fallo se anota en el log (D-068).

## Autenticación (`Auth`)

Dos modos (D-024). El mismo build sirve para cualquiera: solo cambia la configuración.

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Auth:Mode` | `Local` | `Local`: usuarios en la configuración. `Jwt`: tokens de un emisor externo. |
| `Auth:AdminRoles` | `["admin"]` | Roles de administrador: tienen **todos** los permisos. Se comparan sin distinguir mayúsculas. |
| `Auth:Permissions:ConfigureTables` | `["mnt-configurador"]` | Roles que pueden **configurar mantenedores** (ajustes por tabla y columna, botón ⚙ Configurar). |
| `Auth:Permissions:ManageConnections` | `["mnt-conexiones"]` | Roles que pueden **crear, editar, probar y borrar conexiones**. Ver y abrir conexiones no exige permiso. |
| `Auth:Permissions:ManageSecurity` | `["mnt-seguridad"]` | Roles que pueden **administrar la seguridad**: perfiles, usuarios propios y permisos (D-066). |
| `Auth:Permissions:ViewAudit` | `["mnt-auditor"]` | Roles que pueden **consultar la auditoría** de cambios (D-068). |

**Permisos (D-053).** Cada acción sensible tiene su propio permiso, y cada permiso se otorga a uno o más roles.
Así se le puede dar a una persona solo lo que necesita, asignándole el rol en el proveedor de identidad (modo `Jwt`)
o en `Auth:Users:N:Roles` (modo `Local`). Por ejemplo, para que el rol `analista-datos` configure mantenedores:

```json
"Auth": { "Permissions": { "ConfigureTables": [ "analista-datos" ] } }
```

o, por variable de entorno, `Auth__Permissions__ConfigureTables__0=analista-datos`. Un nombre de permiso que no existe
detiene el arranque con un mensaje. Los perfiles del apartado **Seguridad** también otorgan estos permisos, además de
los permisos sobre los datos por conexión, tabla y columna: ver [`seguridad.md`](seguridad.md). Mientras no exista
ningún perfil, la seguridad de datos queda abierta (todos ven y editan todo).

### Modo `Local`

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Auth:JwtKey` | — (**obligatoria en este modo**) | Clave para firmar los tokens, mínimo 32 caracteres. |
| `Auth:Issuer` | `mantenedores` | Emisor y audiencia de los tokens. |
| `Auth:TokenMinutes` | `480` | Duración del token. |
| `Auth:Users:N:Username` | — | Usuario N. |
| `Auth:Users:N:PasswordHash` | — | Hash PBKDF2 de su contraseña (ver abajo). |
| `Auth:Users:N:Roles:M` | — | Roles del usuario, p. ej. `admin`. |

Además de estos, se pueden crear **usuarios propios** desde **Seguridad → Usuarios** (guardados en la metadata,
D-062). Si un nombre existe en los dos lados, manda la configuración.

Generar un hash:

```bash
# con la app publicada
dotnet Mantenedores.Api.dll hash-password "clave"
# desde el código fuente
dotnet run --project backend/src/Mantenedores.Api -- hash-password "clave"
```

> Los valores de `appsettings.Development.json` y de `docker-compose.yml` (`admin` / `admin`) son **solo de demo**.

### Modo `Jwt` (Duende IdentityServer, Keycloak, Entra ID, Auth0, emisor propio…)

Se configura **una** fuente de llaves: `Authority` (lo normal), `MetadataAddress` o `SigningKey`. En este modo no se usa `Auth:JwtKey`.

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Auth:Jwt:Authority` | — | URL del emisor con descubrimiento OIDC. Las llaves y el emisor salen de `/.well-known/openid-configuration`. |
| `Auth:Jwt:MetadataAddress` | — | URL del documento de descubrimiento, si no está en la ruta estándar. |
| `Auth:Jwt:SigningKey` | — | Llave simétrica HS256 (mín. 32 caracteres) para emisores propios sin OIDC. |
| `Auth:Jwt:Audience` | — (no se valida) | Audiencia esperada (`aud`). **Recomendada.** |
| `Auth:Jwt:Issuer` | — | Emisor esperado (`iss`). Con `Authority` se toma del descubrimiento. |
| `Auth:Jwt:RequireHttpsMetadata` | `true` | Exigir HTTPS para el descubrimiento. Solo `false` en desarrollo. |
| `Auth:Jwt:NameClaim` | primero de `preferred_username`, `name`, `email`, `sub` | Claim con el nombre del usuario. |
| `Auth:Jwt:RoleClaim` | `role`, `roles`, `groups` | Claim con los roles. Admite rutas con punto (`realm_access.roles`) y varias separadas por coma. |
| `Auth:Jwt:RequiredScope` | — | Scope que debe traer el token (`scope` o `scp`). |
| `Auth:Jwt:ClockSkewSeconds` | `60` | Tolerancia de reloj al validar vencimientos. |
| `Auth:Jwt:ClientId` | — | Client id **público** para que la UI inicie sesión (Authorization Code + PKCE). Vacío = un gateway/proxy entrega el token. |
| `Auth:Jwt:Scopes` | `openid profile` | Scopes que pide la UI. |
| `Auth:Jwt:ProviderName` | *(vacío)* | Texto del botón "Ingresar con …". Vacío: "el proveedor de identidad", en el idioma elegido. |

La UI renueva el token antes de que venza: con refresh token si el proveedor lo entrega y, si no, en un iframe oculto
que vuelve a `silent-renew.html` con la sesión del proveedor (D-087). Registre esa URL (p. ej.
`https://app.empresa.cl/silent-renew.html`) entre las URI de redirección del cliente.

En el proveedor, el cliente de la UI debe ser **público**, con PKCE y con estos valores:
- *Redirect URI:* `https://<app>/<subruta>/auth/callback`.
- *Post logout redirect URI:* `https://<app>/<subruta>/`.
- *Web origin (CORS):* la URL de la app.

**Keycloak** (probado; realm de ejemplo en `samples/keycloak/realm-demo.json`):

```bash
Auth__Mode=Jwt
Auth__Jwt__Authority=https://sso.empresa.com/realms/ventas
Auth__Jwt__Audience=mantenedores-spa          # agregar un "audience mapper" al cliente
Auth__Jwt__RoleClaim=realm_access.roles       # o resource_access.mantenedores-spa.roles
Auth__Jwt__ClientId=mantenedores-spa
Auth__Jwt__ProviderName=Keycloak
```

**Duende IdentityServer** (probado con Duende 7 y 8 y la app con tokens de 70 s; host de ejemplo en `samples/duende/`):

```bash
Auth__Mode=Jwt
Auth__Jwt__Authority=https://identity.empresa.com
Auth__Jwt__Audience=mantenedores-api          # nombre del ApiResource
Auth__Jwt__RequiredScope=mantenedores         # ApiScope que la app debe pedir
Auth__Jwt__RoleClaim=role
Auth__Jwt__ClientId=mantenedores-spa          # Client con RequirePkce = true, RequireClientSecret = false
Auth__Jwt__Scopes="openid profile mantenedores"
Auth__Jwt__ProviderName="Identidad corporativa"
```

Duende entrega tokens `at+jwt` y el `scope` como lista; la API acepta ambas cosas. Sin `AllowOfflineAccess` no hay refresh
token y la sesión se renueva con `silent-renew.html` (regístrela en `RedirectUris`). Si el IdentityServer se prueba
por `http`, ponga `Authentication.CookieSameSiteMode = Lax`: con `None` el navegador descarta su cookie y el login
vuelve a pedir la clave.

**Microsoft Entra ID:**

```bash
Auth__Mode=Jwt
Auth__Jwt__Authority=https://login.microsoftonline.com/<tenant-id>/v2.0
Auth__Jwt__Audience=<client-id-de-la-api>     # en tokens v2 el aud es el client id (GUID)
Auth__Jwt__RoleClaim=roles                    # App roles asignados al usuario
Auth__Jwt__ClientId=<client-id-de-la-spa>
Auth__Jwt__Scopes="openid profile api://<client-id-de-la-api>/acceso"
```

**Emisor propio con llave compartida:** `Auth__Jwt__SigningKey`, `Auth__Jwt__Issuer` y `Auth__Jwt__Audience`, sin `ClientId`. El token lo pone quien llame a la API.

**Detrás de un proxy que autentica** (p. ej. oauth2-proxy): `Auth__Mode=Jwt` con la fuente de llaves del emisor y **sin** `ClientId`. El proxy agrega `Authorization: Bearer …` y la UI no muestra login ni botón de salir.

`GET /api/auth/config` (anónimo) devuelve a la UI el modo, el `authority`, el `clientId`, los scopes y el nombre del proveedor; **nunca** devuelve llaves. `GET /api/auth/me` devuelve el nombre, los roles, si es administrador y sus permisos (`permissions`).

## Tema de la interfaz

No tiene claves en el servidor (D-093). Cada usuario elige **Auto** (el del sistema), **Día** o **Noche** en la barra
superior o en el login, y se recuerda en su navegador. En Auto, la app cambia sola si el sistema cambia.

## Idioma de la interfaz

No tiene claves en el servidor (D-076). Cada usuario elige español, inglés o ruso en la barra superior o en el
login; se recuerda en su navegador. Sin elección, se usa el idioma del navegador (el primero de sus preferidos que la app tenga) y, si no es ninguno de los tres, inglés.
Las fechas y los números siguen el formato del idioma elegido: 28/10/2026 (es), 10/28/2026 (en), 28.10.2026 (ru).

Los mensajes del servidor (validaciones, errores de la base, límites de uso) también se ven en el idioma elegido
(D-080, D-102). El servidor los arma en español y la interfaz los traduce con las plantillas de
`frontend/src/i18n/server.ts`. `npm run lint` lee los mensajes del backend y falla si alguno no tiene traducción
(`scripts/check-server-i18n.mjs`).

El servidor traduce lo que no pasa por la pantalla:
- la exportación de la auditoría (encabezados, operaciones y nombre del archivo), según el idioma de la interfaz;
- el correo de avisos, según `Notifications:Language`.

Quedan en español los registros del servidor (los lee quien opera el servicio) y los textos del servidor MCP para la
IA, que los entiende y le responde a cada persona en su idioma.

Para agregar un idioma: crear `frontend/src/i18n/<código>.ts` con las mismas claves que `en.ts`, sumarlo a
`LOCALES`, `REGION` y `DATE_REGION` en `frontend/src/i18n/index.ts`, una columna a las plantillas de `server.ts`, y al
chequeo de `scripts/check-i18n.mjs`. En el servidor, sumarlo a `ServerText` (auditoría y correo).

## Pruebas de integración

| Variable | Por defecto | Qué hace |
|---|---|---|
| `MANTENEDORES_PG_IMAGE` | `postgres:12` (versión mínima) | Imagen de PostgreSQL para la batería común. |
| `MANTENEDORES_MSSQL_IMAGE` | `mcr.microsoft.com/mssql/server:2017-latest` | Imagen de SQL Server (2016 no tiene imagen Linux). |
| `MANTENEDORES_MYSQL_IMAGE` | `mysql:8.0` (versión mínima) | Imagen de MySQL. |
| `MANTENEDORES_MARIADB_IMAGE` | `mariadb:10.6` (versión mínima) | Imagen de MariaDB. |
| `MANTENEDORES_POSTGIS_IMAGE` | `postgis/postgis:12-3.4` | PostgreSQL con PostGIS, solo para las pruebas espaciales (P-104). |
| `MANTENEDORES_REDIS_IMAGE` | `redis:6.2` | Redis para las pruebas de la caché compartida (P-050). Sirve también `valkey/valkey:8`. |
| `TESTCONTAINERS_RYUK_DISABLED` | `false` | Ponerla en `true` si el entorno no puede descargar la imagen auxiliar de Testcontainers. |
