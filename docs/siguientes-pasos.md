# Siguientes pasos

> Punto de partida: la Fase 1 está en `main` (PR #1). Hoy el servicio lee el esquema de
> SQL Server y PostgreSQL, y genera grilla y formulario con combos de FK.
> Este documento detalla qué sigue, en qué orden y cuándo se considera terminado cada paso.

## Resumen

| # | Bloque | Duración estimada | Resultado |
|---|---|---|---|
| 0 | Cierre de la Fase 1 | 1 semana | Base sólida: imagen Docker probada, pruebas de integración, errores claros |
| 1 | Fase 2: personalización | 4–5 semanas | Mantenedores ajustables sin tocar código, maestro-detalle, N:M, import/export |
| 2 | Fase 3: seguridad | 3 semanas | Permisos por rol, auditoría, concurrencia, SSO |
| 3 | Fase 4: escala y operación | 3 semanas | Caché compartida, límites, observabilidad, pruebas de carga |
| 4 | Fase 5: extensiones | continuo | Hooks de negocio, más motores, acciones |

Total hasta una v1 productiva: **unas 14–15 semanas** con 1 backend, 1 frontend y QA/DevOps parcial.

---

## 0. Cierre de la Fase 1 (1 semana)

Pendientes detectados al construir la Fase 1, que conviene cerrar antes de crecer.

### 0.1 Validar la imagen Docker
- La imagen no se pudo compilar en el entorno de desarrollo, que no tenía acceso a NuGet.
- Agregar a la CI un job que ejecute `docker build`.
- **Listo cuando:** la CI compila la imagen y `docker compose up` levanta la app con ambas bases de ejemplo.

### 0.2 Pruebas de integración con bases reales
- Nuevo proyecto `backend/tests/Mantenedores.IntegrationTests` con Testcontainers (Postgres 16 y SQL Server 2022).
- Carga `samples/postgres/01_demo.sql` y `samples/sqlserver/01_demo.sql`.
- Casos a cubrir:
  - Lectura del esquema: tablas, PK compuesta, FK, vista, tabla sin PK.
  - CRUD completo.
  - Inserción en una tabla con trigger (SQL Server).
  - Enum y JSON (Postgres).
  - Errores de único, FK y CHECK.
- **Listo cuando:** corren en la CI contra ambos motores.

### 0.3 Mensajes de error amigables
- Hoy se muestra el mensaje crudo del motor; en Postgres incluso dice "Detail redacted".
- Traducir los códigos más comunes en `backend/src/Mantenedores.Api/Infrastructure/ErrorHandling.cs`:

  | Caso | Postgres (SQLSTATE) | SQL Server (número) | Mensaje |
  |---|---|---|---|
  | Duplicado | 23505 | 2627, 2601 | "Ya existe un registro con ese valor en *columna*" |
  | FK al insertar | 23503 | 547 | "El valor de *columna* no existe en *tabla*" |
  | FK al borrar | 23503 | 547 | "No se puede eliminar: lo usan registros de *tabla*" |
  | CHECK | 23514 | 547 | "El valor no cumple la regla *restricción*" |
  | Nulo | 23502 | 515 | "*columna* es obligatoria" |

- **Listo cuando:** cada caso muestra un mensaje en español, asociado al campo cuando se puede.

### 0.4 Preparar producción
- Guía de despliegue en `docs/despliegue.md`:
  - Variables de entorno.
  - Volumen persistente para `data/` (llaves de cifrado).
  - HTTPS detrás de un proxy.
  - Usuario de BD con permisos mínimos.
- **Listo cuando:** alguien externo puede desplegar siguiendo solo la guía.

---

## 1. Fase 2: personalización (4–5 semanas)

Objetivo: que cada mantenedor se pueda ajustar (nombres, campos, orden, reglas) sin
tocar código, y que esos ajustes **no se pierdan** al volver a leer el esquema.

### 1.1 Metadata store en PostgreSQL (1 semana)
Reemplaza el archivo `connections.json` por una base propia del servicio.

- **Migraciones:** DbUp con scripts SQL versionados en `backend/src/Mantenedores.Api/Migrations/`. Es simple y encaja con Dapper, que ya se usa.
- **Tablas:**
  - `connection`: id, nombre, motor, cadena cifrada, esquemas.
  - `table_override`: conexión, esquema, tabla, JSON de ajustes.
  - `column_override`: conexión, esquema, tabla, columna, JSON de ajustes.
  - `schema_snapshot`: conexión, hash, fecha, JSON del esquema leído.
- **Código:** nueva clase `PostgresConnectionStore : IConnectionStore`. `FileConnectionStore` queda solo para desarrollo, elegido por configuración.
- **Migración de datos:** al arrancar, si existe `connections.json` y la tabla está vacía, se importa.
- **Listo cuando:** las conexiones sobreviven a reinicios y a varias instancias de la API.

### 1.2 Modelo de overrides (1 semana)

**Ajustes por tabla**

| Ajuste | Ejemplo |
|---|---|
| `label` | "Productos" en vez de `producto` |
| `hidden` | Ocultar `log_evento` del menú |
| `readOnly` | Tabla de consulta aunque tenga PK |
| `displayColumn` | Corrige la elección automática (p. ej. `Pedido` mostraba `Estado`) |
| `defaultSort` | `-fecha` |
| `virtualPrimaryKey` | Clave para tablas sin PK declarada (habilita la edición) |
| `virtualForeignKeys` | Relaciones que existen en la práctica pero no están declaradas en la BD |
| `group` | Agrupar tablas en el menú ("Ventas", "Configuración") |

**Ajustes por columna**

| Ajuste | Ejemplo |
|---|---|
| `label`, `helpText` | "Precio neto (CLP)" |
| `hidden` | Ocultar `rowguid`, columnas de auditoría |
| `order` | Orden en grilla y formulario |
| `readOnly`, `required` | Más estrictos que la BD |
| `widget` | `textarea`, `email`, `url`, `color`, `money`, `password` |
| `validation` | regex, mínimo/máximo, largo |
| `options` | Lista fija para columnas texto con CHECK (p. ej. `Estado` en SQL Server) |
| `showInGrid` | Visible solo en el formulario |

**Implementación**
- Nuevo `OverrideApplier` en `Mantenedores.Core`, que combina `TableInfo` con los overrides.
- `SchemaService` lo aplica después de leer el catálogo; la caché guarda el resultado ya combinado.
- Endpoints nuevos:
  - `GET/PUT /api/meta/{conn}/overrides/{schema}/{table}`
  - `DELETE` del mismo, para volver a lo automático.
- `QueryBuilder` y `ValueConverter` respetan las reglas nuevas: ocultas, solo lectura, PK y FK virtuales, validaciones.
- **Listo cuando:** un override cambia la grilla y el formulario sin reiniciar, y persiste tras un "↻ refrescar esquema".

### 1.3 Editor de overrides en la UI (1 semana)
- Botón "Configurar" en cada mantenedor (solo para administradores; en Fase 3 pasa a ser un permiso).
- Pantalla con dos pestañas:
  - **Tabla:** nombre visible, grupo, columna a mostrar, orden por defecto.
  - **Columnas:** lista ordenable con visible, etiqueta, ayuda, control y validaciones.
- Vista previa en vivo del formulario resultante.
- Botón "Restablecer a lo automático".
- **Listo cuando:** se puede dejar un mantenedor "presentable" sin editar JSON.

### 1.4 Diferencias de esquema (3 días)
- Al refrescar, comparar con el último `schema_snapshot`: tablas o columnas nuevas o eliminadas, cambios de tipo y de nulos.
- Endpoint `GET /api/meta/{conn}/diff` y aviso en la UI: "Hay 3 cambios en el esquema".
- Marcar los overrides huérfanos, que apuntan a columnas que ya no existen.
- **Listo cuando:** borrar una columna en la BD muestra el aviso y no rompe el mantenedor.

### 1.5 Maestro-detalle (1 semana)
- La API ya expone `referencedBy`. En la pantalla de edición, agregar pestañas con la grilla de cada tabla hija filtrada por la FK.
  - Ejemplo: Pedido → PedidoDetalle.
- "Nuevo" desde una pestaña hija abre el formulario con la FK ya completada y bloqueada.
- Soportar FK compuestas en el filtro (hoy solo se etiquetan las FK de una columna).
- Override `children` para elegir qué hijas mostrar y en qué orden.
- **Listo cuando:** se puede crear un pedido y sus líneas sin salir de la pantalla del pedido.

### 1.6 Relaciones N:M (4 días)
- **Detección:** una tabla con exactamente 2 FK cuya PK está formada por ambas y sin otras columnas obligatorias se trata como tabla puente (p. ej. `producto_etiqueta`).
- **UI:** en el formulario del producto, selector múltiple de etiquetas.
- **API:** `PUT /api/data/{conn}/{schema}/{tabla}/row/links/{puente}` recibe la lista completa y sincroniza en una transacción: inserta las nuevas y borra las quitadas.
- **Listo cuando:** se asignan etiquetas a un producto desde su propio formulario.

### 1.7 Árboles por autorreferencia (3 días)
- Para tablas con FK a sí mismas (p. ej. `empleado.jefe_id`), ofrecer una vista de árbol además de la grilla.
- Validar ciclos al guardar: un registro no puede ser su propio ancestro.
- **Listo cuando:** se ve la jerarquía de empleados y no se puede crear un ciclo.

### 1.8 Exportar e importar (4 días)
- **Exportar:** CSV y Excel del filtro actual, por streaming, con un tope configurable (p. ej. 100.000 filas).
- **Importar** CSV o Excel en tres pasos:
  1. Subir el archivo.
  2. Asociar columnas y ver una vista previa con los errores por fila.
  3. Confirmar.
- Se graba en una transacción por lotes; si algo falla, no queda nada a medias.
- **Listo cuando:** se pueden cargar 5.000 productos con validación previa.

### 1.9 Tipos pendientes (3 días)
- Arreglos de Postgres: editar como lista de valores.
- Binarios: descargar y subir archivos, con tope de tamaño.
- `geography` / `geometry`: solo lectura, mostrados como texto WKT.
- `hierarchyid`: solo lectura como texto.

---

## 2. Fase 3: seguridad (3 semanas)

1. **Usuarios y roles** en el metadata store. Reemplaza los usuarios de `appsettings`.
2. **SSO** con OIDC (Entra ID, Google o Keycloak), manteniendo el login local como respaldo.
3. **Permisos (RBAC)** por conexión, tabla, columna y operación (ver, crear, editar, eliminar, exportar, configurar). Se aplican en `QueryBuilder`; la UI solo oculta lo que no corresponde.
4. **Filtros de fila por rol**, p. ej. `sucursal_id = {usuario.sucursal}`. Se agregan siempre al WHERE.
5. **Auditoría:** quién, cuándo, tabla, clave, valor anterior y nuevo. Se guarda en el metadata store y se consulta desde la UI.
6. **Concurrencia optimista** con `rowversion` (SQL Server) o `xmin` (Postgres). Si otro usuario cambió el registro, avisar en vez de pisarlo.
7. **Secretos en un vault** (Azure Key Vault, HashiCorp Vault) en vez de Data Protection local.

## 3. Fase 4: escala y operación (3 semanas)

1. **Caché del esquema en Redis**, compartida entre instancias, con invalidación por hash.
2. **Pools de conexión con tope** por conexión y cierre de los inactivos.
3. **Límites de uso** (rate limit) por usuario y por conexión.
4. **Tablas grandes:** paginación por clave (sin OFFSET) y conteo estimado (`reltuples` / `sys.partitions`).
5. **Observabilidad:** OpenTelemetry (trazas, métricas por conexión y por consulta lenta) y logs estructurados.
6. **Pruebas de carga** con k6: 200 usuarios concurrentes y tablas de 10 millones de filas.

## 4. Fase 5: extensiones (continuo)

- Hooks de negocio antes y después de guardar: webhook o script.
- Procedimientos almacenados expuestos como botones de acción.
- Nuevos motores: MySQL y Oracle. Solo requieren un nuevo `IDatabaseProvider`.
- Cargas masivas y tareas programadas.

---

## Orden recomendado y calendario

```
Semana  1      0. Cierre Fase 1
Semana  2      1.1 Metadata store
Semana  3      1.2 Overrides (backend)
Semana  4      1.3 Editor de overrides + 1.4 Diff de esquema
Semana  5      1.5 Maestro-detalle
Semana  6      1.6 N:M + 1.7 Árboles + 1.8 Import/Export + 1.9 Tipos
Semana  7–9    Fase 3: seguridad
Semana 10–12   Fase 4: escala y operación
Semana 13–15   Estabilización, documentación y piloto con usuarios reales
```

Lo más urgente es **0.1, 0.2 y 1.1**: todo lo demás depende de tener pruebas contra bases reales
y un lugar donde guardar la configuración.

## Decisiones abiertas

Conviene resolverlas antes de la semana 2:

1. **¿Dónde se va a desplegar?** (Azure, AWS, servidor propio). Define el vault, el SSO y la base de metadata.
2. **¿Quiénes lo usan?** Un equipo interno o varios clientes (multi-tenant). Cambia el modelo de datos de la Fase 2.
3. **¿Qué proveedor de identidad?** Entra ID, Google o Keycloak.
4. **¿Cuántas bases y de qué tamaño?** Define cuándo hace falta la Fase 4.
5. **¿Habrá lógica de negocio propia?** Si sí, adelantar los hooks de la Fase 5.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Esquemas sin FK o PK declaradas | PK y FK virtuales en los overrides (1.2) |
| Lógica oculta en triggers | Mensajes de error claros (0.3) y pruebas con triggers (0.2) |
| Exponer tablas sensibles | Ocultar por defecto y publicar explícitamente cuando llegue RBAC (Fase 3) |
| Diferencias entre motores | Pruebas de integración contra ambos en cada PR (0.2) |
| Pérdida de las llaves de cifrado | Metadata store + vault (1.1, Fase 3) y respaldo del volumen `data/` |
