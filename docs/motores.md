# Motores soportados: diferencias técnicas

Referencia para implementar y mantener los proveedores (`IDatabaseProvider`).
Todo proveedor debe pasar la batería común de pruebas (P-002). Si una diferencia de esta
tabla no está cubierta por una prueba, falta la prueba.

## Alcance (D-034, D-035)

| Familia | Versión mínima | Driver .NET (D-030) | Licencia del driver | Imágenes de prueba (mínima · actual) |
|---|---|---|---|---|
| SQL Server | 2016 | `Microsoft.Data.SqlClient` | MIT | `mssql/server:2017-latest` · `mssql/server:2022-latest` (1) |
| PostgreSQL | 12 | `Npgsql` | PostgreSQL (MIT-like) | `postgres:12` · `postgres:16` |
| MySQL | 8.0 | `MySqlConnector` | MIT | `mysql:8.0` · `mysql:8.4` |
| MariaDB | 10.6 | `MySqlConnector` (mismo proveedor) | MIT | `mariadb:10.6` · `mariadb:11.4` |

(1) SQL Server 2016 no tiene imagen Linux; 2017 es la más antigua en contenedor. Para no romper 2016,
**no usar T-SQL posterior a 2016**: `STRING_AGG`, `TRIM`, `CONCAT_WS`, `TRANSLATE` (2017+) ni `GREATEST`/`LEAST` (2022+).

**Política de pruebas (D-035):** la batería común corre siempre contra la versión **mínima**, que es la que
más casos rompe (p. ej. `gen_random_uuid()` no existe en PostgreSQL 12 sin `pgcrypto`), y contra una **actual**.
Las imágenes se cambian con `MANTENEDORES_PG_IMAGE` y `MANTENEDORES_MSSQL_IMAGE` (y, en el bloque M,
`MANTENEDORES_MYSQL_IMAGE` y `MANTENEDORES_MARIADB_IMAGE`).

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

Los tres usan `@p0` (Npgsql lo traduce en Postgres). El prefijo vive en el dialecto (`ISqlDialect.ParameterPrefix`, ✅ P-020), así que un motor con `:p0` no requiere cambios en Core.

### Paginación y búsqueda

