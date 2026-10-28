# Ajustes de los mantenedores (overrides)

Cada mantenedor se genera solo a partir del catálogo de la base. Los **ajustes** corrigen o completan lo
automático sin tocar código ni la base: etiquetas, columnas ocultas, orden, validaciones, la columna que se
muestra en los combos y claves o relaciones que la base no declara (P-011, D-049 a D-051).

- Se guardan en la metadata del servicio (`mnt_table_override`, o `data/overrides.json` con `Metadata:Provider=File`).
- Se aplican **al momento**, sin reiniciar, y se mantienen al refrescar el esquema (↻).
- Leerlos lo puede hacer cualquier usuario; **cambiarlos exige el permiso `ConfigureTables`**: por defecto, el rol `mnt-configurador` o un administrador (D-053, ver `Auth:Permissions` en [configuracion.md](configuracion.md)).
- Se editan desde la UI: botón **⚙ Configurar** en cada mantenedor (solo lo ve quien tiene el permiso), con pestañas *Tabla* y *Columnas*, vista previa en vivo y **Restablecer a lo automático** (P-012). También se pueden cargar por la API.

## API

| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/meta/{conn}/overrides` | Todos los ajustes de la conexión |
| GET | `/api/meta/{conn}/overrides/{schema}/{table}` | El ajuste de una tabla (`{}` si no tiene) |
| PUT | `/api/meta/{conn}/overrides/{schema}/{table}` | Reemplaza el ajuste completo. Valida y devuelve la tabla ya combinada. **ConfigureTables** |
| DELETE | `/api/meta/{conn}/overrides/{schema}/{table}` | Vuelve a lo automático. **ConfigureTables** |
| POST | `/api/meta/{conn}/overrides/{schema}/{table}/preview` | Aplica el ajuste **sin guardarlo**: devuelve `{ table, errors }`, con la tabla combinada y los errores por campo. La usa el editor. **ConfigureTables** |

Un ajuste inválido se rechaza con `400` y un error por campo, con la ruta del JSON:

```json
{ "errors": { "columns.email.pattern": ["expresión regular inválida: …"], "displayColumn": ["la columna 'x' no existe"] } }
```

Si un ajuste ya guardado deja de aplicar (p. ej. se borró una columna en la base), se omite esa parte y el resto
sigue funcionando. Se informa en `warnings` de `GET /api/meta/{conn}/tables` y en el aviso de cambios del esquema
(ver abajo). En **⚙ Configurar → Columnas** aparecen los ajustes de columnas que ya no existen, con un botón para quitarlos.

## Cambios en el esquema de la base

La primera vez que se lee el esquema de una conexión se guarda una **foto de referencia** (`mnt_schema_snapshot`,
o `data/snapshots/` con `Metadata:Provider=File`). Al refrescar (↻), la app compara el esquema leído con esa foto:

- tablas y vistas nuevas o eliminadas;
- columnas nuevas o eliminadas, cambios de tipo (incluido el largo) y de "acepta vacíos";
- cambios de clave primaria y relaciones (FK) nuevas o eliminadas.

La UI muestra un aviso arriba del mantenedor ("⚠ 2 cambios en el esquema · 1 ajuste sin efecto") con el detalle.
Quien tiene el permiso `ConfigureTables` lo da por revisado con **Aceptar cambios**, que toma el esquema actual como
nueva referencia. Los ajustes sin efecto siguen avisándose hasta que se corrigen.

| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/meta/{conn}/diff` | `{ since, acceptedBy, changes, overrideWarnings }`: cambios desde la referencia y ajustes sin efecto |
| POST | `/api/meta/{conn}/diff/accept` | Toma el esquema actual como referencia. **ConfigureTables** |

## Documento de una tabla

Todo es opcional; lo que no se indica queda automático. Los nombres de columnas no distinguen mayúsculas.

```json
{
  "label": "Productos",
  "group": "Catálogo",
  "hidden": false,
  "readOnly": false,
  "displayColumn": "nombre",
  "defaultSort": "-precio,codigo",
  "virtualPrimaryKey": ["producto_id", "desde"],
  "virtualForeignKeys": [
    { "columns": ["producto_id"], "refSchema": "ventas", "refTable": "producto", "refColumns": ["id"] }
  ],
  "columns": {
    "precio":   { "label": "Precio ($)", "helpText": "Con IVA", "order": 0, "min": 0, "max": 100000, "widget": "money" },
    "codigo":   { "label": "Código", "pattern": "[A-Z]{3}-\\d{3}", "maxLength": 7 },
    "estado":   { "options": ["Pendiente", "Pagado", "Anulado"], "required": true },
    "notas":    { "widget": "textarea", "showInGrid": false },
    "rowguid":  { "hidden": true },
    "creado_en": { "readOnly": true }
  }
}
```

