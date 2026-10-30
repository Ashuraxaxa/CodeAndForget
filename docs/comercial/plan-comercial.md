# Plan comercial de Celdo

**Versión:** 1 · 2026-10-30 · decisiones D-103 y D-104.

Celdo es el nombre comercial de lo que en el código se llama "mantenedores". Este plan define a quién se le vende,
qué se le promete, cuánto cuesta y cómo se llega a las primeras ventas. Los precios son el punto de partida: se revisan
con datos de uso a los 90 días.

---

## 1. Qué vendemos

**Celdo convierte cualquier base de datos SQL Server, PostgreSQL, MySQL o MariaDB en una aplicación para ver, buscar,
crear y editar datos.** Las pantallas salen del esquema en minutos, sin programar. Celdo se instala en la red del
cliente y respeta permisos y auditoría.

**Lema:** *Tu base de datos, lista para trabajar.*

### El problema que resuelve

| Hoy | Con Celdo |
|---|---|
| "¿Me cambias este dato?": un ticket, alguien de TI abre el gestor de base de datos y ejecuta un `UPDATE` a mano en producción. | La persona que pidió el cambio lo hace sola, en una pantalla con validaciones, con el permiso justo y queda en la auditoría. |
| Programar pantallas de mantenimiento de tablas (catálogos, parámetros, maestros) cuesta semanas por cada sistema. | Se conecta la base y las pantallas ya están. Se ajustan nombres, combos y permisos sin código. |
| Una aplicación heredada cuyo proveedor desapareció, o cuyo código nadie se atreve a tocar, deja datos sin manera segura de corregirse. | Celdo trabaja sobre la misma base y no toca la aplicación: sirve de back-office. |
| Las herramientas de paneles internos cobran por usuario y exigen el plan más caro para instalarse en servidores propios. | Celdo se instala en tu red desde el plan gratis y cobra por instalación, no por persona. |

### Por qué Celdo y no otra herramienta

1. **No hay que construir nada.** Retool, Appsmith o Budibase son constructores: alguien arma cada pantalla. Celdo
   lee el esquema (PK, FK, tipos, índices) y genera todo; solo se ajusta lo que se quiera.
2. **En tu red, desde el plan gratis.** Los datos no salen de la empresa. Es la primera objeción de TI y la que más
   frena la compra de herramientas en la nube.
3. **Precio por instalación, no por usuario.** Para un equipo de 25 personas, Retool Business cuesta unos USD 1.250
   al mes (unos USD 50 por usuario). Celdo Pro cuesta USD 49.
4. **Pensado para bases reales.** PK compuestas, FK virtuales, tablas sin clave, vistas, árboles, relaciones N:M,
   tablas de millones de filas y SQL Server desde la versión 2016.
5. **Seguro para producción.** Perfiles por conexión, tabla, columna y acción. Auditoría con el valor anterior y el
   nuevo. No pisa cambios de otra persona (concurrencia optimista). Login local o cualquier proveedor OIDC.
6. **Con IA, sin riesgo.** Por MCP, una IA (Claude, Cursor…) revisa el esquema y propone ajustes. Nunca guarda nada:
   una persona acepta cada propuesta.
7. **En español de verdad**, y también en inglés y ruso.

---

## 2. A quién le vendemos

| Perfil | Qué le duele | Qué le decimos | Quién paga |
|---|---|---|---|
| **TI y soporte** en empresas medianas (50 a 1.000 personas) | Tickets de corrección de datos, scripts a mano en producción, nadie sabe quién cambió qué. | "Deja de ejecutar `UPDATE` a mano: delega el cambio con permisos y auditoría." | Jefatura de TI. Ticket chico, se paga con tarjeta. |
| **Analistas de datos y de negocio** | Mantener catálogos, parámetros y tablas de referencia depende de TI o de un Excel que nadie sincroniza. | "Edita tus tablas de referencia tú mismo, con validaciones, y exporta lo que necesites." | Llega por el plan gratis; empuja la compra Pro dentro de la empresa. |
| **Empresas con aplicaciones heredadas** | Proveedor desaparecido o mantenimiento muy caro; los datos no se pueden corregir. | "Tu aplicación sigue igual; Celdo le da a tu equipo un back-office seguro sobre la misma base." | Gerencia o TI. Plan Empresa más servicios de implementación. |
| **Consultoras e integradores** | Cada cliente pide pantallas de mantenimiento; programarlas no deja margen. | "Entrega en un día lo que hoy cotizas en semanas, y gana un margen sobre la licencia." | Socio: revende con margen (sección 6). |

