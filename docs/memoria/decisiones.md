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
- **Estado:** Reemplazada por D-053 · 2026-10-27
- **Qué:** `PUT` y `DELETE` de `/api/meta/{conn}/overrides/...` exigen la política `admin` (`Auth:AdminRoles`, D-046). Leerlos lo puede hacer cualquier usuario autenticado, porque la UI los necesita para dibujar.
- **Por qué:** es la sugerencia de Q-006 y no requiere nada nuevo. En la Fase 3 puede pasar a un permiso por conexión (P-042).

### D-051 · Qué significa "oculto"
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - **Columna oculta:** no se lee, no se escribe ni se busca por la API (sirve para datos sensibles). Si es PK, se sigue usando como clave y solo la UI la esconde. Ocultar se aplica aunque el resto del ajuste tenga avisos.
  - **Tabla oculta:** solo sale del menú. **No es un permiso**; restringir el acceso es tarea de P-042.
  - Una PK virtual en una vista permite leer por clave y usarla en combos, pero la vista sigue siendo de solo lectura (D-009).
- **Alternativa descartada:** que una tabla oculta responda 404, que daría una falsa sensación de seguridad.

### D-052 · Editor de ajustes con vista previa calculada en el servidor
- **Estado:** Vigente · 2026-10-27 (P-012)
- **Qué:**
  - `POST /api/meta/{conn}/overrides/{schema}/{table}/preview` aplica el borrador sin guardarlo, con los ajustes guardados de las demás tablas, y devuelve la tabla combinada **y** los errores por campo. Un error no corta la vista previa.
  - El editor (`ConfigPage`) arma el mismo JSON de D-049 y quita lo vacío, así lo no tocado sigue automático. El orden se guarda solo si se cambió, y entonces para todas las columnas.
  - El botón "Configurar" se muestra según `/api/auth/me`; es solo cosmético, porque el servidor exige `admin` (D-050).
  - Las FK virtuales se agregan de una columna desde la UI; las compuestas, por la API.
- **Por qué:** la vista previa usa el mismo `OverrideApplier` que la app, sin duplicar sus reglas en TypeScript.
- **Alternativa descartada:** aplicar los ajustes en el navegador, que obligaría a mantener las reglas en dos lugares.

### D-053 · Un permiso por acción sensible, otorgado a roles por configuración
- **Estado:** Vigente · 2026-10-27 (responde Q-006 y P-083; el dueño lo pidió así)
- **Qué:**
  - Permisos `ConfigureTables` (ajustes de mantenedores) y `ManageConnections` (crear, editar, probar y borrar conexiones). Ver y abrir conexiones no exige permiso.
  - Cada permiso se otorga a roles en `Auth:Permissions:<permiso>`. Por defecto: `mnt-configurador` y `mnt-conexiones`. Los `Auth:AdminRoles` tienen todos.
  - Una política por permiso (`perm:<permiso>`). `/api/auth/me` devuelve `permissions` y la UI muestra u oculta las acciones, pero quien decide es el servidor.
  - Un permiso desconocido en la configuración detiene el arranque.
- **Por qué:** el dueño quiere poder dar a una persona o perfil solo lo que necesita. Los roles ya vienen del proveedor de identidad o de los usuarios locales (D-028). El futuro apartado de seguridad (P-040/P-042) asignará estos mismos permisos a perfiles, sin cambiar los endpoints.
- **Alternativas descartadas:**
  - Seguir con "solo admin" (D-050): no permite delegar.
  - Nombres de rol fijos en el código: no se adaptan a los roles que ya existen en cada empresa (D-022).

### D-054 · Cambios de esquema contra una foto de referencia que se acepta
- **Estado:** Vigente · 2026-10-27 (P-013)
- **Qué:**
  - `SchemaSnapshot` es un modelo propio y estable (tablas, columnas con tipo y nulos, PK declarada y FK), guardado como JSON en `mnt_schema_snapshot` (migración `003`), una fila por conexión.
  - La primera lectura de una conexión crea la foto. Después, `GET /diff` compara la foto con el esquema leído, que es el de la caché; tras ↻ se lee de nuevo.
  - El aviso se mantiene hasta que alguien con `ConfigureTables` lo acepta (`POST /diff/accept`), y queda registrado quién lo hizo.
  - Los ajustes sin efecto se informan junto con los cambios; el editor permite quitar los de columnas que ya no existen.
  - Al borrar una conexión se borra su foto.
- **Por qué:**
  - Comparar contra "la última lectura" haría desaparecer el aviso al refrescar, sin que nadie lo revisara.
  - Un modelo propio evita que un cambio en `TableInfo` (p. ej. un campo nuevo) parezca un cambio de la base.
- **Alternativa descartada:** comparar solo el hash, que dice que algo cambió pero no qué.

### D-055 · Maestro-detalle en el formulario de edición, con filtro exacto
- **Estado:** Vigente · 2026-10-27 (P-014)
- **Qué:**
  - Al editar un registro, una pestaña por tabla hija (`TableInfo.Children`) con su grilla filtrada por la FK. Funciona con FK compuestas y virtuales.
  - Nuevo parámetro de listado `e.<columna>` (`ListRequest.Exact`): igualdad exacta, también en textos. `f.` sigue siendo "contiene" en textos. Una columna oculta solo se acepta en `e.` si es parte de una FK o la PK.
  - "Nuevo" y "Editar" desde una pestaña llevan `_fk.<col>` (valor fijo y bloqueado) y `_return` (a dónde volver); esos parámetros no son parte de la clave.
  - Un alta de un registro con hijas queda en su edición, para cargar las hijas sin salir.
  - Ajuste `children`: cuáles hijas y en qué orden, por nombre de FK. Se resuelve después de recalcular las FK entrantes, así incluye las virtuales; un nombre que ya no existe es aviso.
