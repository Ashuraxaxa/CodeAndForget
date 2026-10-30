# Servidor MCP: una IA que revisa el esquema y propone ajustes

El servicio puede publicar un servidor [MCP](https://modelcontextprotocol.io) (P-114, D-101). Con él, cualquier IA
compatible puede leer el esquema de una conexión y proponer ajustes: Claude Code, Claude Desktop, Cursor, VS Code y otras.
La IA **no guarda nada**: sus ajustes quedan como **propuestas** que una persona acepta o descarta en Configurar.

## Activarlo

Está apagado por defecto. En `appsettings.json` o con variables de entorno:

```json
{ "Mcp": { "Enabled": true } }
```

```bash
Mcp__Enabled=true
```

Se publica en `/api/mcp`, por HTTP (*Streamable HTTP*, sin estado), detrás de la misma subruta y del mismo proxy
que el resto de la API (P-007). Ejemplo: `https://mi-servidor/mantenedores/api/mcp`.

## Conectar una IA

1. Entre con un usuario que tenga el permiso **Configurar tablas** (`ConfigureTables`).
2. Abra **Tokens de acceso** desde su nombre, en la barra superior, y cree un token. Cópielo: se muestra una sola vez.
3. En esa misma página, **Conectar una IA por MCP** muestra la URL y el comando listo para Claude Code.

**Claude Code:**

```bash
claude mcp add --transport http celdo https://mi-servidor/api/mcp --header "Authorization: Bearer mnt_…"
```

**Claude Desktop, Cursor, VS Code y otros clientes** con un archivo de configuración (el nombre de las claves cambia
un poco de un cliente a otro; vea su documentación):

```json
{
  "mcpServers": {
    "celdo": {
      "type": "http",
      "url": "https://mi-servidor/api/mcp",
      "headers": { "Authorization": "Bearer mnt_…" }
    }
  }
}
```

En lugar del token `mnt_…` sirve también el JWT de una sesión o del proveedor de identidad, pero vence pronto. El token
es lo práctico.

## Qué puede hacer la IA

| Herramienta | Qué hace |
|---|---|
| `list_connections` | Conexiones que el usuario puede ver: id, nombre, motor y si permiten una muestra de datos. |
| `list_tables` | Tablas y vistas de una conexión, con lo básico y los avisos de ajustes que no se pudieron aplicar. |
| `get_table` | Definición de la tabla según el catálogo (columnas, tipos, PK, FK, índices) y su ajuste actual. |
| `suggest_settings` | Las sugerencias sin IA del servicio (P-112): relaciones, filtros de combos, columnas técnicas y correos. |
| `sample_rows` | Hasta las 1.000 filas más recientes, solo si la conexión tiene **Permitir que la IA vea una muestra de datos**. |
| `preview_settings` | Aplica un ajuste sin guardarlo y devuelve cómo quedaría y sus errores. |
| `propose_settings` | Deja el ajuste como propuesta pendiente, con su motivo. Antes lo valida como si se guardara a mano. |
| `list_proposals` | Propuestas pendientes, aceptadas o descartadas, para no repetirlas. |

Al conectarse, el servidor le explica a la IA el flujo: leer, sugerir, probar y proponer.

Ejemplo de pedido a la IA: *"Revisa las tablas de la conexión ventas y propón nombres visibles, relaciones que falten y
filtros de combos. Explica cada propuesta."*

## Revisar las propuestas

En **Configurar** de cada tabla aparece **Propuestas de la IA (n)** con el motivo de cada una. **Cargar en la edición**
la pone en el editor, con su vista previa; al **Guardar** queda aceptada. **Descartar** la cierra sin aplicarla.

## Seguridad

- **La IA tiene exactamente el acceso de quien creó el token** (D-100). Además, para entrar a `/api/mcp` ese usuario
  necesita el permiso `ConfigureTables`. Sin él, la respuesta es `403`.
- **Nunca guarda ajustes ni cambia datos:** solo lee y propone. No hay herramientas para crear, editar o borrar registros.
- **Datos:** sin la muestra activada en la conexión, la IA solo ve la estructura. Con la muestra, ve lo mismo que el
  usuario en la grilla (perfiles, columnas ocultas), sin archivos.
- **Cada llamada se autoriza de nuevo**, porque el servidor no guarda sesiones: si se borra el token o cambian los
  permisos del usuario, la siguiente llamada ya lo refleja.
- **Límites:** las llamadas cuentan en `RateLimit:PermitsPerMinute`, y cada muestra cuenta como una operación pesada
  (`RateLimit:HeavyPerMinute`).
- **Datos que la IA lee:** el contenido de una muestra puede traer texto que intente desviar a la IA. Por eso nada de lo
  que ella haga se aplica sin que una persona lo revise.
