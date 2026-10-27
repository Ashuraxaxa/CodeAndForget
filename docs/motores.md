# Motores soportados: diferencias técnicas

Referencia para implementar y mantener los proveedores (`IDatabaseProvider`).
Todo proveedor debe pasar la batería común de pruebas (P-002). Si una diferencia de esta
tabla no está cubierta por una prueba, falta la prueba.

## Alcance (D-029)

| Familia | Versión mínima | Driver .NET (D-030) | Licencia del driver | Imagen de pruebas |
|---|---|---|---|---|
| SQL Server | 2016 | `Microsoft.Data.SqlClient` | MIT | `mcr.microsoft.com/mssql/server:2022-latest` |
| PostgreSQL | 12 | `Npgsql` | PostgreSQL (MIT-like) | `postgres:16` |
| MySQL | 8.0 | `MySqlConnector` | MIT | `mysql:8.4` |
| MariaDB | 10.6 | `MySqlConnector` (mismo proveedor) | MIT | `mariadb:11.4` |
| Oracle | 19c | `Oracle.ManagedDataAccess.Core` | Oracle Free Use Terms | `gvenzl/oracle-free:23-slim` |

**Por qué esas versiones:**
- SQL Server 2016+: `OFFSET/FETCH` y las vistas `sys.*` que se usan.
- Postgres 12+: `attgenerated` (columnas generadas).
- MySQL 8.0+: `CHECK` real desde 8.0.16, defaults con expresión y CTE. MySQL 5.7 está fuera de soporte desde 2023.
- MariaDB 10.6+: versión LTS.
- Oracle 19c+: LTS vigente; tiene `OFFSET/FETCH` y columnas identity (12c+).
- Versiones anteriores: la prueba de conexión debe rechazarlas con un mensaje claro (P-028). No se intenta soportarlas a medias.

> **No usar `MySql.Data`** (driver oficial de Oracle para MySQL): su licencia es GPL y su soporte async es limitado.

---

## Diferencias que afectan al diseño

### Identificadores y "esquema"

| | SQL Server | PostgreSQL | MySQL / MariaDB | Oracle |
|---|---|---|---|---|
| Delimitador | `[x]` | `"x"` | `` `x` `` | `"x"` |
| Qué es el "esquema" | Esquema | Esquema | **La base de datos** (no hay esquemas dentro) | **El dueño (owner / usuario)** |
| Mayúsculas | Según la intercalación | Minúsculas si no se delimita | Según el SO y `lower_case_table_names` | **MAYÚSCULAS** si no se delimita |
| Largo máximo de nombre | 128 | **63 bytes (corta sin avisar)** | 64 | 128 (12.2+), 30 en versiones viejas |

**Consecuencias:**
- Siempre delimitar con el nombre exacto que devuelve el catálogo (ya es así, D-004).
- En MySQL, la conexión expone por defecto solo la base indicada en la cadena de conexión.
- En Oracle, la conexión expone por defecto solo el esquema del usuario conectado, porque recorrer todo el diccionario es lento (D-032).
- **No inventar alias largos:** hoy `columna__label` puede pasar los 63 bytes en Postgres. Usar alias cortos (`l0`, `l1`…) y mapearlos de vuelta (P-021).

### Parámetros

| | SQL Server | PostgreSQL | MySQL / MariaDB | Oracle |
|---|---|---|---|---|
| Prefijo | `@p0` | `@p0` (Npgsql lo traduce) | `@p0` | **`:p0`** |
| Detalle | | | | Requiere `BindByName = true`; si no, enlaza por posición |

**Consecuencia:** el prefijo deja de estar fijo en `QueryBuilder` y pasa al dialecto (P-020).

### Paginación y búsqueda