- **Por qué:** reusar la misma grilla evita una segunda implementación. El filtro "contiene" traería hijas de otro padre con un código parecido.
- **Alternativa descartada:** un endpoint propio de hijas (`/children`), que duplicaría paginación, orden y filtros.

### D-056 · Relaciones N:M con tabla puente detectada y sincronización en transacción
- **Estado:** Vigente · 2026-10-27 (P-015)
- **Qué:**
  - **Puente:** una tabla con exactamente dos FK de una columna, cuya PK (declarada o virtual) son esas dos columnas, sin otras columnas obligatorias y con PK de una columna en ambas puntas. Se detecta en `OverrideApplier`, con las FK y PK ya definitivas.
  - `TableInfo.ManyToMany` lista las relaciones de cada tabla (en ambos sentidos) y `IsBridge` marca el puente. El nombre de la relación es el del puente, o `puente.columna` si la tabla se relaciona consigo misma.
  - El puente no sale como pestaña de hija: lo reemplaza un selector múltiple en el formulario, que se guarda junto con el registro.
  - `GET /row/links/{relación}` devuelve `[{ value, label }]`. `PUT` recibe la lista completa, borra los quitados e inserta los nuevos en una transacción, con los mismos `BuildInsert`/`BuildDelete` del puente (seguridad y dialecto incluidos). Un valor inválido revierte todo.
- **Por qué:** la lista completa es idempotente y simple para la UI. Reusar los constructores del puente respeta solo lectura, ajustes y errores traducidos.
- **Alternativa descartada:** endpoints de agregar y quitar uno por uno, que dejan estados a medias si falla el segundo.

### D-057 · Exportar e importar en CSV, Excel, JSON y XML
- **Estado:** Vigente · 2026-10-27 (P-017; el dueño pidió sumar JSON y XML)
- **Qué:**
  - **Exportar:** `GET /export?format=` con los filtros, la búsqueda y el orden del listado, sin paginar. Primero cuenta: si pasa `Crud:ExportMaxRows`, responde un error antes de escribir. Después lee con `QueryUnbufferedAsync` y escribe fila por fila. Las columnas son las visibles y, al lado de cada FK, su texto (`x__label`). Los valores van en texto estable (fechas ISO 8601, punto decimal).
  - **Formatos:**
    - CSV: RFC 4180, UTF-8 con BOM. Un texto que empieza con `= + - @` va con apóstrofo, contra la inyección de fórmulas; al importar se quita.
    - Excel: OpenXml SDK en modo SAX sobre un archivo temporal. Los números de más de 15 dígitos van como texto.
    - JSON: arreglo de objetos con tipos JSON.
    - XML: `<rows><row><field name="…">`, con `null="true"` para los nulos.
  - **Importar:** `POST /import/preview` y `POST /import`, sin estado: el navegador vuelve a enviar el archivo.
    - Asociación automática por nombre o etiqueta, sin tildes, espacios ni mayúsculas, y corregible por el usuario.
    - Cada fila se valida con las mismas reglas del formulario (`ParseValues`). Los errores llevan línea y columna.
    - Se graba en una sola transacción: si la base rechaza una fila, no queda ninguna.
    - XML sin DTD ni entidades externas. Hay topes de filas y de MB.
- **Por qué:** importar los mismos cuatro formatos que se exportan permite exportar, corregir y volver a cargar. Sin estado en el servidor, funciona igual con varias instancias.
- **Alternativas descartadas:**
  - ClosedXML (arma todo el libro en memoria).
  - Guardar el archivo en el servidor entre la vista previa y la confirmación (exige almacenamiento compartido).

### D-058 · Árboles por autorreferencia, cargados por nivel, sin ciclos
- **Estado:** Vigente · 2026-10-27 (P-016)
- **Qué:**
  - `TableInfo.SelfReference` es la primera FK de una columna de la tabla hacia su propia PK de una columna, declarada o virtual.
  - `GET /tree?parent=` devuelve un nivel por llamada: `{ value, label, hasChildren }`, este último con `EXISTS`. Sin `parent` devuelve las raíces: sin padre o con un padre que ya no existe, así ningún registro queda inaccesible.
  - Al editar se sube desde el nuevo padre hacia la raíz con consultas simples; si se llega al propio registro, el cambio se rechaza en el campo. No se usa CTE recursivo porque su sintaxis cambia entre motores (D-031).
  - UI: cambio Grilla/Árbol (`?vista=arbol` en la URL, para volver a la misma vista) y "＋ hijo", que usa el valor fijo de P-014.
- **Por qué:** cargar por nivel escala con jerarquías grandes, y la validación vive en el servidor, que es donde se cumple.
- **Alternativa descartada:** cargar el árbol completo de una vez, que sería lento con miles de registros.

