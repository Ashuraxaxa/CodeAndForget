# Embeber dashboards y mantenedores (plan)

**Estado:** aprobado · 2026-11-05 · D-113 · P-134 a P-139. Pedido del dueño: poner los dashboards y los mantenedores de
Celdo dentro de otras aplicaciones (intranets, portales de clientes, sistemas propios).

## 1. Tres formas

| Forma | Para quién | Cómo entra |
|---|---|---|
| **A. Con sesión** | Usuarios que ya tienen cuenta en Celdo (intranet) | Su login de siempre, dentro del iframe |
| **B. Con token firmado** | Usuarios de la otra app sin cuenta en Celdo (portal de clientes) | El servidor de la otra app firma un token corto |
| **C. Dashboard público** | Cualquiera con el enlace, sin login | Enlace secreto que se puede revocar |

Las tres usan las mismas pantallas, sin el menú ni la barra de arriba:

- `/embed/dashboards/{id}`: un dashboard.
- `/embed/c/{conexión}/t/{esquema}/{tabla}`: un mantenedor, con su formulario y sus pestañas hijas.
- `/embed/p/{enlace}`: un dashboard público.

**Parámetros:** `tema=claro|oscuro|sistema` e `idioma=es|en|ru`. Se aplican solo en el iframe, sin cambiar lo que el
usuario eligió en la app.

**Alto:** la página avisa su alto a la app anfitriona con `postMessage` (`{ type: "celdo:resize", height }`), para que
el iframe no tenga barra de desplazamiento propia.

## 2. Reglas que no se negocian

- **Solo los sitios permitidos pueden embeber.** La cabecera `Content-Security-Policy: frame-ancestors` lo hace cumplir
  en el navegador:
  - el resto de la app solo se deja embeber por sí misma (`'self'`, más `X-Frame-Options: SAMEORIGIN`);
  - antes de este cambio, cualquier página podía meter Celdo en un iframe y engañar al usuario para que hiciera clics.
- **Nunca más permisos que los del usuario o los de la app.** Un token firmado se cruza siempre con el perfil de la app.
- **Filtros fijos por fila del lado del servidor**, como parámetros (D-004). Aplican a la grilla, al formulario, a
  editar, borrar, exportar y a los dashboards. Al crear un registro, el valor fijo se pone solo.
- **Nada externo obligatorio (D-022):** el token se firma con una clave compartida, sin llamar a Celdo ni a un tercero.
- **Auditoría:** lo que hace un usuario externo queda como `app:{aplicación}/{usuario}`.

## 3. Planes (respuesta del dueño, D-113)

| Plan | Embeber | Marca |
|---|---|---|
| Gratis | No | — |
| Pro | Formas A, B y C; hasta **3 aplicaciones** registradas | Sello pequeño "Hecho con Celdo" al pie |
| Empresa | Aplicaciones sin tope | Sin marca |

Los usuarios externos (forma B) y quienes miran un dashboard público (forma C) **no cuentan** como usuarios del plan.

## 4. Forma A: con sesión ✅

```html
<iframe src="https://celdo.miempresa.com/embed/dashboards/ID?tema=sistema&idioma=es"
        style="width:100%;border:0" id="ventas"></iframe>
<script>
  // Opcional: el iframe toma el alto del contenido.
  addEventListener('message', e => {
    if (e.origin === 'https://celdo.miempresa.com' && e.data?.type === 'celdo:resize')
      document.getElementById('ventas').style.height = e.data.height + 'px'
  })
</script>
```

- **Qué se puede embeber:** el id del dashboard sale en su URL (`/dashboards/ID`). Un mantenedor es `/embed/c/{conexión}/t/{esquema}/{tabla}`.
- **Qué hay dentro del iframe:** se mira el dashboard (sin editarlo) y se usan la grilla, el formulario, las pestañas hijas e importar. "Configurar" y el historial quedan en la app.
- **Alto:** Chrome no redibuja un iframe de otro sitio mientras está fuera de la pantalla. El alto se actualiza cuando el iframe entra a la vista.

- **Sitios permitidos:** se configuran en `Embed:AllowedOrigins`, por ejemplo `https://intranet.empresa.com` o
  `https://*.empresa.com`. Sin sitios, nadie de afuera puede embeber.
- **Login local:** el formulario sale dentro del iframe.
- **Login con OIDC:** el proveedor no se deja embeber, así que se abre en una ventana emergente y vuelve sola al
  iframe.
- **Almacenamiento:** los navegadores separan el almacenamiento de un iframe de otro sitio, así que la sesión dentro del
  iframe es aparte de la de la app.
- **Permisos:** los de siempre del usuario.

## 5. Forma B: token firmado ✅

