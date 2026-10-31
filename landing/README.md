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

**Azure (lo que se usa hoy):** `.github/workflows/main_celdo.yml` lo publica en el App Service `celdo` cada vez que
cambia `landing/` en `main`, o a mano desde **Actions → Landing de Celdo en Azure → Run workflow**. Sube solo
`index.html`, `img/` y `web.config`, que le dice a IIS cuál es la página de inicio, el tipo de los `.svg`, la caché y
encabezados de seguridad. En el portal de Azure conviene:
- activar **HTTPS Only**;
- en **Custom domains**, agregar `celdo.app` con su certificado administrado gratis.

Otro hosting estático también sirve (Cloudflare Pages, Netlify, GitHub Pages o un bucket con CDN): hay que subir la
carpeta `landing/` tal cual, con `img/`. No usa cookies. Para medir el embudo se recomienda una analítica sin cookies,
como Plausible o Umami (P-118).