### D-059 · Archivos, arreglos y tipos que solo se muestran
- **Estado:** Vigente · 2026-10-27 (P-018)
- **Qué:**
  - **Tipos lógicos nuevos:** `Array` (con `ElementType` y su tipo nativo) y `ReadOnlyText`.
  - **Archivos (`Binary` sin rowversion):** el contenido no va en los listados. Va su tamaño (`<col>__size`, con `ByteLength` del dialecto). Se descarga con `GET /row/file/{col}`, que deduce el tipo por los primeros bytes. Se guarda en base64 dentro del mismo JSON del alta o la edición, con tope `Crud:FileMaxMegabytes`.
  - **Arreglos de PostgreSQL:** cada elemento se valida con el tipo del elemento. Viajan como texto (`CAST(@p AS <tipo>[])`) y se leen como `text[]`, porque Npgsql no lee un `int4[]` con nulos como `int[]` (bug encontrado por la batería). La UI los edita con un valor por línea.
  - **`ReadOnlyText`:** `geography`, `geometry` y `hierarchyid` de SQL Server, PostGIS y los tipos espaciales de MySQL. Se leen como texto con `SelectExpression` (WKT o ruta) y no se ordenan, filtran ni escriben.
- **Por qué:**
  - El archivo en el mismo JSON permite crear un registro con un archivo obligatorio sin un segundo paso.
  - El tamaño en el listado evita traer megabytes por fila.
  - Todas las diferencias quedan en el dialecto (D-031).
- **Alternativas descartadas:**
  - Un endpoint aparte para subir archivos: un archivo obligatorio exigiría dos pasos.
  - Editar `geography` como WKT: requiere validación espacial propia de cada motor.

### D-060 · Migraciones con bloqueo de sesión
- **Estado:** Vigente · 2026-10-28 (P-081)
- **Qué:**
  - Antes de migrar se toma un bloqueo de sesión: `pg_advisory_lock` en Postgres y `sp_getapplock` en SQL Server. Se libera al terminar o al cortarse la conexión.
  - Dentro de cada transacción se revisa otra vez si el script ya se aplicó. Eso cubre también a SQLite, donde la transacción de escritura bloquea el archivo.
- **Por qué:** varias réplicas que arrancan juntas chocaban creando las mismas tablas. La prueba lo reproduce sin el bloqueo (clave duplicada en `pg_type`).
- **Alternativa descartada:** una tabla de "candado" propia, que queda tomada si el proceso muere.

### D-061 · Referencias a secretos en las cadenas de conexión
- **Estado:** Vigente · 2026-10-28 (P-045, completa D-025)
- **Qué:**
  - `${env:NOMBRE}` y `${file:nombre}` se reemplazan al conectar (`SchemaService.ConnectionStringFor`). En la metadata queda la referencia.
  - Solo se aceptan variables con prefijo `Secrets:EnvPrefix` (`MNT_`) y archivos dentro de `Secrets:FilesDirectory` (`/run/secrets`).
  - Los errores nombran la referencia, nunca el valor.
- **Por qué:** quien gestiona conexiones no debe poder leer cualquier variable (p. ej. la llave JWT) ni cualquier archivo del servidor.
- **Alternativa descartada:** resolver al guardar, que dejaría el secreto en la metadata.

### D-062 · Usuarios: del proveedor de identidad y propios
- **Estado:** Vigente · 2026-10-28 (responde a la pregunta del dueño sobre el apartado de seguridad; P-040)
- **Qué:**
  - La app tiene **perfiles** propios.
  - A un perfil se le asocian roles del proveedor de identidad (modo `Jwt`) y usuarios propios guardados en la metadata (modo `Local`). Las dos fuentes conviven según la configuración.

### D-063 · Permisos por conexión, tabla y columna
- **Estado:** Vigente · 2026-10-28 (P-042)
- **Qué:**
  - Las operaciones son ver, crear, editar, borrar, exportar e importar.
  - Se definen en tres niveles con herencia: lo de la conexión vale para sus tablas, salvo que la tabla lo cambie, y lo mismo de la tabla a sus columnas.
  - Se aplican en el servidor; la UI solo oculta.

### D-064 · Sin filtros de fila por perfil
- **Estado:** Vigente · 2026-10-28 (el dueño: "no es necesario ese nivel")
- **Qué:** P-043 queda descartado. Restringir qué filas ve cada perfil no forma parte del alcance.

### D-065 · Auditoría solo de cambios, por 1 año
- **Estado:** Vigente · 2026-10-28 (responde Q-007; P-044)
- **Qué:**
  - Se registran altas, ediciones y borrados: quién, cuándo, tabla, clave, valor anterior y nuevo. No se registran las lecturas.
  - Se guarda 1 año por defecto, configurable, con limpieza automática.

### D-066 · Permiso para administrar la seguridad
- **Estado:** Vigente · 2026-10-28
- **Qué:** un permiso nuevo, `ManageSecurity`, que se otorga a roles como los de D-053. Permite administrar perfiles, usuarios y permisos. Los `AdminRoles` lo tienen.

### D-067 · Cómo se aplican los perfiles
- **Estado:** Vigente · 2026-10-28 (P-040, P-042)
- **Qué:**
  - **Sin ningún perfil, la seguridad de datos queda abierta** (todos ven y editan todo). Al crear el primero, solo ven datos los administradores y quienes tengan un perfil.
  - Los permisos se aplican **recortando el esquema** que ve cada usuario (`UserAccess.Restrict`): tablas fuera, columnas ocultas o de solo lectura. Así etiquetas, búsqueda, exportación, combos y pestañas los respetan sin código aparte. Crear, editar, borrar, exportar e importar se verifican además con `ForbiddenException` (403).
  - Se definen por conexión (o `*`), tabla y columna; cada nivel **reemplaza** al anterior cuando aparece, y varios perfiles se suman.
  - Cambiar los vínculos N:M de un registro cuenta como **editar el registro dueño**, no como crear o borrar en el puente.
  - Para no revelar nombres de lo que el perfil oculta: el aviso de cambios del esquema solo trae lo que el usuario ve, los avisos de ajustes sin efecto solo llegan sin restricciones, y **leer** ajustes pasa a exigir `ConfigureTables` (antes, cualquier usuario).
  - Los perfiles otorgan también permisos de la app, que se suman a los de `Auth:Permissions`; las políticas `perm:*` pasan a un `AuthorizationHandler`.
  - Usuarios propios en `mnt_user` (hash PBKDF2). Si el nombre existe en `Auth:Users`, manda la configuración. Al desactivar o borrar uno, su token deja de valer en la siguiente solicitud.
