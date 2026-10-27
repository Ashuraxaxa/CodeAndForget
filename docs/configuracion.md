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

## Datos (`Storage`)

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Storage:DataDirectory` | `data` (en la imagen: `/app/data`) | Carpeta de la metadata en SQLite (`mantenedores.db`) y de las llaves de cifrado (`keys/`), cuando se usan los valores por defecto. Una ruta relativa se resuelve contra la carpeta de la app. **Debe persistir y respaldarse.** |

## Metadata del servicio (`Metadata`)

Dónde guarda la app su propia configuración: las conexiones y los ajustes de cada mantenedor ([ajustes.md](ajustes.md)) (D-023).
Las tablas llevan el prefijo `mnt_` y se crean solas al arrancar (migraciones en `backend/src/Mantenedores.Metadata/Migrations/`).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Metadata:Provider` | `Sqlite` | `Sqlite` (un archivo, nada que instalar) · `Postgres` · `SqlServer` · `File` (archivos JSON `connections.json` y `overrides.json`, solo para desarrollo). |
| `Metadata:ConnectionString` | `Data Source=<datos>/mantenedores.db` en `Sqlite` | Obligatoria en `Postgres` y `SqlServer`. El usuario necesita crear tablas la primera vez (o se crean a mano con los scripts). |

**Varias instancias** (Kubernetes con más de una réplica, granja de IIS): usar `Postgres` o `SqlServer` **y** `DataProtection:KeyStore=Database`,
para que todas compartan conexiones y llaves sin un volumen común.

Si al arrancar existe `data/connections.json` (Fase 1) y la base está vacía, sus conexiones se importan una sola vez
y el archivo se renombra a `connections.json.importado`.

## Llaves de cifrado (`DataProtection`)

Las cadenas de conexión se guardan cifradas. Estas llaves son las que permiten leerlas: **si se pierden, hay que volver a ingresar las conexiones** (D-025).

| Clave | Por defecto | Qué hace |
|---|---|---|
| `DataProtection:KeyStore` | `FileSystem` | `FileSystem`: carpeta `<datos>/keys`. `Database`: tabla `mnt_dataprotection_key` de la metadata (requiere `Metadata:Provider` distinto de `File`). |
| `DataProtection:CertificatePath` | — | Certificado `.pfx` para cifrar las llaves en reposo. Opcional. |
| `DataProtection:CertificatePassword` | — | Contraseña del certificado. |

## Consultas (`Crud`)

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Crud:CommandTimeoutSeconds` | `30` | Tiempo máximo por consulta a la base administrada. |

## Autenticación (`Auth`)

Dos modos (D-024). El mismo build sirve para cualquiera: solo cambia la configuración.

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Auth:Mode` | `Local` | `Local`: usuarios en la configuración. `Jwt`: tokens de un emisor externo. |
| `Auth:AdminRoles` | `["admin"]` | Roles que administran la app (p. ej. configurar mantenedores). Se comparan sin distinguir mayúsculas. |

### Modo `Local`

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Auth:JwtKey` | — (**obligatoria en este modo**) | Clave para firmar los tokens, mínimo 32 caracteres. |
| `Auth:Issuer` | `mantenedores` | Emisor y audiencia de los tokens. |
| `Auth:TokenMinutes` | `480` | Duración del token. |
| `Auth:Users:N:Username` | — | Usuario N. |
| `Auth:Users:N:PasswordHash` | — | Hash PBKDF2 de su contraseña (ver abajo). |
| `Auth:Users:N:Roles:M` | — | Roles del usuario, p. ej. `admin`. |

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
| `Auth:Jwt:ProviderName` | `el proveedor de identidad` | Texto del botón "Ingresar con …". |

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

**Duende IdentityServer:**

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

`GET /api/auth/config` (anónimo) devuelve a la UI el modo, el `authority`, el `clientId`, los scopes y el nombre del proveedor; **nunca** devuelve llaves. `GET /api/auth/me` devuelve el nombre, los roles y si es administrador.

## Pruebas de integración

| Variable | Por defecto | Qué hace |
|---|---|---|
| `MANTENEDORES_PG_IMAGE` | `postgres:12` (versión mínima) | Imagen de PostgreSQL para la batería común. |
| `MANTENEDORES_MSSQL_IMAGE` | `mcr.microsoft.com/mssql/server:2017-latest` | Imagen de SQL Server (2016 no tiene imagen Linux). |
| `MANTENEDORES_MYSQL_IMAGE` | `mysql:8.0` (versión mínima) | Imagen de MySQL. |
| `MANTENEDORES_MARIADB_IMAGE` | `mariadb:10.6` (versión mínima) | Imagen de MariaDB. |
| `TESTCONTAINERS_RYUK_DISABLED` | `false` | Ponerla en `true` si el entorno no puede descargar la imagen auxiliar de Testcontainers. |