**Primer mercado:** Latinoamérica, con precios en USD. **Idioma:** español primero; el landing y la app también en inglés.

---

## 3. Planes y precios

Celdo se instala siempre en la red del cliente (Docker, servicio de Windows o systemd). El plan se activa con una
**clave de licencia** que se valida sin conexión a internet (sección 7), así sirve también en redes cerradas.

| | **Gratis** | **Pro** | **Empresa** |
|---|---|---|---|
| **Precio** | USD 0 | **USD 49/mes** por instalación · USD 490/año (2 meses gratis) | **Desde USD 299/mes**, facturado anual (USD 3.588/año) |
| **Para** | Probar, uso personal y equipos chicos | Equipos de TI, soporte y analistas | Varias áreas, producción crítica, cumplimiento |
| Usuarios | Hasta 3 | Hasta 25 (+25 por USD 29/mes) | Ilimitados |
| Conexiones a bases | 2 | Ilimitadas | Ilimitadas |
| Instalaciones | 1 | 1 por licencia | Ilimitadas (producción, QA, desarrollo) |
| Motores | SQL Server, PostgreSQL, MySQL, MariaDB | Igual | Igual, más motores propios como plugins |
| Pantallas generadas, combos con búsqueda, maestro-detalle, N:M, árboles | ✓ | ✓ | ✓ |
| Ajustes por tabla y columna, sugerencias automáticas | ✓ | ✓ | ✓ |
| Exportar e importar | CSV | CSV, Excel, JSON y XML; importar actualizando por clave | ✓ |
| Login | Local | Local | Local y SSO con OIDC (Entra ID, Keycloak, Okta, Auth0…) |
| Perfiles y permisos por tabla, columna y acción | Administrador y usuarios | ✓ | ✓ |
| Auditoría de cambios | 7 días | 1 año, con exportación | Retención configurable |
| Avisos por correo de tablas críticas | — | ✓ | ✓ |
| Botones que ejecutan procedimientos almacenados | — | ✓ | ✓ |
| Copiar ajustes entre conexiones | — | ✓ | ✓ |
| IA por MCP: propuestas de ajustes | — | ✓ | ✓ |
| Varias instancias (Redis), metadata en PostgreSQL o SQL Server, OpenTelemetry | — | — | ✓ |
| Marca "Hecho con Celdo" | Visible | Sin marca | Sin marca |
| Soporte | Comunidad | Correo, respuesta en 2 días hábiles | Prioritario: 4 horas hábiles en incidentes críticos, con SLA |
| Extras | — | — | Implementación y capacitación incluidas, factura u orden de compra, revisión de seguridad |

**Prueba:** 30 días de Pro gratis, sin tarjeta, desde la misma instalación.
**Descuentos:** 50 % para ONG y educación. Precio de lanzamiento a los primeros 50 clientes Pro: USD 39/mes de por vida.

### Por qué estos cortes

- **Gratis vale de verdad:** una persona instala, conecta su base y corrige datos el primer día. Así se gana la
  recomendación interna. Los límites (3 usuarios, 2 conexiones, 7 días de auditoría) se notan justo cuando el uso
  pasa de una persona a un equipo: ese es el momento de pagar.
- **Pro cubre lo que pide un equipo:** permisos finos, auditoría larga, Excel y avisos. Es un precio que una jefatura
  de TI paga con tarjeta, sin licitación.
- **Empresa cobra lo que cuesta atender bien:** SSO, alta disponibilidad, SLA y acompañamiento. El "desde" deja
  espacio para negociar volumen y servicios.
- **Por instalación y no por usuario**, para que nadie deje gente fuera de la herramienta por ahorrar. Es el
  argumento contra Retool y Forest Admin.

