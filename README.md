# Mantenedores dinámicos

Servicio que se conecta a una base **SQL Server**, **PostgreSQL**, **MySQL** o **MariaDB**, lee su catálogo
(tablas, columnas, PK, FK, únicos) y genera en tiempo real el **mantenedor** de cada
tabla: listado con filtros, alta, edición y borrado, con combos para las claves foráneas.

> Estado: **Fase 1 (MVP)**. La memoria del proyecto (estado, decisiones, pendientes) está en
> [docs/memoria/](docs/memoria/README.md) y la hoja de ruta en [docs/siguientes-pasos.md](docs/siguientes-pasos.md).

## Cómo funciona

```
React (grilla + formulario dinámicos)
        │  /api
        ▼
API .NET 8 ──► SchemaService ──► IDatabaseProvider (SqlServer | Postgres)
        │            │ caché en memoria del esquema
        ▼            ▼
   CrudService ──► QueryBuilder + ISqlDialect ──► BD destino
        │
   Metadata store: SQLite · Postgres · SQL Server (conexiones con secreto cifrado)
```

1. **Lectura del esquema**: cada proveedor consulta su catálogo (`sys.*` o `pg_catalog`)
   y entrega filas crudas. `SchemaAssembler` las une en un modelo común: tipo lógico
   por columna, PK, FK salientes y entrantes, y la columna a mostrar en los combos.
2. **SQL seguro**: `QueryBuilder` solo acepta tablas y columnas que existan en esa
   metadata, delimita todo identificador (`[x]` / `"x"`) y envía los valores siempre
   como parámetros.
3. **UI generada**: el frontend pide la definición de la tabla y dibuja el control de
   cada columna según su tipo (texto, número, fecha, enum, JSON, FK con búsqueda…).

## Estructura

```
backend/
  src/Mantenedores.Core                  Modelo de esquema, QueryBuilder, servicios CRUD (sin dependencia de motor)
  src/Mantenedores.Providers.SqlServer   Lectura de catálogo + dialecto SQL Server
  src/Mantenedores.Providers.Postgres    Lectura de catálogo + dialecto PostgreSQL
  src/Mantenedores.Providers.MySql       Lectura de catálogo + dialecto MySQL / MariaDB
  src/Mantenedores.Metadata              Metadata del servicio (conexiones, llaves) y sus migraciones
  src/Mantenedores.Api                   API minimal, autenticación, endpoints
  tests/Mantenedores.Tests               Pruebas unitarias (xUnit)
  tests/Mantenedores.IntegrationTests    Batería común por motor contra bases reales (Testcontainers)
frontend/                                React + Vite + TanStack Query
samples/                                 Bases de ejemplo (mismo modelo en ambos motores)
docs/plan.md                             Viabilidad, escalabilidad y plan por fases
```

## Requisitos de instalación

> **Antes de instalar o de conectar una base nueva, verifica esta sección.**
> Las versiones mínimas son una decisión del proyecto (D-035): por debajo de ellas el servicio
> no se prueba y la conexión no está soportada.

### Bases de datos que se pueden administrar

| Motor | Versión mínima | Estado | Cómo ver la versión |
|---|---|---|---|
| SQL Server | **2016** (13.x) | ✅ Soportado | `SELECT SERVERPROPERTY('ProductVersion');` |
| PostgreSQL | **12** | ✅ Soportado | `SHOW server_version;` |
| MySQL | **8.0** | ✅ Soportado | `SELECT VERSION();` |
| MariaDB | **10.6** | ✅ Soportado | `SELECT VERSION();` (el texto incluye `MariaDB`) |

Oracle no está soportado (D-034).

**Si la versión es menor**, actualiza el motor antes de conectarlo. No hay modo de compatibilidad para versiones
fuera de soporte del fabricante, porque cada una agrega casos especiales que nadie prueba.

Las pruebas automáticas corren contra la **versión mínima** y contra una **actual** de cada motor:
- PostgreSQL 12 y 16.
- SQL Server 2017 y 2022. SQL Server 2016 no tiene imagen para contenedores: su compatibilidad se cuida no usando T-SQL posterior a 2016.
- MySQL 8.0 y 8.4; MariaDB 10.6 y 11.4.