**Registrar la app:** en Seguridad → Aplicaciones, el administrador registra la aplicación con:

- su nombre;
- los sitios desde donde se embebe;
- el **perfil** con el máximo de permisos que puede dar;
- una **clave secreta** (`csk_…`), que se muestra una sola vez y se guarda cifrada con las llaves de la app (con un
  hash no se podría verificar la firma).

**Firmar el token:** el servidor de la otra app firma un JWT (HS256, `kid` = id de la aplicación, vence en 1 hora como
máximo) con:

| Claim | Qué es |
|---|---|
| `sub` | Usuario externo (para la auditoría) |
| `name` | Nombre visible (opcional) |
| `res` | Lo que puede abrir: `dashboard:<id>` o `table:<conexión>/<esquema>/<tabla>` (de 1 a 50) |
| `perm` | `view` (siempre), `create`, `edit`, `delete`, `export`, `import`, `execute`; se cruza con el perfil de la app |
| `filters` | Filtros fijos por fila, p. ej. `{ "cliente_id": 42 }`: hasta 10, con valores de texto, número o sí/no (sección 6) |
| `exp`, `iat` | Vencimiento y emisión |

- **Uso:** la otra app arma la URL `/embed/...#token=...`. El fragmento no llega a los registros del servidor.
- **Canje:** Celdo canjea el token por una sesión corta propia, que solo abre lo que dice `res`.
- **Revocar el acceso:** se desactiva la aplicación o se rota su clave. Las sesiones abiertas se cortan; con varias
  instancias, en hasta 30 s.

**Ejemplo para firmar (Node, sin librerías):**

```js
const crypto = require('crypto')
const b64 = o => Buffer.from(JSON.stringify(o)).toString('base64url')
function celdoToken(kid, secret, sub, res, perm = ['view']) {
  const now = Math.floor(Date.now() / 1000)
  const head = b64({ alg: 'HS256', typ: 'JWT', kid })
  const body = b64({ sub, res, perm, iat: now, exp: now + 600 })
  return `${head}.${body}.${crypto.createHmac('sha256', secret).update(`${head}.${body}`).digest('base64url')}`
}
// <iframe src="https://celdo.miempresa.com/embed/dashboards/ID#token=...">
```

**Qué puede hacer la sesión:**

- **Datos:** solo las tablas de `res`, más las que leen las tarjetas de sus dashboards. Nunca supera el perfil de la
  aplicación ni las operaciones de `perm`.
- **Dashboards:** solo los de `res`, si además están compartidos con el perfil de la aplicación (el dueño lo decide).
  Se miran, no se editan.
- **Prohibido:** no tiene permisos de la app. Administrar, auditar, tokens, OData y MCP responden 403.
- **Sin login:** el token se canjea en `POST /api/embed/session` por una sesión propia (`emb_…`) que vence con él. Si
  vence, la página lo avisa; la renovación automática llega con `embed.js` (etapa 5).
- **Plan:** no ocupa lugar de usuario en el plan.

## 6. Filtros fijos por fila ✅

- **Dónde se aplica:** el filtro `{ "cliente_id": 42 }` va a cada tabla permitida que tenga esa columna. En la
  aplicación se puede indicar otro nombre de columna por tabla (`pedido.id_cliente`).
- **Seguro por defecto:** una tabla sin la columna **no se abre**, salvo que la aplicación la marque como "sin filtro"
  (p. ej. un catálogo de categorías).
- **Combos de FK:** los combos de una tabla filtrada también se filtran si la tabla referenciada tiene la columna. Y
  si la FK es la columna fija (`pedido.cliente_id = 42`), el combo de clientes ofrece solo el 42.
- **Dónde se configura:** en Seguridad → Aplicaciones → Filtros por fila: "otra columna" (`esquema.tabla.filtro =
  columna`) y "tablas sin filtro" (`esquema.tabla`).
- **Qué cubre:** listar, contar, buscar, leer un registro, editar, borrar, exportar, importar, árboles, archivos,
  acciones, relaciones N:M, combos y las tarjetas de los dashboards. La columna fija queda de solo lectura y al crear
  toma el valor sola, aunque se mande otro; tampoco se puede mover un registro a otro valor al editar.
- **Un valor que no calza** con el tipo de la columna (texto en una columna numérica) no abre la tabla.

## 7. Forma C: dashboard público ✅

- **Quién lo activa:** el administrador marca "Permitir dashboards públicos" en Seguridad → Aplicaciones (apagado por
  defecto, desde Pro). Luego, el dueño de un dashboard (o un administrador) toca **Enlace público → Crear enlace
  público** y copia el enlace o el `<iframe>`.