### Servicios profesionales (ingreso aparte)

| Servicio | Precio de referencia |
|---|---|
| Implementación guiada (instalación, conexiones, ajustes de las tablas clave, permisos): 8 horas | USD 600 |
| Capacitación para el equipo: 2 horas en vivo | USD 250 |
| Motor propio como plugin (por ejemplo, una base que no está en la lista) | Cotización |
| Bolsa de horas de consultoría | USD 70/hora |

---

## 4. Cómo se vende (embudo)

El producto se vende solo hasta Pro; Empresa necesita una conversación.

```
Landing ──► Descarga gratis (docker run) ──► Activación ──► Prueba Pro 30 días ──► Pro (tarjeta)
                                                │
                                                └──► "Hablar con ventas" ──► Demo ──► Empresa (factura)
```

- **Activación** = conectar una base y editar el primer registro en las primeras 24 horas. Es la métrica que más
  importa: quien llega ahí entiende el producto.
- **Dentro de la app:** cuando se alcanza un límite (el cuarto usuario, la tercera conexión, la auditoría de más de
  7 días), un aviso claro ofrece la prueba Pro. Nunca se bloquea el trabajo en curso.
- **Cobro:**
  - Pro con tarjeta, mediante un *merchant of record* (Paddle o Lemon Squeezy): cobra en cualquier país, emite la
    factura y paga los impuestos de cada país.
  - Empresa por transferencia, contra factura u orden de compra.

### Canales para los primeros 90 días

| Canal | Acción | Para quién |
|---|---|---|
| Búsqueda (SEO en español) | 10 artículos que respondan búsquedas reales: "editar tabla SQL Server sin programar", "alternativa a Retool autoalojada", "pantalla de mantenimiento para PostgreSQL", "cómo dar acceso a una tabla sin dar acceso a la base". | TI y analistas |
| Video corto | Demos de 2 minutos: "de la base a la pantalla en 120 segundos", una por motor. YouTube, LinkedIn y TikTok técnico. | Todos |
| LinkedIn | El fundador publica 2 veces por semana casos reales de "datos que no se podían corregir". | TI, gerencia |
| Comunidades | Grupos de SQL Server, PostgreSQL y .NET en español; r/SQLServer, r/PostgreSQL y dev.to en inglés. Responder preguntas reales; no hacer spam. | TI |
| Product Hunt y Hacker News | Lanzamiento en inglés en el día 45, con la demo pública lista. | Global, prensa técnica |
| Socios | 3 consultoras o integradores en el primer trimestre (sección 6). | Empresas con aplicaciones heredadas |
| Correo | Secuencia para quien descarga: día 0 (cómo conectar), día 2 (ajustes), día 7 (permisos y auditoría), día 20 (fin de la prueba). | Usuarios gratis |

---

## 5. Hoja de ruta comercial (90 días)

| Semanas | Qué | Listo cuando |
|---|---|---|
| 1–2 | **Marca legal:** buscar y registrar "Celdo" en la oficina de marcas del país sede (y por el Protocolo de Madrid si se apunta a más mercados). Comprar el dominio (`celdo.app`, `celdo.io` o `getceldo.com`) y las cuentas en redes sociales. | Solicitud presentada, dominio comprado |
| 1–4 | **Producto vendible:** cambiar el nombre en la interfaz a Celdo (P-117), sumar el módulo de licencias y los límites por plan (P-116), y publicar la imagen Docker oficial. | La app muestra su plan y se activa con una clave |
| 2–4 | **Landing en línea** (P-118) con precios, demo pública de solo lectura y botón de pago. Términos de servicio, política de privacidad y contrato de licencia (EULA) revisados por un abogado. | Se puede pagar Pro de punta a punta |
| 5–8 | **Lanzamiento** en español: LinkedIn, comunidades, primeros 5 artículos y 3 videos. Precio de lanzamiento para los primeros 50 clientes Pro. | 300 descargas, 10 clientes Pro |
| 7 | **Product Hunt** en inglés. | — |
| 9–13 | **Socios y Empresa:** 3 consultoras firmadas, 2 demos de Empresa por semana y el primer caso de éxito publicado. | 1 cliente Empresa, 25 clientes Pro |