La **prueba de conexión** muestra el producto y la versión del servidor, y avisa si está por debajo del mínimo.

Detalle técnico por motor: [docs/motores.md](docs/motores.md).

### Usuario de base de datos

Crea un usuario dedicado para el servicio, nunca `sa`, `postgres` ni `root`. Necesita:
- Lectura del catálogo (`sys.*` / `pg_catalog` / `information_schema`).
- `SELECT`, `INSERT`, `UPDATE` y `DELETE` sobre los esquemas que quieras exponer.

### Para ejecutar el servicio

- **Con Docker:** Docker 20+ (o cualquier entorno de contenedores).
- **Sin Docker:** el runtime de ASP.NET Core 8.
- **Para desarrollar:** .NET 8 SDK y Node 20+. Para las pruebas de integración, además, Docker.

## Ejecutar

### Opción A: Docker (todo incluido)

```bash
docker compose up --build
```

Abre http://localhost:8080, entra con `admin` / `admin` y crea las conexiones:

| Motor | Cadena de conexión |
|---|---|
| PostgreSQL | `Host=pg-demo;Port=5432;Database=demo;Username=postgres;Password=postgres` |
| SQL Server | `Server=mssql-demo,1433;Database=Demo;User Id=sa;Password=Demo_Pass_123!;TrustServerCertificate=True` |
| MySQL | `Server=mysql-demo;Port=3306;Database=ventas;User ID=root;Password=demo` |

### Opción B: desarrollo local