| | SQL Server | PostgreSQL | MySQL / MariaDB |
|---|---|---|---|
| Paginación | `OFFSET … FETCH` (exige ORDER BY) | `LIMIT … OFFSET` | `LIMIT … OFFSET` |
| "Contiene" sin mayúsculas | `LIKE` (según intercalación) | `ILIKE` | `LIKE` (según intercalación) |
| Carácter de escape de LIKE | `\` funciona | `\` funciona | **`\` depende de `NO_BACKSLASH_ESCAPES`** |
| Conteo rápido estimado | `sys.partitions` | `pg_class.reltuples` | `information_schema.TABLES.TABLE_ROWS` |

**Consecuencia:** todos los dialectos usan `!` como escape (`QueryBuilder.LikeEscape`, ✅ P-024).

### Devolver la clave después de insertar

| | Mecanismo | Funciona con triggers |
|---|---|---|
| SQL Server | `OUTPUT … INTO @tabla` + `SELECT` | Sí (D-010) |
| PostgreSQL | `RETURNING` | Sí |
| MariaDB 10.5+ | `RETURNING` (no se usa: el proveedor usa `LAST_INSERT_ID()` en ambos, D-040) | Sí |
| MySQL | **No hay `RETURNING`**: `LAST_INSERT_ID()` solo sirve para una PK `AUTO_INCREMENT` | Sí |

Caso especial: si la PK de MySQL tiene un default por expresión (p. ej. `UUID()`), no hay forma de recuperarla. **Si es UUID (`CHAR(36)`), la genera la app** y la envía (D-040); si es de otro tipo, el formulario pide el valor.

**Cómo lo resuelve Core (✅ P-020, D-040):** `QueryBuilder.BuildInsert` prueba en orden:
1. `ISqlDialect.InsertReturningKeys` (SQL Server, PostgreSQL).
2. Si la PK es una sola columna autonumérica: `ISqlDialect.LastInsertIdExpression` (MySQL/MariaDB: `LAST_INSERT_ID()`).
3. Si la PK vino completa en el alta: se usa esa.

Si no se cumple ninguna, pide el valor de la clave al usuario.

### Tipos con trampa

| Caso | Dónde | Qué hacer |
|---|---|---|
| **Npgsql guarda los tipos en caché al primer uso** | PostgreSQL: un enum creado después de esa conexión no se puede leer ("-.-") | Los enums se leen como texto (`CAST(... AS text)`) (D-041) |
| **Cada driver devuelve fechas y horas con tipos distintos** | Npgsql 10: `DateOnly`/`TimeOnly`; SqlClient: `DateTime`/`TimeSpan` | `CrudService` normaliza: Date → `DateOnly` (`AAAA-MM-DD`), Time → `TimeOnly` (`HH:MM:SS`) (D-036) |
| **Arreglos de PostgreSQL** | `text[]`, `int4[]`… Npgsql no lee un `int4[]` con nulos como `int[]` | Se leen como `text[]`, se escriben como texto validado por elemento con `CAST(@p AS int4[])` (P-018, D-059) |
| **Tipos espaciales y jerárquicos** | SQL Server `geography`/`geometry`/`hierarchyid`, PostGIS, MySQL `POINT`… | Solo lectura, como texto: `.STAsText()`, `.ToString()`, `ST_AsText()` en `SelectExpression` (P-018, D-059) |
| **Binarios y nulos** | SQL Server no convierte un `NULL` sin tipo (nvarchar) a `varbinary` | `ParameterFor` hace `CAST(@p AS varbinary(max))`; el tamaño se calcula con `ByteLength` (`DATALENGTH` / `OCTET_LENGTH`) (P-018) |
| **Números más grandes que `decimal` de .NET (28–29 dígitos)** | PG `numeric` sin límite, SQL Server y MySQL `DECIMAL(29..65)` | El proveedor marca `IsLargeNumber`; se leen como texto (`SelectExpression`) y se escriben como texto validado (✅ P-023, D-041) |
| Booleanos simulados | MySQL `TINYINT(1)` / `BIT(1)` | Boolean por defecto, corregible por override |
| Enteros sin signo | MySQL `INT UNSIGNED`, `BIGINT UNSIGNED` | `BIGINT UNSIGNED` no cabe en `long`: tratar como Decimal |
| Enum por columna | MySQL `ENUM('a','b')` | Leer valores desde `COLUMN_TYPE` y tratar como `Enum` |
| `SET('a','b')` | MySQL | Selección múltiple (Fase 2) o texto |
| Fechas "cero" `0000-00-00` | MySQL con modos SQL laxos | El proveedor fuerza `ConvertZeroDateTime=true` en la conexión |
| `TINYINT` con signo, `INT UNSIGNED` | MySQL | El tipo nativo se informa como `tinyint signed` / `int unsigned` para que Core no aplique rangos de SQL Server |
| UUID en `CHAR(36)` | MySQL | Se trata como Uuid (MySqlConnector lo lee como `Guid`) |
| `TIMESTAMP` convierte a la zona de la sesión | MySQL | La sesión se fija en UTC al abrir la conexión (`InitializeSessionAsync`) y el valor se devuelve como UTC (`HasTimeZone`) |
| Tipos no comparables | SQL Server `text`, `ntext`, `image`, `xml`; PostgreSQL `json`, `xml` | El proveedor los marca `Sortable = false` y `Filterable = false`; la API los rechaza y la UI no ofrece ordenar ni filtrar (los textos igual admiten "contiene") (✅ P-022, D-042) |
| JSON | PG `json/jsonb`, MySQL `JSON`, MariaDB (alias de `LONGTEXT` + CHECK), SQL Server (texto) | Detectar por tipo o por el CHECK `json_valid` (MariaDB) |
| Espaciales y XML | Todos | Solo lectura como texto (P-018) |

### Catálogo

| | Fuente | Notas |
|---|---|---|
| SQL Server | `sys.objects`, `sys.columns`, `sys.key_constraints`, `sys.foreign_keys` | Ya implementado |
| PostgreSQL | `pg_class`, `pg_attribute`, `pg_constraint`, `pg_enum` | Ya implementado |
| MySQL / MariaDB | `information_schema.TABLES`, `COLUMNS` (`EXTRA` indica `auto_increment` y columnas generadas), `KEY_COLUMN_USAGE` (PK, UNIQUE y FK; no `TABLE_CONSTRAINTS`, ver abajo), `STATISTICS` (índices únicos) | Las tablas **MyISAM** no tienen FK: se cubren con FK virtuales (P-011) |

**Usuario de la base con solo lectura (P-073).** MariaDB 10.6 no muestra `information_schema.TABLE_CONSTRAINTS` a un
usuario con solo `SELECT`, pero sí `KEY_COLUMN_USAGE`. Por eso las claves de MySQL y MariaDB se leen solo de
`KEY_COLUMN_USAGE`: la PK se llama siempre `PRIMARY`, una FK tiene tabla referenciada y el resto son `UNIQUE`.

**Permiso denegado (P-073)**, por código: PostgreSQL `42501`, SQL Server `229`/`230`, MySQL y MariaDB `1142`/`1143`.
Se traduce a "el usuario con que la app se conecta a la base no tiene permiso para …" (403).

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