### Metas (no son pronósticos)

| Indicador | Meta |
|---|---|
| Visita al landing → descarga | 5 % |
| Descarga → activación en 24 h | 40 % |
| Activación → Pro en 60 días | 4 % |
| Cancelación mensual de Pro | menos de 3 % |
| Ingreso mensual recurrente al mes 6 | ≈ USD 2.900 (40 Pro + 3 Empresa) |
| Ingreso mensual recurrente al mes 12 | ≈ USD 8.900 (120 Pro + 10 Empresa) |

---

## 6. Programa de socios

- **Revendedor:** 25 % de margen sobre la licencia el primer año y 15 % en las renovaciones. El socio cobra la
  implementación con su propia tarifa.
- **Requisitos:** una persona certificada (una capacitación de 4 horas, gratis) y un caso implementado.
- **Lo que recibe:** licencia de demostración sin costo, materiales con la marca, figurar en el directorio de socios
  y los contactos de su país que lleguen por el landing.

---

## 7. Qué hay que construir para cobrar

| Pendiente | Qué es |
|---|---|
| **P-116 · Licencias y planes** | Una clave firmada (Ed25519) con la organización, el plan, el tope de usuarios y el vencimiento. Se valida sin internet, sirve en redes cerradas y no envía datos a ningún lado. Al vencer hay 14 días de gracia y después se vuelve al plan Gratis sin perder nada. Los límites se aplican en el servidor: usuarios activos, conexiones, retención de la auditoría y las funciones Pro y Empresa. La página "Plan" muestra el plan y su uso, y tiene un botón para pegar una clave nueva. |
| **P-117 · Nombre Celdo en la app** | Título, logo, favicon, login, README, imagen Docker y textos. El código interno (`Mantenedores.*`) puede seguir igual. |
| **P-118 · Landing en línea** | `landing/` en un hosting estático (Cloudflare Pages, Netlify o GitHub Pages) con el dominio propio, más el enlace de pago y la medición del embudo (analítica sin cookies, como Plausible). |
| **Demo pública** | Una instancia de solo lectura con la base de ejemplo, para probar Celdo sin instalar nada. |

---

## 8. Riesgos y cómo se cubren

| Riesgo | Cómo se cubre |
|---|---|
| El nombre está registrado en algún país objetivo | Búsqueda formal antes de gastar en marca. El plan B es un nombre de la lista corta (Kerno). |
| "Es gratis, ¿para qué pagar?" | Los límites están en lo que necesita un equipo (usuarios, permisos, auditoría), no en lo que necesita una persona. |
| Piratería de la clave | La clave está firmada y no se puede inventar. Copiarla entre instalaciones viola la licencia; Empresa, el plan de mayor valor, incluye una relación y un SLA que no se copian. No vale la pena gastar más en esto al inicio. |
| Ticket bajo en Latinoamérica | Servicios profesionales y socios. Empresa se cotiza por valor. |
| Competencia con mucha inversión (Retool, Appsmith) | No se compite en construir pantallas: se compite en "cero construcción", en la red del cliente, precio por instalación y en español. |
| Soporte que no escala | Documentación, comunidad y videos para Gratis; soporte por correo con plazo para Pro; SLA solo en Empresa. |

---

## Preguntas abiertas para el dueño

Están en [preguntas-abiertas.md](../memoria/preguntas-abiertas.md): Q-009 (código abierto o cerrado), Q-010 (empresa y
país que factura) y Q-011 (dominio).

Fuentes de los precios de la competencia, revisadas el 2026-10-30. Son referencias de blogs comparativos; antes de
publicar comparaciones hay que confirmarlas en la página de precios de cada empresa:
- [Appsmith: precios](https://www.superblocks.com/blog/appsmith-pricing)
- [Budibase: precios](https://www.modern-datatools.com/tools/budibase/pricing)
- [Retool: precios](https://toolradar.com/tools/retool/pricing)
- [Alternativas a Forest Admin](https://www.jetadmin.io/blog/best-forest-admin-alternatives-compare-top-admin-panel-builders-for-2026/)