- **Descartado:**
  - Cerrar todo al actualizar (rompería instalaciones existentes sin perfiles).
  - Validar permisos en cada consulta con `if` repartidos por el CRUD: se olvidarían casos (etiquetas, búsquedas).
  - Que la columna o la tabla se sumen a lo de arriba en vez de reemplazarlo: no permitiría restringir una tabla dentro de una conexión abierta.

### D-068 · Cómo se registra y quién ve la auditoría
- **Estado:** Vigente · 2026-10-28 (P-044)
- **Qué:**
  - Una entrada por alta, edición, baja, fila importada o cambio de vínculos N:M, en `mnt_audit` de la metadata. Clave del registro como `col=valor` ordenada, para buscar el historial de un registro.
  - En una edición se guardan **solo las columnas que cambiaron**. Una edición sin cambios no se registra. No se guardan la `rowversion`, las columnas ocultas ni el contenido de archivos (solo su tamaño).
  - Se escribe **después de confirmar** el cambio en la base de datos. Si la metadata falla en ese momento, el cambio queda hecho y el error va al log.
  - Nuevo permiso `ViewAudit` (rol `mnt-auditor`); ve la auditoría de todas las conexiones.
  - Un servicio en segundo plano borra lo vencido al arrancar y cada `Audit:PurgeIntervalHours`.
  - El formulario de edición envía **solo los campos que cambió el usuario**. Así un campo sin tocar no pierde precisión (los segundos de una fecha) ni aparece como cambio.
- **Descartado:**
  - Escribir la auditoría en la misma transacción que el cambio: está en otra base (la metadata), y una transacción distribuida no es portable entre motores.
  - Fallar la operación si la auditoría falla: el usuario creería que no se guardó.
  - Triggers en las bases de los clientes: exigen permisos de DDL y no saben quién es el usuario de la app.
  - Que la auditoría la vea cualquiera que vea la tabla: muestra valores anteriores de columnas que un perfil puede ocultar.

### D-069 · Concurrencia optimista por hash de la fila
- **Estado:** Vigente · 2026-10-28 (P-046)
- **Qué:**
  - Cada fila de un listado o detalle trae `__version`: un hash de los valores de las columnas que el usuario ve (el tamaño, en los archivos).
  - Al editar o borrar, la UI lo devuelve en el encabezado `If-Match`. Si la fila cambió, el servidor responde **409** con cómo quedó (`type: mantenedores:concurrencia`, `current`) y no la toca. Sin `If-Match` no se verifica (importaciones, scripts).
  - El formulario muestra qué cambió la otra persona y deja elegir entre ver lo actual o guardar igual (solo los campos propios, así no pisa lo demás).
  - La comparación y la escritura no van bajo un bloqueo: la ventana es de milisegundos, frente a los minutos que un formulario puede estar abierto.
- **Descartado:**
  - `rowversion` / `xmin`: no existen en MySQL ni MariaDB, ni en tablas de SQL Server sin esa columna. Habría una diferencia entre motores para cubrir un caso que el hash ya cubre.
  - Agregar una columna de versión a las tablas del cliente: la app no modifica su estructura.
  - Comparar en el `WHERE` del `UPDATE` los valores originales: varios tipos no se comparan con `=` (`xml`, `ntext`, `json`).

### D-070 · Tope del pool y límites de uso por configuración
- **Estado:** Vigente · 2026-10-28 (P-051, P-091)
- **Qué:**
  - **Pool:** cada proveedor agrega a la cadena, con los nombres de su driver, el máximo de conexiones (`Database:Pool:MaxSize`, 20) y el cierre de inactivas (`IdleSeconds`, 300). Solo si la cadena no los trae: lo del usuario manda.
  - **Límites de uso:** el limitador de ASP.NET Core, en memoria:
    - 600 solicitudes por minuto por usuario;
    - 10 exportaciones o importaciones por minuto por usuario;
    - 10 intentos de login por minuto por IP.
  - Al pasarse, `429` con `Retry-After`. Los health checks no cuentan.
- **Descartado:**
  - Un semáforo propio por conexión: el pool del driver ya pone el tope y hace esperar.
  - Contadores compartidos entre instancias (Redis): sería una dependencia externa (D-022). Con N instancias el límite efectivo es N veces el configurado; queda para P-050.
  - Limitar el login por usuario además de por IP: el nombre viene en el cuerpo y habría que leerlo antes de autenticar.

### D-071 · Observabilidad opcional por OTLP y log de solicitudes lentas
- **Estado:** Vigente · 2026-10-28 (P-053)
- **Qué:**
  - OpenTelemetry (paquetes 1.19) solo si se configura `Telemetry:OtlpEndpoint`. Sin destino, no se registra nada (D-022).
  - Trazas: ASP.NET Core y las fuentes `Npgsql` y `MySqlConnector`. Métricas: ASP.NET Core, runtime, límites de uso y el pool de esos drivers.
  - Un middleware anota en el log las solicitudes de más de `SlowRequestMilliseconds` (2 s): método, ruta sin la query (puede traer valores), estado, usuario y duración.
  - Pruebas de carga con k6 (`tests/load/crud.js`, `docs/carga.md`), fuera de la CI.
