# API

Referencia de las rutas HTTP. Todas, salvo `auth/config`, `auth/login` y los health checks, requieren
`Authorization: Bearer <token>`: el del login local o el del proveedor de identidad (ver
[configuracion.md](configuracion.md#autenticación-auth)). Los permisos se explican en [seguridad.md](seguridad.md).

Los errores siguen el formato *Problem Details* (`application/problem+json`); las validaciones traen `errors` por campo.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/auth/config` | Modo de autenticación y datos públicos para el login (sin secretos) |
| POST | `/api/auth/login` | Solo modo `Local`: `{ username, password }` → `{ token }` |
| GET | `/api/auth/me` | Nombre, roles, si es administrador y permisos |
| GET | `/api/connections` | Lista conexiones (sin la cadena de conexión) |
| PUT | `/api/connections/{id}` | Crea o edita. Si `connectionString` va vacío, se conserva la anterior. Permiso `ManageConnections` |
| POST | `/api/connections/test` | Prueba sin guardar. Permiso `ManageConnections` |
| DELETE | `/api/connections/{id}` | Elimina la conexión (no toca la base). Permiso `ManageConnections` |
| GET | `/api/meta/{conn}/tables` | Tablas y vistas descubiertas |
| GET | `/api/meta/{conn}/tables/{schema}/{table}` | Definición completa (columnas, PK, FK) |
| POST | `/api/meta/{conn}/refresh` | Vuelve a leer el esquema |
| GET | `/api/meta/{conn}/diff` | Cambios del esquema desde la referencia y ajustes sin efecto |
| POST | `/api/meta/{conn}/diff/accept` | Toma el esquema actual como referencia. Permiso `ConfigureTables` |
| GET | `/api/meta/{conn}/overrides[/{schema}/{table}]` | Ajustes de los mantenedores ([ajustes.md](ajustes.md)) |
| PUT / DELETE | `/api/meta/{conn}/overrides/{schema}/{table}` | Guarda o quita el ajuste de una tabla. Permiso `ConfigureTables` |
| POST | `/api/meta/{conn}/overrides/{schema}/{table}/preview` | Vista previa de un ajuste sin guardarlo. Permiso `ConfigureTables` |
| POST | `/api/meta/{conn}/overrides/copy` | Copia a `{conn}` los ajustes de otra conexión del mismo motor: `{ from, schemaMap?, parts?, tables?, replaceAll?, apply }`. Con `apply: false` solo dice qué pasaría con cada tabla. Permiso `ConfigureTables` |
| GET | `/api/data/{conn}/{schema}/{table}` | Lista. `?page=1&pageSize=25&sort=nombre,-precio&q=texto&f.categoria_id=2`. `f.` = contiene (textos) o igual; `e.` = igual exacto, también en textos (`e.pedido_id=5`) |
| GET | `/api/data/{conn}/{schema}/{table}/row?{pk}=…` | Un registro. PK compuesta: `?PedidoId=1&Linea=2` |
| POST | `/api/data/{conn}/{schema}/{table}` | Crea. Devuelve la fila creada |
| PUT | `/api/data/{conn}/{schema}/{table}/row?{pk}=…` | Actualiza |
| DELETE | `/api/data/{conn}/{schema}/{table}/row?{pk}=…` | Elimina |
| GET | `/api/data/{conn}/{schema}/{table}/lookup?q=…` | `[{ value, label }]` para combos de FK |
| GET | `/api/data/{conn}/{schema}/{table}/tree?parent=…` | Árbol de una tabla con autorreferencia, un nivel por llamada (`[{ value, label, hasChildren }]`); sin `parent`, las raíces |
| GET | `/api/data/{conn}/{schema}/{table}/export?format=csv\|xlsx\|json\|xml` | Exporta el listado con los mismos filtros, búsqueda y orden, sin paginar (tope `Crud:ExportMaxRows`) |
| POST | `/api/data/{conn}/{schema}/{table}/import/preview` | Multipart `file` (+ `mapping` opcional): asociación de columnas y errores por fila, sin grabar |
| POST | `/api/data/{conn}/{schema}/{table}/import` | Igual, y si no hay errores inserta todo en una transacción |
| GET | `/api/data/{conn}/{schema}/{table}/row/links/{relación}?{pk}=…` | Valores vinculados por una relación N:M (`[{ value, label }]`) |
| PUT | `/api/data/{conn}/{schema}/{table}/row/links/{relación}?{pk}=…` | Reemplaza la lista completa (`[1, 2, 3]`) en una transacción |
| GET | `/api/data/{conn}/{schema}/{table}/row/file/{columna}?{pk}=…` | Descarga el archivo de una columna binaria (imágenes y PDF se ven en el navegador; `&download=1` fuerza la descarga) |
| POST | `/api/data/{conn}/{schema}/{table}/row/actions/{acción}?{pk}=…` | Ejecuta una acción (procedimiento almacenado) sobre el registro; `{ "parámetro": valor }` para los que se preguntan. Permiso `execute` |
| GET | `/api/meta/{conn}/routines` | Procedimientos y funciones que se pueden usar en acciones. Permiso `ConfigureTables` |
| GET | `/api/engines` | Motores instalados, también los de plugins |
| GET | `/api/meta/notifications` | Si los avisos por correo están activos |
| GET | `/api/audit` | Auditoría con filtros (`conn`, `schema`, `table`, `user`, `op`, `from`, `to`, `k.<columna>`). Permiso `ViewAudit` |
| GET | `/api/audit/export?format=csv\|xlsx\|json\|xml` | La auditoría filtrada (tope `Audit:ExportMaxRows`), para descargar. Permiso `ViewAudit` |
| GET / PUT / DELETE | `/api/security/profiles[/{id}]`, `/api/security/users[/{usuario}]` | Perfiles y usuarios propios ([seguridad.md](seguridad.md)). Permiso `ManageSecurity` |
| GET | `/api/health/live`, `/api/health/ready` | Health checks, sin autenticación |

En los listados, cada FK de una columna trae además `<columna>__label` con el texto de la tabla referenciada, y cada
columna de archivo, `<columna>__size` con su tamaño en bytes (el contenido no viaja en el listado). Un archivo se
guarda enviando su contenido en base64 (admite `data:…;base64,`) en el alta o la edición; `null` lo quita.
Los arreglos de PostgreSQL viajan como listas JSON (`["a", "b"]`).
