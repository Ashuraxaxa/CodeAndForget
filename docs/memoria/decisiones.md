# Registro de decisiones

Formato: **qué** se decidió, **por qué**, y qué **alternativa** se descartó.
Estado: `Vigente`, `Temporal` (se reemplazará en una fase futura) o `Reemplazada por D-yyy`.

---

## Arquitectura

### D-001 · Backend en .NET 8 con Dapper, sin ORM
- **Estado:** Vigente · 2026-10-27
- **Por qué:** muy buen soporte de SQL Server y de PostgreSQL (Npgsql). Las consultas se generan en tiempo de ejecución sobre tablas desconocidas, así que un ORM con modelos fijos no aporta; Dapper da control total del SQL.
- **Alternativas descartadas:** Node/NestJS con Knex (equivalente, pero el equipo apunta a MSSQL); EF Core (pensado para modelos conocidos al compilar).

### D-002 · Dialecto SQL propio en vez de SqlKata
- **Estado:** Vigente · 2026-10-27
- **Por qué:** las diferencias reales son pocas (delimitadores, paginación, ILIKE, inserción que devuelve claves, CAST de enums y JSON). Una interfaz chica (`ISqlDialect`) es fácil de auditar y de probar con SQL exacto.
- **Alternativa descartada:** SqlKata (una dependencia más y menos control sobre casos como `OUTPUT … INTO`).

### D-003 · Un proveedor por motor + un ensamblador común
- **Estado:** Vigente · 2026-10-27
- **Qué:** cada motor implementa `IDatabaseProvider` (conexión, `ISqlDialect`, `ISchemaProvider`) y solo devuelve filas crudas del catálogo. `SchemaAssembler` (en Core) arma PK, FK salientes y entrantes, y la columna a mostrar.
- **Por qué:** agregar MySQL u Oracle es escribir consultas de catálogo y un dialecto, sin tocar Core ni la API.
- **Archivos:** `backend/src/Mantenedores.Core/Providers/`, `Schema/SchemaAssembler.cs`.

