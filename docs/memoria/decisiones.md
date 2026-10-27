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
- **Estado:** Vigente · 2026-10-27 (se corrige por tabla con el ajuste `displayColumn`, D-049)
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
- **Se revisa con:** edición de vistas actualizables (sin fecha). Las tablas sin PK ya se editan con una PK virtual (D-049).

### D-010 · En SQL Server, INSERT con `OUTPUT … INTO` una variable de tabla
- **Estado:** Vigente · 2026-10-27
- **Por qué:** un `OUTPUT` directo falla si la tabla tiene triggers, lo que es común en bases SQL Server existentes. Probado con `ventas.Producto` y su trigger en `samples/`.

### D-011 · Conexiones en un archivo JSON cifrado
- **Estado:** Reemplazada por D-023/D-047 · 2026-10-27 (queda como `Metadata:Provider=File` para desarrollo)
- **Qué:** `data/connections.json`, con la cadena cifrada por ASP.NET Data Protection. Las llaves van en `data/keys/`.
- **Riesgo:** si se pierden las llaves, hay que volver a ingresar las conexiones.

### D-012 · Login básico con usuarios en configuración
- **Estado:** Vigente como modo `Local` de D-024 · 2026-10-27 (los usuarios pasan al metadata store con P-040)
- **Qué:**
  - Hash PBKDF2-SHA256 con 210.000 iteraciones y JWT HS256.
  - La API no arranca si `Auth:JwtKey` tiene menos de 32 caracteres.
  - Para generar un hash: `dotnet run --project backend/src/Mantenedores.Api -- hash-password "clave"`.

### D-013 · Caché del esquema en memoria
- **Estado:** Vigente como valor por defecto · 2026-10-27 (Redis es opcional, D-026)
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
- **Estado:** Vigente solo para errores no reconocidos · 2026-10-27 (los de restricciones los traduce D-038)
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

## Entorno y adaptabilidad (respuestas del dueño, 2026-10-27)

### D-022 · Se adapta a cualquier entorno; todo por configuración
- **Estado:** Vigente · 2026-10-27 · **Principio rector**
- **Qué:** el servicio se despliega igual en un servidor propio o en cualquier nube, sin depender de servicios de un proveedor. Cada pieza de infraestructura está detrás de una interfaz y se elige por configuración: metadata, autenticación, llaves, caché, secretos y motores destino.
- **Valores por defecto:** sin configurar nada arranca **sin dependencias externas** (un contenedor + un volumen `data/`).
- **Consecuencias:**
  - Nada de SDKs de nube como dependencia obligatoria; si se agregan, van como paquetes opcionales.
  - Toda clave nueva se documenta en `docs/configuracion.md` con su valor por defecto y su variable de entorno equivalente.
  - Publicable como contenedor, IIS, servicio de Windows, systemd o Kubernetes, y detrás de cualquier proxy (`PathBase`, `ForwardedHeaders`).
- **Alternativa descartada:** elegir una nube (Azure) y usar sus servicios administrados, que era la sugerencia inicial.

