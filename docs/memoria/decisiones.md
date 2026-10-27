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
- **Estado:** Temporal · 2026-10-27 → se reemplaza con P-010 (metadata store, D-023) y P-045 (llaves y secretos del entorno, D-025)
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
- **Estado:** Vigente · 2026-10-27 (se implementa en P-010)
- **Qué:** `Metadata:Provider` = `Sqlite` (por defecto) · `Postgres` · `SqlServer`. Migraciones con DbUp y scripts por motor.
- **Por qué:**
  - SQLite permite una instalación de un solo contenedor sin instalar otra base.
  - Postgres o SQL Server se usan cuando hay varias instancias o el equipo ya respalda sus bases ahí. Cada equipo usa la que tiene.
- **Alternativa descartada:** obligar a usar PostgreSQL (plan inicial).

### D-024 · Autenticación libre: modo `Local` o cualquier emisor JWT/OIDC
- **Estado:** Vigente · 2026-10-27 (se implementa en P-041)
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
- **Estado:** Vigente · 2026-10-27
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
  - `Oracle.ManagedDataAccess.Core` para Oracle (sin cliente instalado, `BindByName = true`).
- **Alternativa descartada:** `MySql.Data`, el driver oficial de Oracle para MySQL: licencia GPL y async limitado.

### D-031 · El dialecto absorbe las diferencias, no Core
- **Estado:** Vigente · 2026-10-27 (se implementa en P-020 a P-024)
- **Qué:** `ISqlDialect` declara:
  - Prefijo de parámetros.
  - Estrategia de inserción: filas, parámetros de salida, último id o valores provistos.
  - Carácter de escape de LIKE (`!`).
  - Si `''` equivale a NULL.
  - Capacidades por columna: ordenable, filtrable, buscable.
  - Ajustes de sesión al abrir la conexión.
- **Además:** las etiquetas de FK usan alias cortos (`l0`…) para no chocar con el límite de 63 bytes de Postgres ni con el de Oracle.
- **Regla:** en Core no puede haber `if (motor == X)`. Si un motor necesita algo distinto, se expresa como capacidad del dialecto.
- **Alternativa descartada:** ramas por motor dentro de `QueryBuilder`.

### D-032 · Qué es un "esquema" en cada motor
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - En MySQL/MariaDB, el "esquema" es la base de datos. Por defecto se expone solo la de la cadena de conexión.
  - En Oracle, el "esquema" es el owner. Por defecto se expone solo el del usuario conectado, porque recorrer `ALL_*` completo es lento.
  - En ambos se pueden listar más esquemas en la configuración de la conexión.

### D-033 · Un motor está soportado si pasa la batería común
- **Estado:** Vigente · 2026-10-27
- **Qué:**
  - Las pruebas de integración se escriben una vez como clase base y cada proveedor la hereda con su contenedor y su base de ejemplo.
  - La CI corre una matriz con un job por motor.
  - Un cambio en Core o en un proveedor no se fusiona si algún motor falla.
- **Por qué:** es lo que evita que un motor quede "soportado en el papel".

---

## Propuestas (aún no decididas)

Se convierten en `D-xxx` al aprobarse.

- **PR-01 · Migraciones del metadata store con DbUp** (scripts SQL versionados por motor: SQLite, Postgres, SQL Server). Encaja con Dapper; la alternativa es EF Core Migrations. Ver P-010.
- **PR-02 · Pruebas de integración con Testcontainers** (la organización como batería común ya quedó decidida en D-033). Ver P-002 y P-027.
- **PR-04 · Login en la SPA con `oidc-client-ts`** (Authorization Code + PKCE), librería estándar y sin dependencia de un proveedor. Ver P-041.
- **PR-03 · Overrides guardados como JSON** por tabla y por columna (flexibles, sin migrar el esquema por cada ajuste nuevo). Ver P-011.
