# Motores soportados: diferencias técnicas

Referencia para implementar y mantener los proveedores (`IDatabaseProvider`).
Todo proveedor debe pasar la batería común de pruebas (P-002). Si una diferencia de esta
tabla no está cubierta por una prueba, falta la prueba.

## Alcance (D-034)

| Familia | Versión mínima | Driver .NET (D-030) | Licencia del driver | Imagen de pruebas |
|---|---|---|---|---|
| SQL Server | 2016 | `Microsoft.Data.SqlClient` | MIT | `mcr.microsoft.com/mssql/server:2022-latest` |
| PostgreSQL | 12 | `Npgsql` | PostgreSQL (MIT-like) | `postgres:16` |
| MySQL | 8.0 | `MySqlConnector` | MIT | `mysql:8.4` |
| MariaDB | 10.6 | `MySqlConnector` (mismo proveedor) | MIT | `mariadb:11.4` |

**Oracle queda fuera de alcance** (D-034). Sus notas están al final de este archivo por si se retoma.

**Por qué esas versiones:**
- SQL Server 2016+: `OFFSET/FETCH` y las vistas `sys.*` que se usan.
- Postgres 12+: `attgenerated` (columnas generadas).
- MySQL 8.0+: `CHECK` real desde 8.0.16, defaults con expresión y CTE. MySQL 5.7 está fuera de soporte desde 2023.
- MariaDB 10.6+: versión LTS.
- Versiones anteriores: la prueba de conexión debe rechazarlas con un mensaje claro (P-028). No se intenta soportarlas a medias.

> **No usar `MySql.Data`** (driver oficial de Oracle para MySQL): su licencia es GPL y su soporte async es limitado.

---

## Diferencias que afectan al diseño

### Identificadores y "esquema"

| | SQL Server | PostgreSQL | MySQL / MariaDB |
|---|---|---|---|
| Delimitador | `[x]` | `"x"` | `` `x` `` |
| Qué es el "esquema" | Esquema | Esquema | **La base de datos** (no hay esquemas dentro) |
| Mayúsculas | Según la intercalación | Minúsculas si no se delimita | Según el SO y `lower_case_table_names` |
| Largo máximo de nombre | 128 | **63 bytes (corta sin avisar)** | 64 |

**Consecuencias:**
- Siempre delimitar con el nombre exacto que devuelve el catálogo (ya es así, D-004).
- En MySQL, la conexión expone por defecto solo la base indicada en la cadena de conexión (D-032).
- **No inventar alias largos:** hoy `columna__label` puede pasar los 63 bytes en Postgres. Usar alias cortos (`l0`, `l1`…) y mapearlos de vuelta (P-021).

### Parámetros

Los tres usan `@p0` (Npgsql lo traduce en Postgres). Aun así, el prefijo pasa al dialecto (P-020): cuesta muy poco y deja la puerta abierta a motores que usan `:p0`.

### Paginación y búsqueda