### D-023 · Metadata store de motor configurable
- **Estado:** Vigente e implementada · 2026-10-27 (P-010, PR #10; detalle en D-047)
- **Qué:** `Metadata:Provider` = `Sqlite` (por defecto) · `Postgres` · `SqlServer`. Scripts de migración por motor (con un migrador propio, no DbUp: ver D-047).
- **Por qué:**
  - SQLite permite una instalación de un solo contenedor sin instalar otra base.
  - Postgres o SQL Server se usan cuando hay varias instancias o el equipo ya respalda sus bases ahí. Cada equipo usa la que tiene.
- **Alternativa descartada:** obligar a usar PostgreSQL (plan inicial).

### D-024 · Autenticación libre: modo `Local` o cualquier emisor JWT/OIDC
- **Estado:** Vigente e implementada · 2026-10-27 (P-041, PR #9; detalle en D-046)
- **Qué:**
  - `Auth:Mode = Local`: lo actual (D-012).
  - `Auth:Mode = Jwt`: valida tokens de cualquier emisor, como Duende IdentityServer, Keycloak, Entra ID, Auth0 u otro.
    - Por descubrimiento OIDC (`Authority` + `Audience`) o con llave o JWKS fija.
    - Los claims de nombre y de rol se mapean por configuración.
- **UI:**
  - Lee `GET /api/auth/config`.
  - Si hay `ClientId`, hace login con Authorization Code + PKCE; si no, asume que un gateway entrega el token.
- **Por qué:** cada equipo ya tiene su proveedor de identidad; la app no debe imponer uno ni administrar usuarios si existe uno externo.
- **Alternativa descartada:** integrar un proveedor concreto (Entra ID o Google).

### D-025 · Llaves y secretos desde el entorno
- **Estado:** Vigente · 2026-10-27 (P-045)
- **Qué:**
  - `DataProtection:KeyStore` = `FileSystem` (por defecto) · `Database`, con certificado opcional para cifrar las llaves.
  - Las cadenas de conexión aceptan referencias `${env:VAR}` y `${file:/ruta}`, para usar Docker/Kubernetes secrets o variables.
  - Los vaults específicos van como adaptadores opcionales.
- **Alternativa descartada:** vault obligatorio (Azure Key Vault o HashiCorp Vault).

### D-026 · Redis opcional
- **Estado:** Vigente · 2026-10-27 (P-050)
- **Qué:** `Cache:Provider` = `Memory` (por defecto) · `Redis`. Solo hace falta con varias instancias.

### D-027 · Un solo equipo interno, sin multi-tenant
- **Estado:** Vigente · 2026-10-27
- **Qué:** no se agrega `tenant_id`. Si otro equipo lo necesita, instala su propia copia con su configuración.
- **Por qué:** simplifica el modelo de datos y los permisos; D-022 hace barato instalar varias copias.

### D-028 · Los permisos se asocian a nombres de rol
- **Estado:** Vigente · 2026-10-27 (P-042)
- **Qué:** las reglas de permisos (RBAC) guardadas en la metadata apuntan a nombres de rol. Esos roles salen del claim configurado (modo `Jwt`) o de los usuarios locales (modo `Local`).
- **Por qué:** funciona igual con cualquier proveedor de identidad (consecuencia de D-024).

## Motores soportados (pedido del dueño, 2026-10-27)

### D-029 · Cuatro familias de motores desde la v1
- **Estado:** Reemplazada por D-034 · 2026-10-27 (Oracle quedó fuera)
- **Qué:** SQL Server 2016+, PostgreSQL 12+, MySQL 8.0+ / MariaDB 10.6+ y Oracle 19c+.
- **Cuándo:** se implementan en el bloque **M**, antes de la Fase 2.
- **Por qué:** overrides, maestro-detalle, import/export y permisos tocan el SQL generado. Diseñarlos con dos motores y sumar otros después obligaría a rehacerlos (deuda técnica).
- **Versiones viejas:** fuera de soporte del fabricante, no se soportan; se rechazan con un mensaje claro (P-028).
- **Alternativa descartada:** dejar MySQL y Oracle para la Fase 5 (plan anterior).
- **Detalle técnico:** `docs/motores.md`.

### D-030 · Drivers
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - `MySqlConnector` (MIT, async real) para MySQL y MariaDB, con un solo proveedor.
  - ~~`Oracle.ManagedDataAccess.Core` para Oracle~~: no aplica mientras Oracle esté fuera de alcance (D-034).
- **Alternativa descartada:** `MySql.Data`, el driver oficial de Oracle para MySQL: licencia GPL y async limitado.

### D-031 · El dialecto absorbe las diferencias, no Core
- **Estado:** Vigente · 2026-10-27 (se implementa en P-020 a P-024)
- **Qué:** `ISqlDialect` declara:
  - Prefijo de parámetros.
  - Estrategia de inserción: filas, último id o valores provistos.
  - Carácter de escape de LIKE (`!`).
  - Capacidades por columna: ordenable, filtrable, buscable.
  - Ajustes de sesión al abrir la conexión.
- **Además:** las etiquetas de FK usan alias cortos (`l0`…) para no chocar con el límite de 63 bytes de Postgres.
- **Actualizado con D-034:** se quitaron "parámetros de salida" y "`''` = NULL", que solo hacían falta para Oracle.
- **Regla:** en Core no puede haber `if (motor == X)`. Si un motor necesita algo distinto, se expresa como capacidad del dialecto.
- **Alternativa descartada:** ramas por motor dentro de `QueryBuilder`.

### D-032 · Qué es un "esquema" en cada motor
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - En MySQL/MariaDB, el "esquema" es la base de datos. Por defecto se expone solo la de la cadena de conexión.
  - En MySQL/MariaDB se pueden listar más bases en la configuración de la conexión.
  - (Oracle, si se retoma: el "esquema" sería el owner; ver `docs/motores.md`.)

### D-034 · Tres familias de motores: Oracle queda fuera
- **Estado:** Vigente · 2026-10-27 · reemplaza a D-029
- **Qué:** la v1 soporta SQL Server 2016+, PostgreSQL 12+ y MySQL 8.0+ / MariaDB 10.6+. **Oracle queda fuera de alcance** por decisión del dueño.
- **Se mantiene de D-029:** MySQL/MariaDB se implementan en el bloque **M**, antes de la Fase 2, y las versiones fuera de soporte se rechazan (P-028).
- **Para no cerrar la puerta:** el prefijo de parámetros igual pasa al dialecto (P-020) y el análisis técnico de Oracle queda en `docs/motores.md`. Retomarlo sería agregar un proveedor y una estrategia de inserción, sin rehacer nada (estimado: 1,5 a 2 semanas).
- **Efecto en el plan:** el bloque M baja de 4 a 2,5 semanas; el total, a 17–18 semanas.

### D-033 · Un motor está soportado si pasa la batería común
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - Las pruebas de integración se escriben una vez como clase base y cada proveedor la hereda con su contenedor y su base de ejemplo.
  - La CI corre una matriz con un job por motor.
  - Un cambio en Core o en un proveedor no se fusiona si algún motor falla.
- **Por qué:** es lo que evita que un motor quede "soportado en el papel".

### D-035 · Versiones mínimas confirmadas y probadas
- **Estado:** Vigente · 2026-10-27 (responde Q-008)
- **Qué:** se trabaja con **SQL Server 2016, PostgreSQL 12, MySQL 8.0 y MariaDB 10.6** como mínimos, publicados en el `README.md` ("Requisitos de instalación") para que toda instalación los verifique.
- **Cómo se prueban:**
  - La batería común corre siempre contra la versión **mínima** y contra una **actual** de cada motor (matriz de la CI).
  - Por defecto, las pruebas locales usan la mínima, porque es la que más casos rompe.
- **SQL Server 2016:** no tiene imagen Linux. Se prueba con 2017 y no se usa T-SQL posterior a 2016 (lista en `docs/motores.md`).
- **Ya dio frutos:** la base de ejemplo de Postgres no funcionaba en PG 12 (`gen_random_uuid()` sin `pgcrypto`).

### D-036 · Fechas y horas con el mismo formato en todos los motores
- **Estado:** Vigente · 2026-10-27
- **Qué:** `CrudService` normaliza la respuesta: Date → `DateOnly` (`"AAAA-MM-DD"`) y Time → `TimeOnly` (`"HH:MM:SS"`).
- **Por qué:** cada driver usa tipos distintos (Npgsql 10: `DateOnly`/`TimeOnly`; SqlClient: `DateTime`/`TimeSpan`), y la API devolvía formatos diferentes según el motor. Lo detectó la batería común.
- **Alternativa descartada:** dejar que cada proveedor devuelva lo que entrega su driver, y que el frontend lo corrija.

### D-037 · La subruta se resuelve al servir el index.html, no al compilar
- **Estado:** Vigente · 2026-10-27 (P-007)
- **Qué:**
  - El frontend se compila con `base: './'` y con `<base href="/" />` en `index.html`.
  - La API sirve `index.html` reemplazando esa base por `Hosting:PathBase` (p. ej. `/mantenedores/`).
  - Las llamadas a la API son relativas (`api/...`) y el router toma su `basename` de `document.baseURI`.
- **Por qué:** **un mismo build** sirve en la raíz o en cualquier subruta, sin recompilar por entorno (D-022).
- **Detalle técnico:** `app.UseRouting()` debe ir **después** de `UsePathBase`; si no, el enrutamiento implícito compara la ruta completa y todo cae en el `index.html`. Lo detectó `HostingTests`.
- **Alternativa descartada:** fijar la base al compilar (`vite build --base=/mantenedores/`), que obliga a un build por instalación.

### D-038 · Errores de restricciones: códigos del motor + metadata
- **Estado:** Vigente · 2026-10-27 (P-003)
- **Qué:**
  - Cada proveedor clasifica el error **por código**: SQLSTATE en Postgres; número de error en SQL Server (2627, 2601, 547, 515). Para el nombre de la restricción, SQL Server solo da el texto entre comillas.
  - `DbErrorTranslator` (Core) busca ese nombre en la PK, los UNIQUE y las FK de la tabla, y arma un mensaje con el campo afectado.
  - La API responde 409 con `detail` (mensaje) y `errors` (por campo).
  - Si no se reconoce, queda el mensaje original (D-016).
- **Casos:** duplicado → columna; FK al insertar → columna y tabla referenciada; FK al borrar → tabla hija que lo usa; CHECK → nombre de la regla; nulo → columna.
- **Por qué:** el texto de los errores cambia con el idioma del servidor; los códigos y los nombres de las restricciones no.
- **Detalle:** el 547 de SQL Server cubre FK y CHECK. Se clasifica como `ForeignKeyOrCheck` y se resuelve según si el nombre es una FK conocida.
- **Alternativa descartada:** buscar palabras en el mensaje ("FOREIGN KEY", "duplicate"), que falla con servidores en otro idioma.

### D-039 · La app se ejecuta bajo cualquier administrador de servicios
- **Estado:** Vigente · 2026-10-27 (P-004)
- **Qué:** `UseWindowsService()` y `UseSystemd()`, que no hacen nada si no aplican. Una `Storage:DataDirectory` relativa se resuelve contra la carpeta de la app.
- **Por qué:** D-022. Como servicio de Windows, el directorio de trabajo es `C:\Windows\System32` y los datos terminaban ahí.
- **Recetas:** `docs/despliegue.md` (Docker, Compose, Kubernetes, nginx, IIS, servicio de Windows, systemd, respaldo y permisos mínimos por motor).

## Bloque M

### D-040 · Cómo se obtiene la clave de una fila recién creada
- **Estado:** Vigente · 2026-10-27 (P-020)
- **Qué:** `QueryBuilder.BuildInsert` devuelve un `InsertQuery` y prueba en este orden:
  1. El dialecto la devuelve como filas (`InsertReturningKeys`: SQL Server, PostgreSQL).
  2. PK de una sola columna autonumérica + `LastInsertIdExpression` (MySQL/MariaDB).
  3. La PK vino completa en el alta (`ProvidedKeys`).
  4. Si no se cumple ninguna, se pide el valor de la clave al usuario.
- **Además:** el prefijo de parámetros (`@`, `:`) vive en el dialecto.
- **Por qué:** MySQL no tiene `RETURNING`; MariaDB sí, pero usar `LAST_INSERT_ID()` en ambos evita depender de la versión.
- **Alternativa descartada:** detectar la versión de MariaDB para usar `RETURNING` (más ramas, sin beneficio práctico).

### D-041 · Valores que se leen como texto
- **Estado:** Vigente · 2026-10-27 (P-023)
- **Qué:** `ISqlDialect.SelectExpression` permite transformar la lectura de una columna. Se leen como texto:
  - **Números exactos de más de 28 dígitos o sin precisión** (`IsLargeNumber`, lo marca el proveedor). No caben en `decimal` de .NET. Se escriben como texto validado; en PG con `CAST(@p AS numeric)`.
  - **Enums de PostgreSQL.** Npgsql guarda los tipos en caché al conectarse por primera vez, y un enum creado después no se puede leer. Lo detectó la batería común.
- **Los alias de las etiquetas de FK son cortos** (`__lbl0`) y la respuesta los renombra a `<columna>__label` (P-021). Evita el corte de 63 bytes de PostgreSQL.
- **Alternativa descartada:** recargar los tipos de Npgsql (`ReloadTypes`) en cada lectura del esquema, que es más frágil y no cubre los tipos creados entre recargas.

### D-042 · Capacidades por columna
- **Estado:** Vigente · 2026-10-27 (P-022)
- **Qué:** el proveedor marca `Sortable` y `Filterable` en cada columna. `ColumnInfo` expone `IsSortable` e `IsFilterable`; los textos siempre admiten "contiene".
  - El `QueryBuilder` rechaza ordenar o filtrar por una columna no apta; los combos se ordenan por la PK si la columna a mostrar no es ordenable.
  - La UI no ofrece esas opciones.
- **Casos de hoy:** SQL Server `text`, `ntext`, `image`, `xml`; PostgreSQL `json`, `xml`.

### D-043 · La versión mínima se verifica al conectar
- **Estado:** Vigente · 2026-10-27 (P-028)
- **Qué:**
  - `IDatabaseProvider.GetServerInfoAsync` devuelve el producto, la versión y ajustes útiles (intercalación, codificación, zona).
  - La prueba de conexión los muestra y avisa si la versión es menor que la mínima (D-035).
  - Al leer el esquema, un servidor no soportado da `UnsupportedServerException` (400) con el mínimo y un enlace a los requisitos.
- **Además:** `InitializeSessionAsync` permite ajustes de sesión al abrir cada conexión (en MySQL: zona UTC).

### D-044 · UUID de la PK generado por la app cuando el motor no puede devolverlo
- **Estado:** Vigente · 2026-10-27 (complementa D-040)
- **Qué:** si el motor no devuelve la clave (sin `RETURNING`) y la PK es un UUID con default (p. ej. MySQL `CHAR(36) DEFAULT (UUID())`), Core genera un `Guid` y lo envía en el INSERT. Para otros tipos de PK con default, se pide el valor.
- **Por qué:** es la única forma de conocer la clave nueva en MySQL sin consultas adicionales frágiles. El resultado es equivalente al default de la BD.
- **Alternativa descartada:** consultar la fila recién creada buscándola por sus otros valores (ambiguo si hay duplicados).

### D-045 · Particularidades de MySQL/MariaDB resueltas en su proveedor
- **Estado:** Vigente · 2026-10-27 (P-025)
- **Qué:**
  - Un solo `MySqlProvider` para ambos; la prueba de conexión distingue "MySQL" de "MariaDB" y aplica su mínimo (8.0 o 10.6).
  - Sesión en UTC (`SET time_zone = '+00:00'`); `TIMESTAMP` se marca con zona y se devuelve como UTC.
  - `ConvertZeroDateTime` forzado.
  - `CHAR(36)` → Uuid; `TINYINT(1)`/`BIT(1)` → Boolean; `BIGINT UNSIGNED` → Decimal.
  - Tipo nativo `tinyint signed` / `… unsigned`, para no aplicar rangos de SQL Server.
  - JSON de MariaDB detectado por su CHECK `json_valid`.
  - Números grandes con `CAST(@p AS DECIMAL(65,30))` en la escritura, porque MySQL compararía texto contra DECIMAL como DOUBLE.
  - Errores por número: 1062, 1451, 1452, 3819, 4025, 1048, 1364.
- **Por qué:** D-031: todo lo propio del motor vive en su proveedor; Core no cambió salvo D-044 y la normalización a UTC.

## Fase 2

### D-046 · Cómo se implementa la autenticación configurable
- **Estado:** Vigente · 2026-10-27 (P-041)
- **Qué:**
  - `AuthSetup` configura JwtBearer según `Auth:Mode`. En modo `Jwt` la fuente de llaves es `Authority` (OIDC), `MetadataAddress` o `SigningKey`. La configuración se valida al arrancar con mensajes que dicen qué falta, y `Auth:JwtKey` solo se exige en modo `Local` (resuelve P-039).
  - **Claims normalizados:** al validar el token, `ClaimsMapper` agrega `ClaimTypes.Name` y `ClaimTypes.Role` según `NameClaim` y `RoleClaim`. Admite claims repetidos, arreglos JSON y rutas anidadas (`realm_access.roles`). El resto de la app no conoce el formato de cada emisor. Se usa `MapInboundClaims = false` para leer los nombres originales.
  - Política `admin` = algún rol en `Auth:AdminRoles`. Los usuarios locales tienen `Roles`.
  - `GET /api/auth/config` (anónimo, sin secretos) le dice a la UI cómo iniciar sesión; `GET /api/auth/me` devuelve nombre, roles y si es admin. El login local se desactiva (404) en modo `Jwt`.
  - **UI:** con `ClientId`, Authorization Code + PKCE con `oidc-client-ts`. El token queda en memoria y en `sessionStorage`, manejado por la librería, **no en `localStorage`**. Sin `ClientId` se asume un proxy que autentica y no hay login ni botón de salir.
  - El `redirect_uri` se arma con `<base href>`, así funciona bajo subruta (D-037).
- **Alternativas descartadas:**
  - Mapear roles con `ClaimActions` por proveedor, que exige código por emisor.
  - Guardar el token OIDC en `localStorage`, más expuesto ante XSS.
- **Probado:** con un emisor HS256 en memoria (`AuthTests`) y con Keycloak 26 real (realm en `samples/keycloak`): login PKCE en el navegador, roles anidados y cierre de sesión. **Duende IdentityServer no se probó en vivo** (P-078); su configuración es OIDC estándar y está documentada.

### D-047 · Metadata en un proyecto propio con un migrador mínimo
- **Estado:** Vigente · 2026-10-27 (P-010)
- **Qué:**
  - `Mantenedores.Metadata` contiene:
    - `SqlConnectionStore`: SQL portable (UPDATE y, si no hubo fila, INSERT; ids en minúsculas).
    - `MetadataMigrator`: scripts embebidos `Migrations/<Motor>/NNN_nombre.sql`, registro en `mnt_migration` y una transacción por script.
    - `DatabaseXmlRepository`: llaves de Data Protection en `mnt_dataprotection_key`.
    - `LegacyConnectionImporter` y `MetadataSetup`.
  - Las tablas llevan el prefijo `mnt_`.
  - Las migraciones corren al arrancar, antes de atender solicitudes.
  - SQLite en modo WAL.
  - `FileConnectionStore` se movió al mismo proyecto y queda como `Metadata:Provider=File`.
- **Por qué:** cubre lo que hacía falta de DbUp (orden, registro, transacción) en unas 60 líneas, sin sumar cuatro paquetes que dependen de versiones de Npgsql y SqlClient distintas de las que ya se usan.
- **Alternativa descartada:** DbUp (propuesta PR-01) y EF Core Migrations.

### D-048 · Paso de la Fase 1 a la metadata sin intervención
- **Estado:** Vigente · 2026-10-27 (P-010)
- **Qué:** al arrancar, si existe `data/connections.json` y la tabla está vacía, se importa una sola vez:
  - Se descifra con las llaves de `data/keys` y se vuelve a cifrar con las llaves activas, que pueden estar en la base.
  - Después el archivo se renombra a `connections.json.importado`.
- **Por qué:** quien ya usaba la app no pierde sus conexiones al actualizar, aunque cambie de almacén de llaves.

### D-049 · Ajustes: un documento JSON por tabla, aplicado sobre el esquema leído
- **Estado:** Vigente · 2026-10-27 (P-011; era la propuesta PR-03)
- **Qué:**
  - `TableOverride` (con sus `ColumnOverride`) se guarda como **un JSON por tabla** en `mnt_table_override` (clave: conexión, esquema, tabla), o en `data/overrides.json` con `Metadata:Provider=File`.
  - `OverrideApplier` (Core, puro) combina el esquema del catálogo con los ajustes y devuelve un `DatabaseSchema` nuevo: etiquetas, orden, ocultas, solo lectura, obligatorias, reglas, PK y FK virtuales, columna a mostrar y orden por defecto. Recalcula las FK entrantes.
  - `SchemaService` guarda **dos cachés**: el esquema leído (caro) y el combinado (barato). Guardar un ajuste solo invalida el combinado; no vuelve a leer la base. El hash sigue siendo el del catálogo (base de P-013).
  - Lo que no se puede aplicar se omite y queda en `OverrideWarnings`; al guardar, los mismos problemas se devuelven como errores por campo (`columns.email.pattern`). Así un cambio en la base nunca rompe el mantenedor.
  - Las reglas de escritura (`required`, `pattern`, `options`, `min`, `max`, `maxLength`) se aplican en `ValueConverter.ForWrite`, solo en altas y ediciones; los filtros no las usan.
  - `pattern` se evalúa sobre el texto completo y con 100 ms de tiempo máximo (evita ReDoS).
- **Por qué:** sumar un ajuste nuevo no exige migrar la metadata; el documento es el mismo JSON de la API y del futuro editor (P-012).
- **Alternativas descartadas:**
  - Una fila por ajuste o por columna: más tablas y migraciones por cada ajuste nuevo.
  - Aplicar los ajustes en la UI: las reglas y las columnas ocultas deben cumplirse en el servidor.

### D-050 · Solo los administradores cambian los ajustes (por ahora)
- **Estado:** Provisional · 2026-10-27 (se confirma o cambia con Q-006)
- **Qué:** `PUT` y `DELETE` de `/api/meta/{conn}/overrides/...` exigen la política `admin` (`Auth:AdminRoles`, D-046). Leerlos lo puede hacer cualquier usuario autenticado, porque la UI los necesita para dibujar.
- **Por qué:** es la sugerencia de Q-006 y no requiere nada nuevo. En la Fase 3 puede pasar a un permiso por conexión (P-042).

### D-051 · Qué significa "oculto"
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - **Columna oculta:** no se lee, no se escribe ni se busca por la API (sirve para datos sensibles). Si es PK, se sigue usando como clave y solo la UI la esconde. Ocultar se aplica aunque el resto del ajuste tenga avisos.
  - **Tabla oculta:** solo sale del menú. **No es un permiso**; restringir el acceso es tarea de P-042.
  - Una PK virtual en una vista permite leer por clave y usarla en combos, pero la vista sigue siendo de solo lectura (D-009).
- **Alternativa descartada:** que una tabla oculta responda 404, que daría una falsa sensación de seguridad.

---

## Propuestas (aún no decididas)

Se convierten en `D-xxx` al aprobarse. Por ahora no hay.