- **El enlace:** `/embed/p/{token}`, con un secreto de 32 bytes al azar. **Nuevo enlace** invalida el anterior,
  **Quitar enlace** lo cierra, y apagar el interruptor cierra todos. Solo quien comparte ve el enlace; los demás ven la
  marca "Público".
- **Permisos:** las tarjetas corren con los permisos del dueño. En modo Local se usan sus roles actuales y, si se
  desactiva al dueño, el enlace deja de responder. En modo Jwt se usan los roles que tenía al crearlo, como los tokens
  de acceso.
- **Quien mira:** solo elige el período. No edita, no abre la grilla ni ve quién es el dueño.
- **Se embebe en cualquier sitio** (`frame-ancestors *`): es de solo lectura y no tiene sesión.
- **Carga en la base:** cada tarjeta se guarda 60 s por enlace, período y versión del dashboard, y rige el límite de uso
  por IP.
- **Plan:** en Pro sale el sello "Hecho con Celdo"; en Empresa, sin marca.
- **Sin filtros fijos por ahora:** un enlace público muestra lo que ve su dueño. Para mostrar solo una parte, se arma un
  dashboard con los filtros en sus tarjetas, o se usa la forma B.

## 8. `embed.js` ✅

Es un script opcional, sin dependencias, que Celdo publica en `/embed.js`:

```html
<div id="ventas"></div>
<script src="https://celdo.miempresa.com/embed.js"></script>
<script>
  Celdo.embed('#ventas', {
    url: '/embed/dashboards/ID',                       // o /embed/c/{conexión}/t/{esquema}/{tabla}, o /embed/p/{enlace}
    getToken: () => fetch('/celdo-token').then(r => r.text()),   // su servidor firma el token (forma B)
    tema: 'sistema', idioma: 'es',
    onEvent: e => console.log(e.type, e)
  })
</script>
```

- **Qué hace:** arma el iframe, ajusta su alto y pide el token a `getToken` al abrir. Lo vuelve a pedir antes de que
  venza y lo envía sin recargar la página.
- **Qué devuelve:** `{ iframe, setToken(token), destroy() }`.
- **Seguridad:** solo acepta mensajes de ese iframe y de su origen.

| Evento | Cuándo | Datos |
|---|---|---|
| `celdo:ready` | La página cargó | `path` |
| `celdo:resize` | Cambió el alto | `height` |
| `celdo:saved` | Se guardó un registro | `connection`, `schema`, `table`, `key`, `created` |
| `celdo:deleted` | Se borró un registro | `connection`, `schema`, `table`, `key` |
| `celdo:click` | Clic en una barra, porción o fila de una tarjeta | `widget`, `title`, `key`, `label`, `value` |
| `celdo:token-expiring` | Faltan 2 minutos para que venza la sesión | `expiresAt` |
| `celdo:session-expired` | La sesión venció | — |

Sin `embed.js`, los mismos eventos llegan con `postMessage` y el token nuevo se envía como
`{ type: "celdo:token", token }` al iframe.

**Ejemplo para probar:** `samples/embed/server.js` es un portal en Node 18+, sin dependencias. Firma el token en el
servidor y embebe lo que diga `CELDO_RES`:

```bash
CELDO_KID=<id de la aplicación> CELDO_SECRET=<clave> \
CELDO_RES=dashboard:<id>,table:demo/ventas/producto CELDO_PERM=view,edit CELDO_FILTERS='{"categoria_id":1}' \
node samples/embed/server.js     # http://localhost:8090, con el sitio http://localhost:8090 en la aplicación
```

## 9. Por etapas (un PR cada una)

| Etapa | Qué | Pendiente |
|---|---|---|
| **1. Base y forma A** ✅ | Cabecera `frame-ancestors`, `Embed:AllowedOrigins`, rutas `/embed` sin menú, login en el iframe (ventana emergente con OIDC), tema e idioma por parámetro, alto por `postMessage`, sello en Pro, bloqueo en Gratis | P-134 |
| **2. Aplicaciones y tokens** ✅ | Migración 010, Seguridad → Aplicaciones, canje del token, sesión limitada a `res` y `perm`, auditoría `app:`, tope de 3 en Pro | P-135 |
| **3. Filtros por fila** ✅ | Filtro fijo en listar, leer, editar, borrar, exportar, combos y dashboards; valor fijo al crear; batería común en los 4 motores | P-136 |
| **4. Dashboards públicos** ✅ | Enlace secreto, interruptor del administrador, permisos del dueño, límite por IP | P-137 |
| **5. `embed.js` y eventos** ✅ | Script, eventos, renovación del token, página de prueba en `samples/` | P-138 |
| **6. Documentación y venta** | Ejemplos para firmar en C#, Node y Python, README, landing y planes | P-139 |
