# Dashboards dinámicos (plan)

**Estado:** aprobado · 2026-11-05 · D-111 · P-127 a P-131. Pedido del dueño: dashboards que arma el usuario, con tope por
plan (Gratis 1, Pro 5, Empresa sin tope) y permisos para crear, ver, editar y borrar.

## 1. Qué es un dashboard

Una página con **tarjetas** (widgets) que resumen datos de las tablas que el usuario ya ve en Celdo. Se arma sin
programar: se elige tabla, qué contar o sumar, cómo agrupar y el tipo de gráfico.

| Tarjeta | Ejemplo | Qué pide |
|---|---|---|
| **Número** (KPI) | "Pedidos del mes: 1.284" | tabla, métrica, filtros |
| **Barras** | Ventas por categoría | + agrupar por columna (o por su FK, con el nombre) |
| **Líneas** | Pedidos por semana | + agrupar por fecha (día, semana, mes, año) |
| **Torta / dona** | Clientes por región | + agrupar por columna, máx. 8 porciones y "Otros" |
| **Tabla top N** | 10 productos más vendidos | + orden y límite (máx. 50) |
| **Tabla dinámica** | Ventas por categoría (filas) y mes (columnas) | + dos agrupaciones; máx. 500 celdas |
| **Mapa** | Clientes por ubicación | columna espacial (WKT, D-079) o latitud y longitud; puntos sobre un fondo propio |

- **Métricas:** contar filas, contar distintos, suma, promedio, mínimo y máximo de una columna numérica.
- **Filtros por tarjeta:** los mismos de la grilla (igualdad, contiene, rango). Además, un filtro de fechas del
  dashboard que aplica a las tarjetas que lo usen ("últimos 30 días", "este mes", "este año", rango propio).
- **Diseño:** cuadrícula de 12 columnas; cada tarjeta es S, M o L y se reordena arrastrando, como las columnas en
  Configurar tablas.
- **Clic en una barra o porción:** abre la grilla de esa tabla con ese filtro.

## 2. Reglas que no se negocian

- **Permisos de datos (D-067, D-107):** una tarjeta solo usa tablas y columnas que **quien mira** puede ver. Si el
  dashboard está compartido y alguien no ve una tabla, esa tarjeta le sale "Sin acceso", sin datos ni nombres.
- **Plan Gratis:** solo las 15 tablas elegidas (D-106).
- **SQL seguro (D-004):** nombres solo desde la metadata y con `Quote`; valores como parámetros. Nada de SQL libre.
- **Motores (D-031, D-035):** la agrupación por fecha va en el dialecto (`date_trunc` en PG, `DATEFROMPARTS`/
  `DATEADD` en SQL Server 2016, `DATE_FORMAT` en MySQL y MariaDB). Pasa la batería común en los 4 motores.
- **Carga en la base:** consultas con tope de grupos, tiempo máximo (`CommandTimeout`), caché corta por tarjeta
  (60 s, la del servicio o Redis) y el límite de uso `Heavy`.

## 3. Permisos

**Del dashboard (por perfil, como las de datos):** `Ver`, `Crear`, `Editar` y `Borrar` dashboards. Se marcan en el
perfil, en una sección nueva "Dashboards". Los administradores tienen todo.

**De cada dashboard:**
- Quien lo crea es su **dueño**: lo ve, edita, comparte y borra.
- **Compartir:** privado (por defecto) o con perfiles elegidos. Para cada perfil: "puede ver" o "puede editar".
- Para editar o borrar uno ajeno hacen falta las dos cosas: el permiso del perfil (`Editar` o `Borrar`) y que el
  dashboard esté compartido con "puede editar".
- Sin ningún perfil creado (seguridad abierta, D-067), todos pueden todo, como hoy con los datos.

## 4. Topes por plan

| Plan | Dashboards | Tarjetas por dashboard |
|---|---|---|
| Gratis | 1 | 6 |
| Pro | 5 | 20 |
| Empresa | sin tope | 50 |

- **El tope es por instalación** (como conexiones y usuarios), no por persona.
- Al llegar al tope, "Nuevo dashboard" muestra el error de plan con **Ver planes** (P-122).
- **Al bajar de plan no se borra nada:** quedan en uso los más antiguos hasta el tope; los demás se ven en la lista
  como "Requiere Pro" y vuelven al subir. Igual que las conexiones de más.

## 5. Cómo se guarda

- Migración **009** en SQLite, PostgreSQL y SQL Server: `mnt_dashboard` (id, nombre, dueño, definición JSON y fechas).
  Con quién se comparte va dentro de la definición, junto a las tarjetas: son pocos dashboards y se filtran en memoria.
  En modo archivo, `dashboards.json`.
- La definición JSON tiene las tarjetas: tipo, conexión, tabla, métrica, agrupación, filtros, tamaño y orden. Se
  valida al guardar contra el esquema (tabla y columnas existen, la métrica cabe en el tipo de columna).
- Si una tabla o columna cambia o desaparece, la tarjeta muestra el aviso en vez de fallar (como los ajustes sin
  efecto).

## 6. API

| Método | Ruta | Qué hace |
|---|---|---|
| GET | `/api/dashboards` | Los que puede ver, con su estado de plan |
| POST | `/api/dashboards` | Crear (permiso y tope) |
| GET / PUT / DELETE | `/api/dashboards/{id}` | Ver, editar (con `shares`, solo el dueño o un administrador) y borrar |
| POST | `/api/dashboards/{id}/widgets/{w}/data` | Datos de una tarjeta, con el filtro de fechas |
| POST | `/api/dashboards/preview` | Datos de una tarjeta mientras se edita, sin guardar |

## 7. Por etapas (un PR cada una)

| Etapa | Qué | Pendiente |
|---|---|---|
| **1. Consultas** | `QueryBuilder.BuildAggregate` y agrupación por fecha en cada dialecto; pruebas unitarias y en los 4 motores. | P-127 |
| **2. Guardar y permisos** | Migración 009, servicio, API, permisos del perfil, compartir y topes del plan. | P-128 |
| **3. Ver** | Menú "Dashboards", lista y página del dashboard con las 5 tarjetas, filtro de fechas, modo día y noche, y móvil. | P-129 |
| **4. Editar** | Editor de tarjetas con vista previa, tamaños y orden arrastrando; compartir. | P-130 |
| **5. Extras** ✅ | Mapa, refresco automático, exportar una tarjeta a CSV, clic que abre la grilla filtrada, y que la IA (MCP) proponga dashboards. | P-131 |

**Gráficos:** propios en SVG, sin librería: son tipos simples, quedan con los colores de la marca en los dos
temas y no suman peso al paquete. Alternativa descartada por ahora: Recharts (~100 KB más).

## 8. Respuestas del dueño (2026-11-05, D-111)

1. El tope de dashboards es **por instalación**.
2. Se comparte **solo con perfiles**.
3. Topes de tarjetas: 6 / 20 / 50.
4. Se suman la **tabla dinámica** (etapas 3 y 4) y el **mapa** (etapa 5).
   - El mapa no depende de un servicio externo (D-022): por defecto dibuja los puntos sobre un contorno propio en SVG.
   - Un servidor de mosaicos (OpenStreetMap u otro) es opcional, por configuración.