- **Descartado:**
  - Medir dentro de cada método del CRUD: ensucia Core y deja fuera lo que no es CRUD. La solicitud HTTP y las trazas de los drivers ya lo cubren.
  - Un endpoint Prometheus propio: su paquete de OpenTelemetry sigue en prerelease. El Collector convierte OTLP a Prometheus si hace falta.
  - Instrumentar SqlClient: requiere otro paquete y su tiempo ya está en la traza de la solicitud.

### D-072 · Tablas grandes: total estimado, conteo con tope y turno para consultas pesadas
- **Estado:** Vigente · 2026-10-28 (responde Q-004; P-052)
- **Contexto:** el dueño indicó tablas de hasta ~1 millón de filas, en general no transaccionales, y pidió "tenerlo cubierto". Medido con 1 millón de filas en PostgreSQL 16 (`docs/carga.md`):
  - paginar por número va bien: la última página tarda 0,2 s;
  - el costo está en la búsqueda de texto sin índice, que recorre la tabla.
- **Qué:**
  - Una tabla es **grande** si la estimación del motor llega a `Crud:EstimateCountAbove` (500.000). La estimación sale de `pg_class.reltuples`, `sys.partitions` o `information_schema.TABLES`, vía `ISqlDialect.EstimatedRowCount`, y se guarda en caché 5 minutos.
  - Sin filtros, el total de una tabla grande es la estimación ("≈"). Con filtros, se cuenta con tope (`FilteredCountCap`, 1.000, vía `ISqlDialect.CappedCount`): "más de 1.000".
  - En todas las tablas: si la página llega incompleta, el total se calcula sin contar.
  - En tablas grandes, las búsquedas, los filtros y los órdenes por otra columna corren de a 4 por conexión (`MaxConcurrentHeavyQueries`). Las demás esperan sin tomar conexión del pool, y a los 30 s responden 503.
- **Descartado:**
  - Paginación por clave (sin `OFFSET`): con 1 millón de filas no hace falta, y quitaría el "ir a la página N" y el orden libre. Queda en P-098 por si aparecen tablas de decenas de millones.
  - Crear índices desde la app: la app no modifica la estructura de las bases (D-069). Se documenta qué índice pedir al DBA.

### D-073 · Sin lógica propia al guardar; avisos por correo opcionales por tabla
- **Estado:** Vigente · 2026-10-28 (responde Q-005)
- **Qué dijo el dueño:**
  - no habrá lógica de negocio propia al guardar: cada cambio es responsabilidad del usuario;
  - los casos extremos los cubre la base con FK o triggers (también responsabilidad del usuario);
  - no hay que validar contra otros sistemas;
  - enviar correos, sí, como configuración apagada por defecto que se habilita para tablas críticas.
- **Qué:**
  - P-060 (hooks) se descarta. Los errores de FK, únicos, CHECK y triggers ya se traducen a mensajes claros (D-038).
  - Ajuste por tabla `notify`: destinatarios, operaciones (por defecto altas, ediciones y bajas) e `includeValues`. Lo edita quien tiene `ConfigureTables`, y las direcciones no viajan a la UI de datos.
  - Se envía por SMTP (`Notifications`, apagado por defecto) con `System.Net.Mail`, sin dependencias.
  - El aviso usa las mismas entradas que la auditoría (D-068), se arma después de confirmar el cambio y se envía en segundo plano. La cola es de 1.000 avisos, con 3 reintentos; los fallos van al log.
- **Descartado:**
  - Webhooks o scripts al guardar: el dueño no los necesita.
  - Un paquete de correo como MailKit: `System.Net.Mail` alcanza para SMTP con STARTTLS y no agrega dependencias.
  - Enviar dentro de la solicitud: el usuario esperaría al servidor de correo, y un fallo del correo parecería un fallo al guardar.

### D-074 · Mejoras de uso: filtro de FK por etiqueta, permisos de la base y fechas
- **Estado:** Vigente · 2026-10-28 (P-032, P-033, P-073, P-084)
- **Qué:**
  - **Filtro de una FK** con texto: busca también en la columna a mostrar de la tabla referenciada, con `col IN (SELECT clave FROM referenciada WHERE etiqueta contiene …)`. Si el texto es un valor válido, además compara por igualdad. Solo si el usuario ve la tabla referenciada y su columna a mostrar es de texto.
  - **Permiso denegado por la base** (`DbErrorKind.PermissionDenied`, también al leer) → 403 con un mensaje que dice qué pedirle al DBA. Lo distingue de un 403 por perfil de la app.
  - **MySQL/MariaDB:** las claves se leen solo de `KEY_COLUMN_USAGE`, porque MariaDB oculta `TABLE_CONSTRAINTS` a un usuario de solo lectura. Lo encontró la prueba nueva.
  - **Fechas** en la UI como día/mes/año fijo, sin depender del idioma del navegador; las fechas sin zona no se convierten.
  - **Nombres visibles de tablas** (ajuste `label`) en "→ tabla", "Referenciada por", N:M y combos, desde la misma consulta del menú.
- **Descartado:** filtrar la FK con un `JOIN` en la consulta de conteo: el subquery `IN` sirve igual en los cuatro motores y no cambia el conteo.

