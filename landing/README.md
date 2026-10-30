# Landing de Celdo

Página de venta en un solo archivo (`index.html`), sin compilación. Está en español e inglés, se adapta a modo
claro y oscuro y tiene precios con facturación mensual o anual. Marca: [manual de marca](../docs/comercial/manual-de-marca.md).

## Antes de publicar

Al inicio del `<script>`, en `CONFIG`:

| Clave | Qué poner |
|---|---|
| `download` | Enlace a la guía de instalación (hoy, el README del repositorio). |
| `checkoutProMonthly` / `checkoutProYearly` | Enlaces de pago de Pro (Paddle o Lemon Squeezy). Mientras estén vacíos, el botón lleva a la sección "Empieza ahora". |
| `salesEmail` | Correo de ventas. |
| `terms` / `privacy` | Enlaces a los términos y a la política de privacidad. Vacíos: no se muestran. |
| `dockerImage` | Imagen oficial, cuando esté publicada (P-117). |

## Publicar

Cualquier hosting estático sirve: Cloudflare Pages, Netlify, GitHub Pages o un bucket con CDN. Hay que subir la
carpeta `landing/` tal cual, con `img/`. No usa cookies. Para medir el embudo se recomienda una analítica sin cookies,
como Plausible o Umami (P-118).
