# Configuración

Toda la configuración se lee de `appsettings.json`, de `appsettings.{Entorno}.json` y de variables
de entorno. En una variable de entorno, cada `:` se escribe como `__` (doble guion bajo):
`Hosting:PathBase` → `Hosting__PathBase`.

**Principio (D-022):** sin configurar nada, la app arranca en la raíz, sin proxy, con datos en `data/`
y sin servicios externos. Solo es obligatoria `Auth:JwtKey`.

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
| `Storage:DataDirectory` | `data` (en la imagen: `/app/data`) | Carpeta de las conexiones (`connections.json`, con secretos cifrados) y de las llaves de cifrado (`keys/`). Una ruta relativa se resuelve contra la carpeta de la app. **Debe persistir y respaldarse**: sin las llaves no se pueden leer las conexiones. |

## Consultas (`Crud`)

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Crud:CommandTimeoutSeconds` | `30` | Tiempo máximo por consulta a la base administrada. |

## Autenticación (`Auth`)

Por ahora solo existe el modo local (D-012). El modo `Jwt` para emisores externos (Duende IdentityServer,
Keycloak…) llega con P-041.

| Clave | Por defecto | Qué hace |
|---|---|---|
| `Auth:JwtKey` | — (**obligatoria**) | Clave para firmar los tokens, mínimo 32 caracteres. La app no arranca sin ella. |
| `Auth:Issuer` | `mantenedores` | Emisor y audiencia de los tokens. |
| `Auth:TokenMinutes` | `480` | Duración del token. |
| `Auth:Users:N:Username` | — | Usuario N del login local. |
| `Auth:Users:N:PasswordHash` | — | Hash PBKDF2 de su contraseña. Se genera con el comando `hash-password` de la API (ver abajo). |

Generar un hash:

```bash
# con la app publicada
dotnet Mantenedores.Api.dll hash-password "clave"
# desde el código fuente
dotnet run --project backend/src/Mantenedores.Api -- hash-password "clave"
```

> Los valores de `appsettings.Development.json` y de `docker-compose.yml` (`admin` / `admin`) son **solo de demo**.

## Pruebas de integración

| Variable | Por defecto | Qué hace |
|---|---|---|
| `MANTENEDORES_PG_IMAGE` | `postgres:12` (versión mínima) | Imagen de PostgreSQL para la batería común. |
| `MANTENEDORES_MSSQL_IMAGE` | `mcr.microsoft.com/mssql/server:2017-latest` | Imagen de SQL Server (2016 no tiene imagen Linux). |
| `MANTENEDORES_MYSQL_IMAGE` | `mysql:8.0` (versión mínima) | Imagen de MySQL. |
| `MANTENEDORES_MARIADB_IMAGE` | `mariadb:10.6` (versión mínima) | Imagen de MariaDB. |
| `TESTCONTAINERS_RYUK_DISABLED` | `false` | Ponerla en `true` si el entorno no puede descargar la imagen auxiliar de Testcontainers. |