### D-075 · Importar actualizando por clave y FK de varias columnas
- **Estado:** Vigente · 2026-10-28 (P-088, P-030)
- **Qué:**
  - **Importar** tiene dos modos, `insert` (como antes) y `upsert`:
    - las filas con la clave completa que ya existe se **actualizan** solo con lo que trae el archivo; las demás se crean;
    - si la clave la genera la base (identity), una clave que no existe es un error de esa fila ("déjela vacía para crear");
    - la vista previa dice cuántas se crean y cuántas se actualizan; todo va en una transacción;
    - las claves se buscan en lotes de 100 con `(k1 = … AND k2 = …) OR …`, igual en los cuatro motores;
    - exige permiso de editar; la auditoría registra las actualizaciones con su antes y después.
  - **FK de varias columnas:**
    - la etiqueta va en la última columna local (`ForeignKeyInfo.LabelColumn`), con un `JOIN` por todas las columnas;
    - si una FK simple ya usa esa columna, manda la simple;
    - `/lookup` de una tabla con PK compuesta devuelve `value` como objeto con cada columna de la clave;
    - en el formulario, un solo combo llena todas las columnas.
  - Bases de ejemplo: `sucursal` (PK país + código) y `bodega` (FK compuesta) en los cuatro motores.
- **Descartado:**
  - Upsert con `MERGE` o `ON CONFLICT`: la sintaxis es distinta en cada motor, y el `MERGE` de SQL Server tiene problemas conocidos de concurrencia.
  - Etiqueta en la primera columna de la FK compuesta: suele ser la más genérica (el país), que también puede tener su propia FK.

### D-076 · Interfaz en varios idiomas (español, inglés y ruso)
- **Estado:** Vigente · 2026-10-28 (P-100)
- **Qué:**
  - Selector de idioma en la barra superior y en el login; se recuerda en el navegador (`mantenedores.locale`). Sin elección, se usa el primero de los idiomas preferidos del navegador que la app tenga; si no tiene ninguno, **inglés** (decisión del dueño). El español sigue siendo el idioma base del código.
  - El texto en español del código es la clave: `t('Guardar')`. `src/i18n/en.ts` y `ru.ts` lo traducen; si falta una traducción se ve el español.
  - Textos con cantidad (`{n}`) con formas por número vía `Intl.PluralRules` (ruso: one/few/many); `es.ts` trae solo los singulares.
  - Fechas y horas con el formato del idioma, no del navegador: 28/10/2026 14:05 (es), 10/28/2026 02:05 PM (en), 28.10.2026 14:05 (ru). Para el español se usa `es-ES` en fechas (`es-CL` las escribe 28-10-2026 con a. m./p. m.) y `es-CL` en números (agrupa también 1.234).
  - `npm run lint` corre `scripts/check-i18n.mjs`: falla si algún `t('…')` no está en `en.ts` o `ru.ts`. Por eso las claves se escriben literales, nunca `t(variable)`.
  - Las peticiones llevan `Accept-Language`, para que el servidor pueda traducir sus mensajes más adelante (P-101). Hoy los mensajes del servidor siguen en español.
  - Los nombres de tablas y columnas y los ajustes (`label`) no se traducen: son datos del usuario.
- **Descartado:**
  - **i18next / react-intl:** agregan dependencias y un formato de mensajes propio para unas 300 frases; con `Intl` del navegador alcanza.
  - **Claves abstractas** (`table.save`): obligan a mirar otro archivo para saber qué dice la pantalla, y el español ya es el idioma base del proyecto.

### D-077 · Avisos de cambios entre instancias, sin Redis
- **Estado:** Vigente · 2026-10-28 (P-082, P-092)
- **Qué:**
  - Tabla `mnt_change_stamp` (migración 006): un contador por ámbito, `overrides:<conexión>`, `schema:<conexión>` y `security`.
  - Quien guarda o borra un ajuste, pulsa ↻, edita o borra una conexión, o cambia un perfil o un usuario, sube el contador (`ChangeStamps.PublishAsync`).
  - Cada instancia revisa todos los contadores cada `Metadata:SyncSeconds` (5 s) con una sola consulta y descarta de su memoria lo que otra cambió. Lo propio no vuelve como aviso.
  - Si subir el contador falla, el cambio ya está guardado: se registra en el log y las demás lo ven al refrescar o al vencer sus cachés (perfiles: 30 s).
  - Usuarios propios: `IsActiveAsync` guarda la respuesta 10 s; al crear, editar o borrar un usuario se limpia aquí y, por el aviso `security`, en las demás.
  - Con metadata en archivo (`File`, una sola instancia) no hay contadores y nada cambia.
- **Descartado:**
  - **Redis o caché compartida** (P-050): una dependencia más para algo que la base de metadata ya resuelve con una consulta cada 5 s.
  - **TTL corto del esquema combinado:** obliga a recombinar todo cada pocos segundos aunque nada cambie, y sigue mostrando lo viejo hasta que vence.
  - **LISTEN/NOTIFY o Service Broker:** distinto en cada motor y no existe en SQLite.

### D-078 · Cambios de esquema detectados solos
- **Estado:** Vigente · 2026-10-28 (P-086)
- **Qué:**
  - Cada `Schema:RefreshMinutes` (60) se vuelve a leer el catálogo de las conexiones que están en memoria. Si la foto (tablas, columnas, tipos, claves) es igual, no se toca nada; si cambió, se reemplaza el esquema y aparece el aviso de cambios.
  - Error de "tabla o columna inexistente" (`DbErrorKind.SchemaMismatch`): PostgreSQL 42703/42P01, SQL Server 207/208, MySQL/MariaDB 1054/1146. Se descarta el esquema en memoria (aquí y en las demás instancias) y se responde 409 `mantenedores:esquema`; la UI vuelve a pedir la definición.
  - Los códigos se leen en cada proveedor; Core solo conoce el tipo de error (D-031).