| | SQL Server | PostgreSQL | MySQL / MariaDB | Oracle |
|---|---|---|---|---|
| Paginación | `OFFSET … FETCH` (exige ORDER BY) | `LIMIT … OFFSET` | `LIMIT … OFFSET` | `OFFSET … ROWS FETCH NEXT … ROWS ONLY` |
| "Contiene" sin mayúsculas | `LIKE` (según intercalación) | `ILIKE` | `LIKE` (según intercalación) | `UPPER(col) LIKE UPPER(:p)` |
| Carácter de escape de LIKE | `\` funciona | `\` funciona | **`\` depende de `NO_BACKSLASH_ESCAPES`** | `\` funciona |
| Conteo rápido estimado | `sys.partitions` | `pg_class.reltuples` | `information_schema.TABLES.TABLE_ROWS` | `ALL_TABLES.NUM_ROWS` (estadísticas) |

**Consecuencia:** usar un carácter de escape neutro (`!`) en todos los dialectos (P-024).

### Devolver la clave después de insertar

| | Mecanismo | Funciona con triggers |
|---|---|---|
| SQL Server | `OUTPUT … INTO @tabla` + `SELECT` | Sí (D-010) |
| PostgreSQL | `RETURNING` (filas de resultado) | Sí |
| MariaDB 10.5+ | `RETURNING` (filas de resultado) | Sí |
| MySQL | **No hay `RETURNING`**: `LAST_INSERT_ID()` solo sirve para una PK `AUTO_INCREMENT` | Sí |
| Oracle | `RETURNING col INTO :out` (**parámetros de salida**, no filas) | Sí |

Casos especiales:
- **PK con default por expresión en MySQL** (p. ej. `UUID()`): no hay forma de recuperarla. El formulario exige el valor o se generan UUIDs en la app (override).
- **Oracle antiguo con secuencia + trigger:** no se detecta como identity. Se configura el nombre de la secuencia en un override (P-011) y se inserta con `seq.NEXTVAL`.

**Consecuencia:** el dialecto deja de devolver solo un texto SQL y declara una **estrategia de inserción**: filas de resultado, parámetros de salida, último id o valores provistos (P-020).

### Tipos con trampa

| Caso | Dónde | Qué hacer |
|---|---|---|
| **`''` es NULL** | Oracle (`VARCHAR2`) | El dialecto declara `EmptyStringIsNull`. Un texto obligatorio vacío es "obligatorio", no un error del motor (P-024) |
| **Números más grandes que `decimal` de .NET (28–29 dígitos)** | Oracle `NUMBER` sin precisión, PG `numeric` sin límite, MySQL `DECIMAL(65)` | Leer como texto cuando la precisión supera 28 o no está declarada (P-023). **Hoy ya puede fallar en Postgres** |
| Booleanos simulados | MySQL `TINYINT(1)` / `BIT(1)`, Oracle `NUMBER(1)` (antes de 23ai) | `TINYINT(1)` y `BIT(1)` → Boolean por defecto; `NUMBER(1)` configurable por override |
| Enteros sin signo | MySQL `INT UNSIGNED`, `BIGINT UNSIGNED` | `BIGINT UNSIGNED` no cabe en `long`: tratar como Decimal |
| Enum por columna | MySQL `ENUM('a','b')` | Leer valores desde `COLUMN_TYPE` y tratar como `Enum` |
| `SET('a','b')` | MySQL | Selección múltiple (Fase 2) o texto |
| Fechas "cero" `0000-00-00` | MySQL con modos SQL laxos | `ConvertZeroDateTime=true` en el driver; mostrar vacío |
| `DATE` incluye hora | Oracle | `DATE` → DateTime (no Date) |
| `TIMESTAMP` convierte a la zona de la sesión | MySQL | Fijar la zona de la sesión a UTC al abrir la conexión |
| LOBs (`CLOB`, `BLOB`, `TEXT`, `nvarchar(max)`) | Todos | **No ordenables** en Oracle (ORA-00932) y lentos de filtrar. El dialecto marca qué se puede ordenar, filtrar y buscar (P-022) |
| JSON | PG `json/jsonb`, MySQL `JSON`, MariaDB (alias de `LONGTEXT` + CHECK), Oracle 21c+ `JSON`, SQL Server (texto) | Detectar por tipo o por el CHECK `json_valid` (MariaDB) |
| Espaciales y XML | Todos | Solo lectura como texto (P-018) |

### Catálogo

| | Fuente | Notas |
|---|---|---|
| SQL Server | `sys.objects`, `sys.columns`, `sys.key_constraints`, `sys.foreign_keys` | Ya implementado |
| PostgreSQL | `pg_class`, `pg_attribute`, `pg_constraint`, `pg_enum` | Ya implementado |
| MySQL / MariaDB | `information_schema.TABLES`, `COLUMNS` (`EXTRA` indica `auto_increment` y columnas generadas), `KEY_COLUMN_USAGE`, `TABLE_CONSTRAINTS`, `STATISTICS` (índices únicos) | Las tablas **MyISAM** no tienen FK: se cubren con FK virtuales (P-011) |
| Oracle | `ALL_TABLES`, `ALL_VIEWS`, `ALL_TAB_COLUMNS`, `ALL_CONSTRAINTS` (P, U, R), `ALL_CONS_COLUMNS`, `ALL_TAB_IDENTITY_COLS` | Las vistas `ALL_*` son lentas: filtrar siempre por `OWNER`. Excluir esquemas del sistema (`ALL_USERS.ORACLE_MAINTAINED = 'Y'`). Los sinónimos quedan fuera del alcance de la v1 |

### Tablas sin PK

- Regla general: solo lectura (D-009) o PK virtual (P-011).
- Oracle: se podría usar `ROWID` como clave técnica. **No se usa por defecto**, porque puede cambiar si la tabla se reorganiza. Queda como opción explícita en un override.

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