| | SQL Server | PostgreSQL | MySQL / MariaDB |
|---|---|---|---|
| Paginación | `OFFSET … FETCH` (exige ORDER BY) | `LIMIT … OFFSET` | `LIMIT … OFFSET` |
| "Contiene" sin mayúsculas | `LIKE` (según intercalación) | `ILIKE` | `LIKE` (según intercalación) |
| Carácter de escape de LIKE | `\` funciona | `\` funciona | **`\` depende de `NO_BACKSLASH_ESCAPES`** |
| Conteo rápido estimado | `sys.partitions` | `pg_class.reltuples` | `information_schema.TABLES.TABLE_ROWS` |

**Consecuencia:** usar un carácter de escape neutro (`!`) en todos los dialectos (P-024).

### Devolver la clave después de insertar

| | Mecanismo | Funciona con triggers |
|---|---|---|
| SQL Server | `OUTPUT … INTO @tabla` + `SELECT` | Sí (D-010) |
| PostgreSQL | `RETURNING` | Sí |
| MariaDB 10.5+ | `RETURNING` | Sí |
| MySQL | **No hay `RETURNING`**: `LAST_INSERT_ID()` solo sirve para una PK `AUTO_INCREMENT` | Sí |

Caso especial: si la PK de MySQL tiene un default por expresión (p. ej. `UUID()`), no hay forma de recuperarla. El formulario exige el valor o se generan UUIDs en la app (override).

**Consecuencia:** el dialecto deja de devolver solo un texto SQL y declara una **estrategia de inserción**: filas de resultado, último id o valores provistos (P-020).

### Tipos con trampa

| Caso | Dónde | Qué hacer |
|---|---|---|
| **Números más grandes que `decimal` de .NET (28–29 dígitos)** | PG `numeric` sin límite, MySQL `DECIMAL(65)` | Leer como texto cuando la precisión supera 28 o no está declarada (P-023). **Hoy ya puede fallar en Postgres** |
| Booleanos simulados | MySQL `TINYINT(1)` / `BIT(1)` | Boolean por defecto, corregible por override |
| Enteros sin signo | MySQL `INT UNSIGNED`, `BIGINT UNSIGNED` | `BIGINT UNSIGNED` no cabe en `long`: tratar como Decimal |
| Enum por columna | MySQL `ENUM('a','b')` | Leer valores desde `COLUMN_TYPE` y tratar como `Enum` |
| `SET('a','b')` | MySQL | Selección múltiple (Fase 2) o texto |
| Fechas "cero" `0000-00-00` | MySQL con modos SQL laxos | `ConvertZeroDateTime=true` en el driver; mostrar vacío |
| `TIMESTAMP` convierte a la zona de la sesión | MySQL | Fijar la zona de la sesión a UTC al abrir la conexión |
| Textos y binarios largos (`TEXT`, `BLOB`, `nvarchar(max)`) | Todos | Lentos de filtrar y, en algunos casos, no ordenables. El dialecto marca qué se puede ordenar, filtrar y buscar (P-022) |
| JSON | PG `json/jsonb`, MySQL `JSON`, MariaDB (alias de `LONGTEXT` + CHECK), SQL Server (texto) | Detectar por tipo o por el CHECK `json_valid` (MariaDB) |
| Espaciales y XML | Todos | Solo lectura como texto (P-018) |

### Catálogo

| | Fuente | Notas |
|---|---|---|
| SQL Server | `sys.objects`, `sys.columns`, `sys.key_constraints`, `sys.foreign_keys` | Ya implementado |
| PostgreSQL | `pg_class`, `pg_attribute`, `pg_constraint`, `pg_enum` | Ya implementado |
| MySQL / MariaDB | `information_schema.TABLES`, `COLUMNS` (`EXTRA` indica `auto_increment` y columnas generadas), `KEY_COLUMN_USAGE`, `TABLE_CONSTRAINTS`, `STATISTICS` (índices únicos) | Las tablas **MyISAM** no tienen FK: se cubren con FK virtuales (P-011) |

### Configuración del servidor que cambia el comportamiento (MySQL / MariaDB)

- `sql_mode`: por ejemplo `NO_BACKSLASH_ESCAPES`, `ANSI_QUOTES` o fechas cero permitidas.
- Intercalación (`collation`): define si las búsquedas distinguen mayúsculas.
- `lower_case_table_names`: define si los nombres de tabla distinguen mayúsculas.

La prueba de conexión debe mostrar estos valores para diagnosticar rápido (P-028).

---

## Cómo agregar un motor nuevo (resumen)

1. Proyecto `backend/src/Mantenedores.Providers.<Motor>`, con:
   - `<Motor>Provider`
   - `<Motor>Dialect`
   - `<Motor>SchemaProvider`
   - `<Motor>TypeMapper`
2. Las consultas de catálogo devuelven solo filas crudas; `SchemaAssembler` hace el resto (D-003).
3. Base de ejemplo en `samples/<motor>/01_demo.sql`, equivalente a las existentes: PK compuesta, autorreferencia, N:M, vista, tabla sin PK, trigger, enum o JSON si el motor los tiene.
4. Agregar el motor a la batería común de pruebas y a la matriz de la CI (P-027).
5. Documentar sus diferencias en este archivo.

---

## Oracle (fuera de alcance, D-034)

Se dejó fuera el 2026-10-27. Si se retoma, este es el punto de partida (análisis hecho antes de descartarlo):

- **Driver:** `Oracle.ManagedDataAccess.Core`, sin cliente instalado. Requiere `BindByName = true`. Versión mínima razonable: 19c.
- **Parámetros:** usan `:p0`. Ya estará cubierto si el prefijo vive en el dialecto (P-020).
- **Esquema:** es el owner. Las vistas `ALL_*` son lentas; filtrar siempre por `OWNER`.
- **Inserción:** `RETURNING … INTO` devuelve **parámetros de salida**, no filas; habría que sumar esa estrategia de inserción. Las tablas antiguas con secuencia + trigger no se detectan como identity.
- **Trampas de tipos:**
  - `''` es NULL.
  - `NUMBER` sin precisión puede superar a `decimal`.
  - `DATE` incluye la hora.
  - `NUMBER(1)` se usa como booleano.
  - `CLOB` no se puede ordenar (ORA-00932).
- **Nombres:** se guardan en MAYÚSCULAS si no se delimitan; largo máximo 128 (30 en versiones viejas).
- **Esfuerzo estimado:** 1,5 a 2 semanas, más la prueba contra una base real.
