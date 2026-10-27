# Mantenedores dinámicos

Servicio que se conecta a una base **SQL Server** o **PostgreSQL**, lee su catálogo
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
   FileConnectionStore (conexiones con secreto cifrado)
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
  src/Mantenedores.Api                   API minimal, JWT, almacén de conexiones
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
| MySQL | **8.0** | 🚧 En desarrollo (bloque M) | `SELECT VERSION();` |
| MariaDB | **10.6** | 🚧 En desarrollo (bloque M) | `SELECT VERSION();` (el texto incluye `MariaDB`) |

Oracle no está soportado (D-034).

**Si la versión es menor**, actualiza el motor antes de conectarlo. No hay modo de compatibilidad para versiones
fuera de soporte del fabricante, porque cada una agrega casos especiales que nadie prueba.

Las pruebas automáticas corren contra la **versión mínima** y contra una **actual** de cada motor:
- PostgreSQL 12 y 16.
- SQL Server 2017 y 2022. SQL Server 2016 no tiene imagen para contenedores: su compatibilidad se cuida no usando T-SQL posterior a 2016.

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
  dotnet test tests/Mantenedores.IntegrationTests
```

## Configuración

| Clave | Para qué |
|---|---|
| `Auth__JwtKey` | Clave para firmar tokens (mín. 32 caracteres). **Obligatoria**. |
| `Auth__Users__0__Username` / `Auth__Users__0__PasswordHash` | Usuarios del login básico. El hash se genera con `dotnet run --project backend/src/Mantenedores.Api -- hash-password "clave"`. |
| `Storage__DataDirectory` | Carpeta donde se guardan las conexiones (cifradas) y las llaves de cifrado. Por defecto `data/`. |
| `Crud__CommandTimeoutSeconds` | Tiempo máximo por consulta. Por defecto 30. |

> La carpeta de datos contiene las llaves que descifran las cadenas de conexión: respáldala
> y protégela. Si se pierde, hay que volver a ingresar las conexiones.

## API

Todas las rutas (salvo login y health) requieren `Authorization: Bearer <token>`.

| Método | Ruta | Descripción |
|---|---|---|
| POST | `/api/auth/login` | `{ username, password }` → `{ token }` |
| GET | `/api/connections` | Lista conexiones (sin la cadena de conexión) |
| PUT | `/api/connections/{id}` | Crea o edita. Si `connectionString` va vacío, se conserva la anterior |
| POST | `/api/connections/test` | Prueba sin guardar |
| DELETE | `/api/connections/{id}` | Elimina la conexión (no toca la base) |
| GET | `/api/meta/{conn}/tables` | Tablas y vistas descubiertas |
| GET | `/api/meta/{conn}/tables/{schema}/{table}` | Definición completa (columnas, PK, FK) |
| POST | `/api/meta/{conn}/refresh` | Vuelve a leer el esquema |
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