### D-004 · Seguridad del SQL generado
- **Estado:** Vigente · 2026-10-27 · **No negociable**
- **Qué:**
  1. Tablas y columnas solo se aceptan si existen en la metadata leída.
  2. Todo identificador pasa por `ISqlDialect.Quote` (escapa `]` o `"`).
  3. Todo valor va como parámetro `@pN`.
  4. El patrón de LIKE escapa `\`, `%`, `_` y `[`.
- **Archivos:** `backend/src/Mantenedores.Core/Query/QueryBuilder.cs`. Probado en `QueryBuilderTests`.

### D-005 · Tipo lógico por columna
- **Estado:** Vigente · 2026-10-27
- **Qué:** cada tipo nativo se traduce a un `LogicalType` (String, Text, Integer, Decimal, Boolean, Date, DateTime, Time, Uuid, Json, Enum, Binary, Unsupported). Define el control de la UI y la conversión de valores.
- **Detalle:** `Binary` y `Unsupported` no se incluyen en los SELECT, para no traer blobs ni tipos que el driver no sabe leer (p. ej. `geography`).

### D-006 · La clave de la fila va en la query string
- **Estado:** Vigente · 2026-10-27
- **Qué:** `/row?Id=5` o `/row?PedidoId=1&Linea=2`.
- **Por qué:** sirve igual para claves simples y compuestas, sin codificar varios valores en un solo segmento de la ruta.

### D-007 · Etiquetas de FK con LEFT JOIN
- **Estado:** Vigente · 2026-10-27
- **Qué:** el listado agrega un `LEFT JOIN` a la tabla referenciada y devuelve `<columna>__label`. Solo para FK de una columna y cuando la columna a mostrar es distinta de la clave.
- **Por qué:** una sola consulta por página, en vez de una búsqueda de etiquetas por fila.

### D-008 · Heurística para la columna a mostrar
- **Estado:** Vigente · 2026-10-27 (se podrá corregir con overrides, P-011)
- **Orden de preferencia:**
  1. Nombres típicos (`nombre`, `descripcion`, `razon_social`, `codigo`…), exactos y luego parciales.
  2. Primera columna de texto corto que no sea clave.
  3. La PK.
  4. Un texto largo.
- **Por qué:** un texto largo (p. ej. `observacion`) es una mala etiqueta; la PK es mejor que eso.

### D-009 · Qué se puede editar en la Fase 1
- **Estado:** Temporal · 2026-10-27
- **Qué:**
  - Las vistas y las tablas sin PK son de solo lectura.
  - La PK no se modifica en una edición.
  - Identity, columnas calculadas y `rowversion` nunca se escriben.
- **Se revisa con:** PK virtual (P-011) y edición de vistas actualizables (sin fecha).

### D-010 · En SQL Server, INSERT con `OUTPUT … INTO` una variable de tabla
- **Estado:** Vigente · 2026-10-27
- **Por qué:** un `OUTPUT` directo falla si la tabla tiene triggers, lo que es común en bases SQL Server existentes. Probado con `ventas.Producto` y su trigger en `samples/`.

### D-011 · Conexiones en un archivo JSON cifrado
- **Estado:** Temporal · 2026-10-27 → se reemplaza con P-010 (metadata store) y luego un vault (P-045)
- **Qué:** `data/connections.json`, con la cadena cifrada por ASP.NET Data Protection. Las llaves van en `data/keys/`.
- **Riesgo:** si se pierden las llaves, hay que volver a ingresar las conexiones.

### D-012 · Login básico con usuarios en configuración
- **Estado:** Temporal · 2026-10-27 → se reemplaza con P-040 y P-041 (usuarios en BD + SSO)
- **Qué:**
  - Hash PBKDF2-SHA256 con 210.000 iteraciones y JWT HS256.
  - La API no arranca si `Auth:JwtKey` tiene menos de 32 caracteres.
  - Para generar un hash: `dotnet run --project backend/src/Mantenedores.Api -- hash-password "clave"`.

### D-013 · Caché del esquema en memoria
- **Estado:** Temporal · 2026-10-27 → se reemplaza con P-050 (Redis)
- **Qué:** se lee al primer uso y se guarda por conexión. Si la lectura falla, no se guarda el error. El esquema lleva un `hash` para detectar cambios (base de P-013).

### D-014 · Frontend sin librerías de formularios ni de tablas
- **Estado:** Vigente · 2026-10-27
- **Qué:** React + Vite + TanStack Query + React Router. Grilla y formulario propios.
- **Por qué:** los controles se generan desde la metadata, y un estado simple alcanzó. Se instaló `react-hook-form` y se quitó por no aportar. Se puede revisar si la grilla necesita virtualización (P-053).
- **Alternativa descartada (por ahora):** TanStack Table y react-hook-form, que estaban en el plan inicial.

### D-015 · Una sola app desplegable
- **Estado:** Vigente · 2026-10-27
- **Qué:** la API sirve el frontend compilado desde `wwwroot` y cualquier ruta que no es de la API va a `index.html`. Una ruta `/api/...` desconocida devuelve 404.
- **Imagen:** `Dockerfile` multi-etapa (node → dotnet publish → aspnet).

### D-016 · Errores del motor como 409 con el mensaje original
- **Estado:** Temporal · 2026-10-27 → se mejora con P-003
- **Qué:** las violaciones de FK, únicos o CHECK y los errores de triggers se devuelven como ProblemDetails 409 para que el usuario sepa qué pasó. La validación propia devuelve 400 con los errores por campo.

### D-017 · Reglas de conversión de valores
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - Un texto vacío en un campo que no es texto se interpreta como nulo.
  - Un nulo en una columna con default se omite al insertar, para que la BD aplique su valor.
  - Las fechas con zona se normalizan a UTC.
  - Los enteros se envían con su tamaño exacto (int2/int4/int8), porque Postgres es estricto.
- **Archivo:** `backend/src/Mantenedores.Core/Query/ValueConverter.cs`.

### D-018 · Tope de página de 200 filas
- **Estado:** Vigente · 2026-10-27
- **Por qué:** protege a la BD destino de listados enormes. Las exportaciones grandes irán por streaming (P-017).

## Proceso

### D-019 · Flujo de git
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - `main` es la rama por defecto y protegida por convención.
  - Todo cambio va en una rama `feature/...`, `fix/...` o `docs/...` y se integra con un PR.
  - No se reescribe historia publicada; la sesión remota además bloquea el force-push.

### D-020 · Idioma
- **Estado:** Vigente · 2026-10-27
- **Qué:** español en la UI, los mensajes de error, la documentación, los comentarios, los commits y los PR. Los identificadores de código van en inglés (convención de .NET y React).

### D-021 · Memoria del proyecto en archivos Markdown
- **Estado:** Vigente · 2026-10-27
- **Qué:** `CLAUDE.md` en la raíz obliga a leer `docs/memoria/` antes de cada tarea y a actualizarla al terminar.
- **Por qué:** el trabajo se retoma en sesiones distintas y sin contexto previo; la memoria evita volver a discutir lo ya decidido.

---

## Propuestas (aún no decididas)

Se convierten en `D-xxx` al aprobarse.

- **PR-01 · Migraciones del metadata store con DbUp** (scripts SQL versionados). Encaja con Dapper; la alternativa es EF Core Migrations. Ver P-010.
- **PR-02 · Pruebas de integración con Testcontainers** contra Postgres 16 y SQL Server 2022 en la CI. Ver P-002.
- **PR-03 · Overrides guardados como JSON** por tabla y por columna (flexibles, sin migrar el esquema por cada ajuste nuevo). Ver P-011.