Requisitos: ver [Requisitos de instalación](#requisitos-de-instalación).

```bash
# API en http://localhost:5080 (usa appsettings.Development.json: admin / admin)
cd backend && dotnet run --project src/Mantenedores.Api

# Frontend en http://localhost:5173 (redirige /api a la API)
cd frontend && npm install && npm run dev
```

Pruebas:

```bash
cd backend
dotnet test tests/Mantenedores.Tests              # unitarias, sin dependencias
dotnet test tests/Mantenedores.IntegrationTests   # contra bases reales en contenedores (requiere Docker)

# Por defecto usan las versiones mínimas; para probar otra:
MANTENEDORES_PG_IMAGE=postgres:16 MANTENEDORES_MSSQL_IMAGE=mcr.microsoft.com/mssql/server:2022-latest \
MANTENEDORES_MYSQL_IMAGE=mysql:8.4 MANTENEDORES_MARIADB_IMAGE=mariadb:11.4 \
  dotnet test tests/Mantenedores.IntegrationTests
```

## Configuración

| Clave | Para qué |
|---|---|
| `Auth__Mode` | `Local` (usuarios en la configuración, por defecto) o `Jwt` (Duende IdentityServer, Keycloak, Entra ID u otro emisor). |
| `Auth__JwtKey` | Clave para firmar tokens del modo `Local` (mín. 32 caracteres). **Obligatoria en ese modo**. |
| `Auth__Jwt__Authority` / `Auth__Jwt__ClientId` | Emisor OIDC y cliente público para el login con PKCE desde la UI (modo `Jwt`). |
| `Auth__Users__0__Username` / `Auth__Users__0__PasswordHash` / `Auth__Users__0__Roles__0` | Usuarios del modo `Local`. El hash se genera con `dotnet run --project backend/src/Mantenedores.Api -- hash-password "clave"`. |
| `Storage__DataDirectory` | Carpeta de datos (metadata SQLite y llaves de cifrado por defecto). Por defecto `data/`. |
| `Metadata__Provider` | Dónde guarda la app sus conexiones: `Sqlite` (por defecto), `Postgres` o `SqlServer` (con `Metadata__ConnectionString`). |
| `DataProtection__KeyStore` | Llaves de cifrado en `FileSystem` (por defecto) o en la base de metadata (`Database`), necesario para varias instancias. |
| `Crud__CommandTimeoutSeconds` | Tiempo máximo por consulta. Por defecto 30. |
| `Hosting__PathBase` | Subruta de publicación, p. ej. `/mantenedores`. Por defecto la raíz. |
| `Hosting__ForwardedHeaders__Enabled` | Activa `X-Forwarded-*` detrás de un proxy (HTTPS terminado en nginx, IIS, Traefik…). |

Referencia completa, con todas las claves y sus valores por defecto: [docs/configuracion.md](docs/configuracion.md).
Cómo instalar en Docker, Kubernetes, IIS, servicio de Windows o systemd: [docs/despliegue.md](docs/despliegue.md).

Health checks sin autenticación: `/api/health/live` (proceso vivo) y `/api/health/ready` (listo para atender).

> La carpeta de datos contiene las llaves que descifran las cadenas de conexión: respáldala
> y protégela. Si se pierde, hay que volver a ingresar las conexiones.

## API

Todas las rutas (salvo `auth/config`, `auth/login` y health) requieren `Authorization: Bearer <token>`: del login local o del proveedor de identidad.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/auth/config` | Modo de autenticación y datos públicos para el login (sin secretos) |
| POST | `/api/auth/login` | Solo modo `Local`: `{ username, password }` → `{ token }` |
| GET | `/api/auth/me` | Nombre, roles y si es administrador |
| GET | `/api/connections` | Lista conexiones (sin la cadena de conexión) |
| PUT | `/api/connections/{id}` | Crea o edita. Si `connectionString` va vacío, se conserva la anterior |
| POST | `/api/connections/test` | Prueba sin guardar |
| DELETE | `/api/connections/{id}` | Elimina la conexión (no toca la base) |
| GET | `/api/meta/{conn}/tables` | Tablas y vistas descubiertas |
| GET | `/api/meta/{conn}/tables/{schema}/{table}` | Definición completa (columnas, PK, FK) |
| POST | `/api/meta/{conn}/refresh` | Vuelve a leer el esquema |
| GET | `/api/meta/{conn}/overrides[/{schema}/{table}]` | Ajustes de los mantenedores ([docs/ajustes.md](docs/ajustes.md)) |
| PUT / DELETE | `/api/meta/{conn}/overrides/{schema}/{table}` | Guarda o quita el ajuste de una tabla. Solo administradores |
| POST | `/api/meta/{conn}/overrides/{schema}/{table}/preview` | Vista previa de un ajuste sin guardarlo. Solo administradores |
| GET | `/api/data/{conn}/{schema}/{table}` | Lista. `?page=1&pageSize=25&sort=nombre,-precio&q=texto&f.categoria_id=2` |
| GET | `/api/data/{conn}/{schema}/{table}/row?{pk}=…` | Un registro. PK compuesta: `?PedidoId=1&Linea=2` |
| POST | `/api/data/{conn}/{schema}/{table}` | Crea. Devuelve la fila creada |
| PUT | `/api/data/{conn}/{schema}/{table}/row?{pk}=…` | Actualiza |
| DELETE | `/api/data/{conn}/{schema}/{table}/row?{pk}=…` | Elimina |
| GET | `/api/data/{conn}/{schema}/{table}/lookup?q=…` | `[{ value, label }]` para combos de FK |

En los listados, cada FK de una columna trae además `<columna>__label` con el texto de la tabla referenciada.

## Qué cubre la Fase 1

- Lectura de tablas, vistas, columnas, PK (simples y compuestas), únicos y FK en ambos motores.
- Tipos: texto, números, booleanos, fechas (con y sin zona), hora, UUID, JSON y enums de Postgres.
- Identity, serial, columnas calculadas y `rowversion` se detectan como solo lectura.
- Inserción en SQL Server compatible con tablas que tienen triggers (`OUTPUT … INTO`).
- Vistas y tablas sin PK: solo lectura.
- Errores del motor (FK, únicos, CHECK) se muestran al usuario.

**Pendiente para fases siguientes**: personalización (etiquetas, campos ocultos, columna a mostrar),
maestro-detalle, N:M con selección múltiple, permisos por rol, auditoría, concurrencia optimista,
SSO, caché distribuida y tipos especiales (arreglos, `geography`, `hierarchyid`, binarios).