- **Descartado:** releer el catálogo en cada solicitud o con un TTL corto: es la consulta más cara de la app y los cambios de esquema son raros.

### D-079 · Health check del contenedor con la propia app
- **Estado:** Vigente · 2026-10-28 (P-074)
- **Qué:** `dotnet Mantenedores.Api.dll healthcheck [url]` consulta `/api/health/ready` (de `ASPNETCORE_URLS` y `Hosting__PathBase`) y sale con 0 o 1. El `Dockerfile` lo usa como `HEALTHCHECK`.
- **Descartado:** instalar `curl` en la imagen: suma un paquete y superficie de ataque solo para esto.

### D-080 · Mensajes del servidor traducidos en el frontend
- **Estado:** Vigente · 2026-10-28 (P-101)
- **Qué:**
  - El servidor sigue respondiendo en español. `frontend/src/i18n/server.ts` tiene unas 145 plantillas `[es, en, ru]`, con las partes variables entre llaves.
  - `translateServer` busca la frase exacta, luego por plantilla y, si son varias oraciones, una por una. Lo que no reconoce queda en español.
  - Se aplica en `ApiError` (mensaje y errores por campo), en los errores de importación y en las descripciones de cambios de esquema.
  - Una variable no cruza el fin de una oración; `{verb}` se reconoce por la lista de fragmentos ("editar registros de"…), que también se traducen.
  - `npm run lint` revisa que cada traducción tenga las mismas `{variables}` que el español (también en `en.ts` y `ru.ts`).
  - La exportación de la auditoría sí usa `Accept-Language` para sus encabezados.
- **Descartado:**
  - **Catálogo de mensajes en el servidor:** obliga a cambiar unos 150 `throw` de interpolación a plantilla con argumentos y a tener dos sistemas de traducción.
  - **Códigos de error en vez de frases:** las pruebas y los registros pierden el texto legible, y cada frase igual necesita su traducción.

### D-081 · Auditoría con etiquetas, exportación y filtro por perfil
- **Estado:** Vigente · 2026-10-28 (P-094, P-095)
- **Qué:**
  - **Por perfil:** con una consulta `SELECT DISTINCT` se sabe qué tablas tienen entradas y, por nombres (sin leer el esquema, sirve aunque la base no responda), cuáles puede ver el usuario. La consulta se limita a esas conexiones completas o tablas sueltas, y de antes y después se quitan las columnas que no puede ver. Los administradores y la seguridad abierta ven todo.
  - **Etiquetas:** por página, una consulta por tabla referenciada (`WHERE clave IN (…)`, hasta 500 valores) con los permisos del usuario, para FK de una columna y vínculos N:M. Si la base no responde, se ven los ids.
  - **Exportar:** `GET /api/audit/export?format=csv|xlsx` con los mismos filtros, hasta `Audit:ExportMaxRows` (100.000), recorriendo páginas sin contar el total.
- **Descartado:** guardar la etiqueta en la entrada al registrar: el texto cambia (se renombra la categoría) y agranda cada entrada; la etiqueta actual es lo que el usuario reconoce.

### D-082 · Relaciones N:M en pestaña y árbol con relación elegida
- **Estado:** Vigente · 2026-10-28 (P-087, P-089)
- **Qué:**
  - Ajuste `links` por relación N:M: `selector` (por defecto), `tab` (el puente sale en las pestañas de hijas con su propio formulario) o `none`. `ManyToManyLink.Display` lo informa; el formulario solo muestra los de `selector`.
  - Ajuste `treeParent`: cuál autorreferencia usa el árbol cuando hay más de una. En el árbol, arrastrar un nodo sobre otro cambia su padre (con `jefe_id` nulo, también al primer nivel); el servidor sigue rechazando un ciclo.
- **Descartado:** pasar a pestaña por defecto los puentes con columnas extra: cambiaría instalaciones existentes cuyo puente tiene, p. ej., una fecha opcional.

### D-083 · Geometrías editables como WKT
- **Estado:** Vigente · 2026-10-28 (P-090)
- **Qué:**
  - `LogicalType.Spatial`: se lee como WKT y se escribe desde WKT con `ISqlDialect.ParameterFor`. SQL Server: `geography` en SRID 4326 y `geometry` en 0. MySQL/MariaDB: `ST_GeomFromText`, SRID 0.
  - `ValueConverter` revisa la forma general del WKT para dar un mensaje claro; la base valida la geometría.
  - No se ordena ni se filtra por estas columnas.
  - PostGIS y `hierarchyid` siguen de solo lectura (`ReadOnlyText`): sin PostGIS en la imagen de pruebas no se puede pasar la batería común (D-033). Queda P-104.
- **Descartado:** un control de mapa: suma una librería y un proveedor de mapas (D-022), y el WKT ya se puede copiar de cualquier GIS.