### Tabla

| Ajuste | Qué hace |
|---|---|
| `label` | Nombre visible en el menú y los títulos. |
| `group` | Grupo del menú en vez del esquema. |
| `hidden` | No aparece en el menú. **No es un permiso:** la tabla se sigue pudiendo abrir por su URL o la API (los permisos llegan con P-042). |
| `readOnly` | De consulta aunque tenga PK. |
| `displayColumn` | Columna que se muestra cuando otra tabla la referencia (combos y etiquetas de FK). Si no se indica y la automática queda oculta, se vuelve a elegir. |
| `defaultSort` | Orden del listado cuando el usuario no eligió otro: `-fecha,nombre` (`-` = descendente). |
| `virtualPrimaryKey` | Clave para tablas **sin PK declarada**: habilita editar y borrar. En una vista permite leer por clave y usarla en combos, pero sigue siendo de solo lectura. Las columnas deben identificar una sola fila: si no, una edición afectaría varias. |
| `children` | Tablas hijas que se muestran en pestañas al editar un registro, por nombre de la FK y en ese orden. Sin definir = todas; `[]` = ninguna. En el editor: *Tabla → Tablas hijas*. |
| `notify` | Avisos por correo cuando cambia un registro (D-073): `{"emails": ["jefa@empresa.cl"], "on": ["update", "delete"], "includeValues": true}`. `on` acepta `create`, `update`, `delete`, `import` y `links`; vacío = altas, ediciones y bajas. Solo se envían si la app tiene SMTP configurado (`Notifications`, ver `configuracion.md`). En el editor: *Tabla → Avisos por correo*. |
| `virtualForeignKeys` | Relaciones que la base no declara: dan etiqueta, combo y "Referenciada por" como una FK real. La base no las hace cumplir. `name` es opcional. |

### Columna

| Ajuste | Qué hace |
|---|---|
| `label`, `helpText` | Nombre visible y ayuda bajo el campo. |
| `order` | Posición en grilla y formulario (menor primero). Por defecto, la posición en la tabla (1, 2, 3…). |
| `hidden` | No se lee, no se escribe ni se busca por la API: sirve para datos sensibles. Excepción: si es PK, se sigue usando como clave pero la UI no la muestra. No se puede ocultar una columna obligatoria sin valor por defecto (no se podrían crear registros). |
| `showInGrid` | `false`: solo en el formulario. |
| `readOnly` | No se escribe aunque la base lo permita. |
| `required` | Obligatoria aunque la base acepte nulos; un texto vacío cuenta como faltante. |
| `widget` | Control de la UI: `textarea`, `email`, `url`, `color`, `money`, `password`. |
| `pattern` | Expresión regular (.NET) que debe cumplir **el texto completo**. Se evalúa con un tiempo máximo de 100 ms. |
| `maxLength` | Largo máximo de un texto; nunca más que lo que acepta la base. |
| `min`, `max` | Rango de un número. |
| `options` | Lista fija de valores para un texto (se muestra como lista desplegable). |

Las reglas (`required`, `pattern`, `options`, `min`, `max`, `maxLength`) se validan **en el servidor** al crear y
editar, junto con las de la base, y los errores vuelven por campo. Los filtros del listado no las aplican.

## Ejemplo con curl

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/auth/login -H 'content-type: application/json' \
  -d '{"username":"admin","password":"admin"}' | jq -r .token)

curl -X PUT http://localhost:5080/api/meta/demo/overrides/ventas/producto \
  -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' \
  -d '{"label":"Productos","group":"Catálogo","columns":{"atributos":{"hidden":true}}}'
```

## Varias instancias

Cada instancia guarda en memoria el esquema combinado. Un ajuste se ve al instante en la instancia que lo
guardó; en las demás, al refrescar el esquema (↻) o al reiniciar. La caché compartida llega con P-050 (ver P-082).