### D-084 · Índices únicos, búsqueda sin CAST y tipos propios de PostgreSQL
- **Estado:** Vigente · 2026-10-28 (P-031, P-034, P-075)
- **Qué:**
  - Los índices únicos que no son restricción cuentan como claves únicas (SQL Server `sys.indexes`, PG `pg_index`), salvo los filtrados o por expresión, que no valen para la columna entera. El error de duplicado nombra la columna. En MySQL/MariaDB ya venían así.
  - La búsqueda y el filtro "contiene" ya no convierten a texto las columnas que lo son (`ISqlDialect.Contains(…, isText)`): la base puede usar un índice de trigramas o similar.
  - PG: compuestos, rangos, multirangos e `inet`, `cidr`, `macaddr`, `interval`, `bit`, geometría simple y `timetz` se manejan como texto (`ColumnInfo.TextCast`): se leen con su salida normal (`format('%s', …)`, porque `CAST(inet AS text)` agrega "/32") y se escriben con `CAST(texto AS tipo)`. La base valida el valor. `tsvector` sigue sin soporte: suele ser generada.
- **Descartado:** mapear cada uno de estos tipos a un control propio: la forma en texto es la que el usuario de Postgres ya conoce.

### D-085 · Bloqueo de fila al guardar con versión, y auditoría sin ediciones vacías
- **Estado:** Vigente · 2026-10-28 (P-096, P-103)
- **Qué:**
  - Cuando el cliente manda la versión que editó (D-069), la edición o el borrado van en una transacción: se bloquea la fila (`ISqlDialect.LockRow`: `FOR UPDATE`, o `WITH (UPDLOCK, ROWLOCK, HOLDLOCK)` en SQL Server), se compara y se escribe. Con 4 ediciones simultáneas con la misma versión gana una sola, en los cuatro motores.
  - Auditoría con perfil: una edición en la que solo cambiaron columnas que el usuario no ve no se lista. El total de la página puede quedar mayor que lo listado.
- **Descartado:** `UPDATE … WHERE version = …`: la versión es un hash calculado de la fila, no una columna.

### D-086 · PostGIS editable y SRID por columna
- **Estado:** Vigente · 2026-10-28 (P-104)
- **Qué:** PostGIS pasa a `Spatial`. El SRID se lee de `format_type` (`geometry(Point,4326)`); sin SRID declarado, geography usa 4326 y geometry 0. MySQL 8 informa `SRS_ID`; con SRID se usa `axis-order=long-lat` al leer y escribir, para que el WKT vaya en longitud latitud como en los demás motores. MariaDB no informa SRID (0). Las pruebas de PostGIS usan su propia imagen (`postgis/postgis:12-3.4`, variable `MANTENEDORES_POSTGIS_IMAGE`).
- **Descartado:** pedir el SRID al usuario en cada valor (EWKT): solo PostgreSQL lo entiende.

### D-087 · Renovación silenciosa en un iframe
- **Estado:** Vigente · 2026-10-28 (P-079)
- **Qué:** `silent_redirect_uri` apunta a `silent-renew.html` (estática, sin dependencias), que le pasa la URL a la app con el mismo mensaje que `IFrameWindow.notifyParent` de oidc-client-ts. Con refresh token, oidc-client-ts lo sigue usando; sin él, renueva en el iframe con la sesión del proveedor. Probado con Keycloak sin refresh tokens y tokens de 70 s: la sesión siguió más allá del vencimiento.
- **Descartado:** importar oidc-client-ts en una segunda entrada de Vite solo para esa página: más peso para lo que hace un `postMessage`.

### D-088 · Acciones con procedimientos almacenados
- **Estado:** Vigente · 2026-10-28 (P-061)
- **Qué:**
  - Ajuste `actions` por tabla; cada parámetro sale de una columna del registro, un valor fijo o lo que escriba el usuario.
  - Las rutinas se leen del catálogo (`ISchemaProvider.ReadRoutinesAsync`), en caché como el esquema. No se ofrecen: las de sistema o de extensiones; las sobrecargadas; los procedimientos con parámetros de salida; los parámetros sin nombre; las funciones de SQL Server.
  - La llamada la arma el dialecto (`CallRoutine`): SQL Server `EXEC … @p = …`; PostgreSQL por nombre con `CAST` al tipo declarado (`CALL` o `SELECT * FROM`); MySQL por posición (`CALL` o `SELECT f(…)`). Los valores van siempre como parámetros.
  - Operación nueva de perfil `execute` (incluida en "todo"). Cada ejecución queda en la auditoría (operación `Action`, con los valores), y se muestra el primer conjunto de filas que devuelve (hasta 200).
- **Descartado:** escribir el SQL de la acción en el ajuste: rompe la regla de nombres solo desde el catálogo y valores como parámetros (D-004).

### D-089 · Paginación por clave
- **Estado:** Vigente · 2026-10-28 (P-098)
- **Qué:** con PK de una columna y el orden de la PK (sin orden elegido ni `defaultSort`), "Siguiente" manda `after` = la PK de la última fila y el servidor pide `pk > after` sin OFFSET. Con otro orden se ignora. La grilla recuerda el `after` de cada página para volver atrás; cambiar filtros, orden o tamaño lo reinicia. En 1 millón de filas (PG 16), la página 39.999 bajó de 8,5 s en frío a ~180 ms.
- **Descartado:** cursores para cualquier orden: necesitan los valores de todas las columnas del orden y la comparación de filas, distinta en cada motor.

### D-090 · Nombres y ayudas por idioma
- **Estado:** Vigente · 2026-10-28 (P-102)
- **Qué:** ajustes `labels` (tabla y columna) y `helpTexts` (columna) por código de idioma; la UI usa el del idioma elegido y, si falta, `label`. También para el texto de las acciones.

---

## Propuestas (aún no decididas)

Se convierten en `D-xxx` al aprobarse. Por ahora no hay.
