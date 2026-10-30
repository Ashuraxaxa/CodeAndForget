# Bitácora

Una entrada por sesión, la más reciente arriba. Breve: qué se hizo, qué se decidió y qué quedó abierto.

---

## 2026-10-30 · Revisión completa de idiomas (P-115)
- El dueño vio mensajes en español con otro idioma elegido y pidió que se respete el idioma en el frontend y en el backend.
- Se midió con un script: de 209 mensajes del servidor que llegan al usuario, 36 no tenían traducción. Ahora es un chequeo de `npm run lint` (D-102).
- Pantallas que no traducían texto del servidor: la vista previa en Configurar, copiar ajustes y probar conexión. Importar escondía el motivo del error del archivo.
- El servidor traduce la exportación de la auditoría (antes salía `Update` aunque la interfaz estuviera en español) y el correo (`Notifications:Language`).
- El botón de login con proveedor decía "Sign in with el proveedor de identidad".
- En el navegador, en inglés y en ruso: los errores de Configurar, de la prueba de conexión y de importar salen traducidos.
- 268 unitarias en verde.

## 2026-10-30 · Servidor MCP, tercera parte de la IA (P-114)
- `/api/mcp` con el SDK oficial de C#, HTTP sin estado, apagado por defecto (`Mcp:Enabled`) y con `ConfigureTables`.
- Ocho herramientas: leen, sugieren, muestran, prueban y proponen. Ninguna guarda. Detalle en D-101 y `docs/mcp.md`.
- La página de Tokens muestra la URL y el comando de Claude Code.
- Probado en vivo contra Postgres con un token: la muestra se negó en la conexión sin permiso, el ajuste inválido volvió con su error y la propuesta válida quedó pendiente. Probado también con el cliente oficial de MCP en las pruebas.
- Hallazgos:
  - al activarlo la app no arrancaba (opciones JSON sin `TypeInfoResolver`); lo cubre una prueba;
  - la vista previa devolvía 500 en vez de 404 (una lambda `async` que no esperaba su tarea).
- 259 unitarias y 291 de integración en verde.

## 2026-10-29 · Tokens, propuestas y muestra, segunda parte de la IA (P-113)
- Migración 007 en los tres motores de metadata, y almacenes de tokens y propuestas (también en modo `File`).
- Tokens con un esquema de autenticación propio: actúan como su dueño. El dueño pidió que tengan el mismo acceso que quien los crea.
- Propuestas validadas como un ajuste; en Configurar se cargan en la edición y se aceptan al guardar.
- Muestra de las 1.000 filas más recientes, solo con la conexión marcada.
- En el navegador:
  - se creó un token y, solo con él, la "IA" vio su usuario, propuso un ajuste (una propuesta inválida dio 400) y la muestra se negó sin el permiso;
  - con el permiso, la muestra vino ordenada por `creado_en`;
  - la propuesta se cargó, se guardó y quedó aceptada.
- Hallazgo: filas con la misma fecha salían en orden al azar; ahora la PK desempata.
- 251 unitarias y 291 de integración en verde.

## 2026-10-29 · Sugerencias de ajustes, primera parte de la IA (P-112)
- El dueño preguntó por un MCP para que una IA escanee y recomiende configuraciones. Se aprobó el plan en tres partes: sugerencias sin IA, tokens y propuestas, y servidor MCP.
- Decisiones del dueño:
  - la IA solo propone;
  - puede ver las 1.000 filas más recientes en las conexiones donde se active;
  - los tokens tienen el acceso de quien los crea.
- Sugerencias por reglas: relaciones, filtros de combos, columnas técnicas y correos.
- Hallazgo: el filtro se descartaba al validarse solo, porque su relación también era sugerida. Ahora se valida junto con ella.
- En el navegador, una tabla con empresa_id, area_id y tipo_gasto_id dio 7 sugerencias; se aplicaron y guardaron, y no quedó ninguna.
- 246 unitarias y 285 de integración en verde.

## 2026-10-29 · Copiar ajustes entre conexiones (P-111)
- El dueño pidió copiar la configuración entre conexiones para bases idénticas en servidores distintos. Precisó que sirva también entre dos conexiones ya existentes (por ejemplo, solo las relaciones virtuales) y solo entre el mismo motor.
- Copia con vista previa y validación contra el destino; partes y tablas elegibles; esquemas mapeables. Los nombres de FK de pestañas y árbol se traducen por columnas.
- "Duplicar" crea la conexión con el mismo motor y copia sus ajustes.
- En el navegador:
  - solo se ofreció la conexión PostgreSQL como origen;
  - copiar solo "Relaciones" llevó las FK virtuales y el filtro de área y dejó el nombre propio de producto en el destino;
  - duplicar copió 2 ajustes;
  - por la API, Postgres → MySQL respondió 400.
- Hallazgo de diseño: un combo dentro de una etiqueta se estiraba hacia abajo; se corrigió para todos.
- 243 unitarias y 285 de integración en verde.

## 2026-10-29 · Combos de FK filtrados (P-110)
- El dueño mostró CentroCosto, con EmpresaID → Empresa y AreaID → Area, donde Area también tiene EmpresaID. Pidió que el combo de área ofrezca solo las de la empresa del registro y que quede bloqueado sin empresa cuando el filtro está configurado.
- Ajuste `lookupFilters`, combo con `e.<columna>`, y validación al guardar (también por la API).
- Prueba en los cuatro motores, con tablas propias de DDL común: pasó a la primera.
- En el navegador, sin FK declaradas en la base:
  - el editor propuso "Filtrar por empresa_id (area.empresa_id)";
  - el área quedó bloqueada hasta elegir la empresa;
  - la empresa 1 ofreció sus 3 áreas, y al pasar a la 2 el área se vació y ofreció las 2 suyas;
  - por la API, un área de otra empresa dio 400.
- 243 unitarias y 281 de integración en verde.

## 2026-10-29 · Combos con búsqueda (P-108)
- El dueño reportó que en el combo de Acciones no se leían las opciones y que con muchos registros costaba encontrar una. Pidió autocompletar, además de elegir, en todos los controles de ese tipo.
- Causa del texto invisible: las opciones nativas con fondo transparente se veían blancas en Windows, con el texto claro del modo noche.
- Un combo propio reemplazó a los `<select>` y `<datalist>` de toda la app, incluidas las FK (que buscan en el servidor).
- En el navegador:
  - 61 procedimientos: "subir" dejó uno y ↓ Enter eligió con el teclado;
  - el filtro Sí/No de la grilla no quedó cortado, el idioma cambió escribiendo "engl", y en Seguridad la conexión ya usada apareció desactivada.
- Hallazgo: la búsqueda de FK en el servidor distingue tildes ("lact" no encuentra "Lácteos"); quedó como P-109.

## 2026-10-29 · Navegación de la grilla (P-107)
- El dueño notó que llegar a la página 30 o a la última obligaba a pulsar "Siguiente" una y otra vez, y que las acciones quedaban al final de la grilla. Pidió además un indicador de carga.
- Paginador con primera, última, números e "Ir a página", también en la auditoría. Acciones como primera columna, fijas al desplazar.
- En el navegador, con 1.234 filas:
  - "Ir a 30" mostró desde el id 726, y "Siguiente" siguió con `after=750`;
  - la última página trajo 9 filas, y las acciones quedaron en el borde tras desplazar 400 px.
- En 1 millón de filas: sin "Última" (el total es estimado); el salto a la página 39.999 mostró el indicador y luego los ids desde 999.951.

## 2026-10-29 · Backend en .NET 10 (P-106)
- El dueño pidió pasar el backend a .NET 10. El SDK no se podía bajar (proxy); se copió de la imagen `mcr.microsoft.com/dotnet/sdk:10.0`.
- Hallazgo: el SDK 10 audita también las dependencias indirectas y marcó `System.IO.Packaging` 8.0.0 y `System.Security.Cryptography.Pkcs` 9.0.18, vulnerables. Ya estaban con .NET 8. Se fijaron a 10.0.13.
- Dos APIs obsoletas reemplazadas. `System.Net.IPNetwork` rechaza `192.168.1.5/24`; se limpia para no romper configuraciones, con prueba.
- 237 unitarias y 277 de integración en verde en `net10.0`. Duende 8 con la API en .NET 10 en vivo: login PKCE, token y renovación silenciosa.

## 2026-10-28 · README nuevo e interfaz renovada (P-105)
- El dueño fusionó los PR #33 a #36 y borró las ramas viejas (P-005). Pidió un README moderno, que se eligió bilingüe (español e inglés), y una interfaz con modo día/noche, con CSS propio y sin librería de componentes.
- README con capturas que cambian con el tema de GitHub, inicio rápido y funciones. La tabla de rutas pasó a `docs/api.md`, con las rutas nuevas.
- Tema Auto/Día/Noche sin parpadeo al cargar. Probado en el navegador: se recuerda, en Auto sigue al sistema y queda marcado el elegido.
- Íconos lucide en lugar de símbolos; Inter empaquetada; grilla con encabezado fijo; barra que en teléfono se compacta. Hallazgo: en 390 px la barra se salía de la pantalla; se resolvió ocultando textos.

## 2026-10-28 · Pendientes, lote C (P-050, P-064, P-078) y cierre
- **Redis opcional:** dos APIs reales con un Redis compartido sumaron el límite de login entre las dos (la cuarta respuesta fue 429). Con Redis apagado siguieron respondiendo en milisegundos. Probado con Redis 6.2 y Valkey 8.
- **Plugins:** hizo falta que `DatabaseEngine` dejara de ser enum. Hallazgo: el archivo de conexiones y el `connections.json` de la Fase 1 guardaban el motor como número; se siguen leyendo. En el navegador, el motor del plugin apareció en el selector, la conexión se probó (13 tablas) y se listaron sus tablas.
- **Duende 7 en vivo:** login PKCE, `at+jwt`, `scope` como lista, rol y renovación silenciosa con tokens de 70 s, a la primera en la app. Hallazgo del lado de Duende: por `http` su cookie `SameSite=None` se descarta y el login vuelve a pedir la clave. Se documentó.
- 230 unitarias y 277 de integración en verde. No queda trabajo de código; P-005 y P-097 son del dueño y P-062 se hace a pedido.

## 2026-10-28 · Pendientes, lote B (P-102, P-079, P-061, P-098)
- Nombres y ayudas por idioma en los ajustes; en el navegador, "Products" en inglés en título y menú.
- Renovación silenciosa con Keycloak sin refresh tokens (tokens de 70 s): siguió en la app pasados los 95 s. Lo que parecían dos pedidos por renovación era el `redirect_uri` dentro de la URL de autorización.
- Acciones con procedimientos en los cuatro motores al primer intento; en el navegador el editor propuso los parámetros solo (`p_id` → id) y el precio subió 710 → 712,5, con su entrada en la auditoría.
- Paginación por clave: misma página que con OFFSET; en 1 millón de filas, 8,5 s en frío → ~180 ms.

## 2026-10-28 · Pendientes, lote A (P-031, P-034, P-075, P-096, P-103, P-104)
- El dueño pidió seguir con los pendientes hasta el final. Se ordenaron en tres lotes; quedan fuera solo los que dependen del dueño o de un equipo que los pida.
- Índices únicos sin restricción en SQL Server y PG; búsqueda sin CAST en columnas de texto (se actualizó el SQL esperado de 4 pruebas).
- Tipos propios de PG como texto. Hallazgo: `CAST(inet AS text)` agrega la máscara ("10.0.0.1/32"); se usa `format('%s', …)`. `tsvector` sigue sin soporte (una prueba lo protegía).
- Bloqueo de fila al guardar con versión: carrera de 4 ediciones, gana una, en los cuatro motores.
- PostGIS con su propia imagen de pruebas; MySQL 8 con SRID 4326 respeta longitud latitud (comprobado con `ST_Latitude`).
- 212 unitarias y 260 de integración en verde.

## 2026-10-28 · Tanda 3, parte B (P-085, P-087, P-089, P-090)
- El dueño fusionó los PR #28 a #32.
- Árbol: ajuste `treeParent` y arrastrar nodos; en el navegador, mover a "Rita" bajo "Luis" funcionó y el ciclo se rechazó mostrando el motivo (al principio salía "Hay datos inválidos": se corrigió).
- N:M: ajuste `links` (selector, pestaña, nada); se descartó pasar a pestaña por defecto los puentes con columnas extra, porque rompía la prueba de un puente con una fecha opcional.
- Imágenes: miniatura en la grilla con `widget: image` y vista previa en el formulario; WKT editable en SQL Server y MySQL/MariaDB, probado en los tres; PostGIS queda en P-104.
- Editor: FK virtuales compuestas (con sucursal propone las dos columnas de su PK) y columnas que se ordenan arrastrando.
- Docker del sandbox se detuvo con el reinicio de la sesión; se levantó de nuevo.

## 2026-10-28 · Tanda 3, parte A (P-094, P-095, P-093, P-101)
- Auditoría: nombres en FK y N:M, exportación a CSV/Excel con encabezados en el idioma de la interfaz, y filtro por perfil en SQL más enmascarado de columnas (D-081).
- En el navegador: "categoria_id: Bebidas (1) → Snacks! (2)"; el CSV trae lo mismo; una usuaria con perfil no vio `etiqueta` ni `categoria_id` y el admin vio todo.
- Hallazgo de la prueba: "ana" ya tenía otro perfil de una prueba anterior y los perfiles se suman; se repitió con una usuaria nueva.
- Seguridad: copiar perfil y listas de tablas y columnas con la regla `*` (13 tablas sugeridas).
- Mensajes del servidor traducidos en el frontend con plantillas (D-080); lint revisa que las traducciones tengan las mismas variables.
- Abierto: P-103 (edición con solo columnas ocultas se lista vacía).

## 2026-10-28 · Tanda 2: operación (P-082, P-092, P-074, P-086)
- El dueño pidió hacer las tandas 2 y 3 de una vez; la 2 va en el PR #31 y la 3 en los siguientes.
- Avisos entre instancias con contadores en la metadata (D-077): probado con dos APIs reales sobre la misma metadata; B vio el ajuste guardado en A en 1,7 s y su borrado.
- Usuarios propios activos en caché 10 s; perfil nuevo y usuario desactivado se notan en la otra instancia en ~1 s (prueba unitaria con dos instancias).
- Cambios de esquema solos (D-078): relectura cada hora y, si falta una tabla o columna, 409 con relectura inmediata; probado en los cuatro motores.
- `healthcheck` de la propia app y `HEALTHCHECK` en el Dockerfile (D-079).
- 203 unitarias y 245 de integración en verde.

## 2026-10-28 · Interfaz en inglés y ruso (P-100)
- Pedido del dueño antes de seguir con la tanda 2: inglés y ruso intercambiables desde la interfaz, con el formato de fechas de cada idioma.
- Sistema propio y liviano (D-076): el español del código es la clave, `en.ts`/`ru.ts` traducen 304 textos, plurales con `Intl.PluralRules`, y `npm run lint` falla si falta alguna traducción.
- Hallazgo en el navegador: `es-CL` escribe las fechas 28-10-2026 y la hora con a. m./p. m.; para fechas se usa `es-ES`, y `es-CL` queda para los números.
- Navegador: login, grilla, formulario y auditoría en los tres idiomas; el idioma se recuerda al recargar.
- El dueño confirmó: el idioma inicial sale del navegador y, si no es español, inglés ni ruso, se usa inglés.
- **Abierto:** traducir los mensajes del servidor (P-101).

## 2026-10-28 · Tanda 1 de mejoras, parte B (P-088, P-030)
- Importar en modo "agregar y actualizar por clave": búsqueda de claves por lotes, error claro si la clave es identity y no existe, y conteo de nuevas y actualizadas en la vista previa.
- FK de varias columnas: etiqueta, `/lookup` con la clave completa y combo único en el formulario. Bases de ejemplo con `sucursal`/`bodega`.
- Al preparar esta parte se vio que el PR #28 tenía rotas dos pruebas unitarias (SQL esperado sin el filtro por etiqueta). Se corrigió en el #28.
- 197 unitarias y 239 de integración en verde; navegador: bodega con sucursal compuesta e importación con 1 nueva y 1 actualizada.

## 2026-10-28 · Tanda 1 de mejoras, parte A (P-032, P-033, P-073, P-084)
- El dueño fusionó los PR #23 a #27.
- Filtro de una FK por el texto que se muestra o por el id.
- Errores de permisos de la base traducidos en los cuatro motores, también al leer.
- Fechas día/mes/año fijas y nombres visibles de las tablas en las relaciones.
- La prueba con un usuario de base de solo lectura destapó que **MariaDB oculta `TABLE_CONSTRAINTS`** a ese usuario: la app no veía las PK y dejaba las tablas de solo lectura. Las claves se leen ahora de `KEY_COLUMN_USAGE`.
- 197 unitarias y 231 de integración en verde (mínimas y actuales).
- **Siguiente:** parte B, importar con actualización (P-088) y FK compuestas (P-030).

## 2026-10-28 · Fase 5: avisos por correo (Q-005 → D-073)
- El dueño respondió que no habrá lógica propia al guardar (P-060 descartado), y que sí quiere correos, apagados por defecto y habilitables por tabla.
- Ajuste `notify` por tabla (destinatarios, operaciones, valores) y envío SMTP en segundo plano, a partir de las mismas entradas de la auditoría.
- Pruebas:
  - validación del ajuste;
  - texto del correo;
  - envío real contra un SMTP falso;
  - en la batería, solo se avisa lo configurado;
  - en el navegador, el correo llegó con el cambio y el enlace.
- 194 unitarias y 223 de integración en verde.
- **Siguiente:** pendientes menores.

## 2026-10-28 · Fase 4: tablas grandes (P-052)
- El dueño respondió Q-004: tablas de hasta ~1 millón de filas, en general no transaccionales, "pero tengámoslo cubierto" → D-072.
- Se midió con una tabla de 1 millón en PostgreSQL 16. Paginar por número va bien (la última página tarda 0,2 s), así que no hace falta la paginación por clave (queda en P-098).
- Con carga apareció el problema real: las búsquedas de texto recorren la tabla, ocupan el pool y traban lo demás (abrir un registro llegaba a 5 s).
- Se agregó:
  - total estimado del catálogo y conteo con tope, por dialecto;
  - el total sin contar cuando la página llega incompleta;
  - un turno de 4 consultas pesadas por conexión, con 503 a los 30 s.
  - Con eso, abrir un registro queda en p95 13 ms con 50 usuarios buscando.
- 190 unitarias y 219 de integración en verde.

## 2026-10-28 · Fase 4: observabilidad y pruebas de carga (P-053)
- OpenTelemetry 1.19 solo con `Telemetry:OtlpEndpoint`; trazas y métricas de ASP.NET Core, runtime y los drivers de PostgreSQL y MySQL (D-071). La 1.9 tenía una vulnerabilidad conocida y la 1.20 tiene menos de dos semanas.
- Log de solicitudes lentas sin la query.
- Prueba con un receptor OTLP falso: llegan trazas y métricas a su ruta. Una versión que dependía del vaciado al apagar fallaba de vez en cuando: ahora exporta cada 100 ms y espera.
- Script k6 en `tests/load/crud.js`. No se pudo bajar k6 en el entorno, así que se midió con Node: 200 usuarios, 2.255 solicitudes/s, p95 83 ms.
- 190 unitarias en verde.
- **Siguiente:** P-052, tablas grandes (pregunta Q-004 al dueño).

## 2026-10-28 · Fase 4: tope del pool y límites de uso (P-051, P-091)
- Cada proveedor agrega a la cadena el máximo de conexiones y el cierre de inactivas con los nombres de su driver, sin pisar lo que trae la cadena (D-070).
- Límites con el limitador de ASP.NET Core: por usuario, para exportar e importar, y para el login por IP. Los health checks quedan fuera.
- 187 unitarias y 215 de integración en verde.
- **Siguiente:** P-053, observabilidad.

## 2026-10-28 · Fase 3: concurrencia optimista (P-046), Fase 3 completa
- El dueño fusionó los PR #18 a #22.
- Cada fila trae `__version` (hash de sus valores visibles). Editar y borrar la devuelven en `If-Match`, y si cambió responde 409 con la fila actual (D-069).
- Es igual en los cuatro motores: no depende de `rowversion` ni de `xmin`.
- Formulario con aviso de conflicto (qué cambió, ver lo actual o guardar igual); el borrado desde una lista vieja también se rechaza.
- 182 unitarias y 215 de integración en verde; recorrido con dos sesiones en el navegador.
- **Siguiente:** Fase 4 (P-051 a P-053).

## 2026-10-28 · Fase 3: auditoría de cambios (P-044)
- Tabla `mnt_audit` (migración `005_audit`) en SQLite, Postgres y SQL Server, más un almacén en archivo.
- El CRUD registra altas, ediciones (solo lo que cambió), bajas, filas importadas y cambios N:M, después de confirmar el cambio (D-068).
- Permiso `ViewAudit`, página **Auditoría** con filtros e **Historial** desde cada registro. Limpieza automática a los 365 días.
- La auditoría destapó un bug anterior: el formulario reenviaba todos los campos al editar, y las fechas perdían los segundos. Ahora solo envía lo que el usuario cambió.
- 179 unitarias y 211 de integración en verde (versiones mínimas y actuales); recorrido en navegador.
- **Siguiente:** P-046, concurrencia optimista.

## 2026-10-28 · Fase 3: perfiles y permisos (P-040, P-042)
- Perfiles en la metadata (`mnt_profile`, `mnt_user`, migración `004_security`), con roles, usuarios, permisos de la app y permisos por conexión, tabla y columna.
- Se aplican recortando el esquema por usuario y con 403 en las operaciones (D-067). Sin perfiles, todo sigue abierto.
- Login de usuarios propios; desactivarlos corta su sesión. Nuevo permiso `ManageSecurity` (rol `mnt-seguridad`).
- Pantalla **Seguridad**; la UI oculta Nuevo, Editar, Eliminar, Exportar e Importar según el perfil.
- Las pruebas encontraron un bug: editar un usuario sin mandar el nombre lo borraba. Corregido.
- 172 unitarias y 205 de integración en verde; recorrido en navegador.
- **Siguiente:** P-044, auditoría de cambios.

## 2026-10-28 · Fase 3: decisiones de seguridad, migraciones y secretos
- El dueño respondió el apartado de seguridad:
  - usuarios propios y del proveedor de identidad;
  - permisos por conexión, tabla y columna;
  - sin filtros de fila;
  - auditoría solo de cambios por 1 año;
  - permiso propio para administrar la seguridad.
  - Quedó en D-062 a D-066; Q-007 queda respondida y P-043 descartado.
- P-081: bloqueo de sesión al migrar (D-060). La prueba con cuatro "instancias" falla sin el bloqueo (verificado) y pasa con él.
- P-045: `${env:MNT_…}` y `${file:…}` en las cadenas de conexión, con prefijo y carpeta permitidos (D-061).
- 158 unitarias y 201 de integración en verde.
- **Siguiente:** P-040/P-042, perfiles y permisos.

## 2026-10-27 · Fase 2: tipos pendientes (Fase 2 completa)
- Archivos: tamaño en el listado, descarga con tipo deducido, subida en base64 con tope `Crud:FileMaxMegabytes`.
- Arreglos de PostgreSQL editables como lista; `geography`/`geometry`/`hierarchyid` y espaciales de MySQL como texto de solo lectura (D-059).
- Bugs encontrados por la batería: Npgsql no lee un `int4[]` con nulos (se lee como `text[]`); SQL Server no acepta un `NULL` sin tipo en `varbinary` (`CAST` en el dialecto).
- Bases de ejemplo con `icono` en categoría, `telefonos text[]` (PG), `Ubicacion geography` y `Nodo hierarchyid` (SQL Server), `ubicacion POINT` (MySQL).
- 155 unitarias y 199 de integración en verde, versión mínima y actual. Probado en navegador.
- **Fase 2 completa.** **Siguiente:** conversar el apartado de seguridad (Fase 3). **Nuevo pendiente:** P-090.

## 2026-10-27 · Fase 2: árboles
- `SelfReference` en la metadata, `GET /tree` por niveles (raíces = sin padre o con padre inexistente) y validación de ciclos al editar, con SQL portable (D-058).
- UI: Grilla/Árbol con la vista en la URL, "＋ hijo" con el jefe fijo. Encontrado en el navegador: tras guardar se perdía la vista de árbol; ahora queda en `?vista=arbol`.
- 149 unitarias y 191 de integración en verde, versión mínima y actual.
- **Siguiente:** P-018, tipos pendientes. **Nuevo pendiente:** P-089.

## 2026-10-27 · Fase 2: exportar e importar
- El dueño pidió sumar JSON y XML a CSV y Excel. Se exportan e importan los cuatro (D-057).
- Exportación por streaming (`QueryUnbufferedAsync`), con tope y conteo previo; CSV protegido contra inyección de fórmulas; Excel con OpenXml SDK.
- Importación sin estado en dos pasos (vista previa y confirmación), asociación automática sin tildes, errores por línea y una sola transacción.
- Verificado: 5.000 productos validados en 0,4 s e importados en 2,3 s; exportación de 5.000 filas en menos de 0,4 s en cada formato.
- Entorno: Docker quedó con un PID viejo y no arrancaba; se borró `/var/run/docker.pid`.
- 148 unitarias y 187 de integración en verde.
- **Siguiente:** P-016 o P-018. **Nuevo pendiente:** P-088 (importar actualizando por clave).

## 2026-10-27 · Fase 2: relaciones N:M
- Detección de tablas puente (dos FK que forman la PK, sin otras obligatorias); `manyToMany` en la metadata (D-056).
- `GET`/`PUT /row/links/{relación}`: lista completa sincronizada en una transacción. Selector múltiple en el formulario, guardado junto con el registro; el puente ya no sale como pestaña.
- Bug de UI encontrado en el navegador: la lista de opciones quedaba abierta y tapaba "Guardar"; ahora se cierra al elegir.
- 136 unitarias y 167 de integración en verde, versión mínima y actual.
- **Siguiente:** P-017, exportar e importar. **Nuevo pendiente:** P-087.

## 2026-10-27 · Fase 2: maestro-detalle
- Al editar un registro, una pestaña por tabla hija con su grilla filtrada por la FK; "Nuevo" con la FK fija y vuelta al padre; un alta con hijas queda en su edición (D-055).
- Filtro exacto `e.<col>` en la API (la grilla de hijas lo usa; sirve para FK compuestas y textos). Ajuste `children` y su selector en el editor.
- Docker del entorno remoto se detuvo a mitad de la batería; reiniciado, todo en verde.
- 133 unitarias y 163 de integración en verde. Probado en navegador (pedido → líneas, categoría → productos).
- **Siguiente:** P-015, relaciones N:M.

## 2026-10-27 · Fase 2: cambios de esquema
- Foto de referencia por conexión (`mnt_schema_snapshot`, migración `003`) y comparación al refrescar: tablas, columnas, tipos, nulos, PK y FK (D-054).
- `GET /diff` y `POST /diff/accept` (permiso `ConfigureTables`). Aviso en la UI con detalle; en el editor se quitan los ajustes de columnas que ya no existen.
- Batería común: crear una tabla, borrar y agregar columnas en la base real; aviso correcto y el mantenedor sigue funcionando, en los cuatro motores.
- 131 unitarias y 159 de integración en verde, versión mínima y actual. Probado en navegador.
- **Siguiente:** P-014, maestro-detalle. **Nuevo pendiente:** P-086 (detectar cambios sin pulsar ↻).

## 2026-10-27 · Fase 2: permisos por acción
- El dueño respondió Q-006 y P-083: configurar mantenedores y gestionar conexiones deben tener **cada uno su rol**, asignable a un perfil o usuario. El apartado de seguridad propio se conversará después.
- Permisos `ConfigureTables` y `ManageConnections`, otorgados a roles en `Auth:Permissions` (por defecto `mnt-configurador` y `mnt-conexiones`); los administradores tienen todos (D-053, reemplaza a D-050).
- `/api/auth/me` devuelve `permissions`; la UI oculta Configurar y Nueva/Editar/Eliminar conexión a quien no los tiene.
- 125 unitarias en verde.
- **Siguiente:** P-013, diferencias de esquema. **Pendiente de conversar:** el apartado de seguridad (P-040/P-042).

## 2026-10-27 · Fase 2: editor de ajustes
- PR #11 fusionado. Nuevo botón **⚙ Configurar** (solo admin) con pestañas Tabla y Columnas, vista previa en vivo y "Restablecer a lo automático".
- Endpoint `.../overrides/{schema}/{table}/preview`: aplica sin guardar y devuelve tabla y errores (D-052).
- Probado en navegador (escritorio y teléfono): guardar, reabrir, error en vivo y restablecer.
- **Siguiente:** P-013, diferencias de esquema. **Nuevo pendiente:** P-085 (FK virtuales compuestas y arrastrar columnas).

## 2026-10-27 · Fase 2: ajustes por tabla y columna
- `OverrideApplier` en Core aplica un JSON por tabla sobre el esquema leído; dos cachés (leído y combinado) para aplicar sin volver a leer la base (D-049).
- Migración `002_overrides`, `SqlOverrideStore` y `FileOverrideStore`; endpoints `/api/meta/{conn}/overrides`, que solo cambia un administrador (D-050, provisional hasta Q-006).
- Reglas en el servidor: obligatoria, patrón con tiempo máximo, opciones, rango y largo. Columnas ocultas fuera de lecturas, escrituras y búsqueda (D-051).
- UI: grupos, etiquetas, orden, ayudas, controles (`textarea`, `email`, `money`…) y aviso de ajustes que no aplican. Guía en `docs/ajustes.md`.
- 123 unitarias y 153 de integración en verde en los cuatro motores, versión mínima y actual. Probado en navegador.
- **Siguiente:** P-012, el editor en la UI. **Nuevos pendientes:** P-082 (ajustes con varias instancias), P-083 (conexiones solo admin), P-084.

## 2026-10-27 · Fase 2: metadata configurable
- Nuevo `Mantenedores.Metadata`: conexiones en SQLite (por defecto), Postgres o SQL Server, con un migrador propio en vez de DbUp (D-047).
- Llaves de Data Protection en la base (`KeyStore=Database`) y certificado opcional: dos instancias comparten conexiones (probado).
- Importación automática del `connections.json` de la Fase 1, que se vuelve a cifrar con las llaves nuevas (D-048).
- 106 unitarias y 139 de integración en verde.
- **Siguiente:** P-011, los overrides. **Nuevo pendiente:** P-081, el bloqueo de migraciones con varias instancias.

## 2026-10-27 · Fase 2: login configurable
- `Auth:Mode` = `Local` o `Jwt`, con Authority OIDC, MetadataAddress o SigningKey. Claims normalizados (nombre y roles, incluso anidados), `AdminRoles`, `/api/auth/config` y `/api/auth/me` (D-046). `Auth:JwtKey` solo se exige en `Local`.
- UI: login PKCE con `oidc-client-ts` cuando hay `ClientId`; modo proxy sin login; redirect bajo subruta.
- Probado en vivo con Keycloak 26 (`samples/keycloak/realm-demo.json`): login, roles, API y salida. Bug corregido: al salir se borraba el usuario antes de redirigir, y Keycloak pedía confirmación.
- 10 pruebas nuevas en `AuthTests` (emisor en memoria): audiencia, firma, vencimiento, scope, roles anidados y por defecto, configuración inválida.
- **Pendiente:** prueba en vivo con Duende (P-078). **Siguiente:** P-010, la metadata configurable.

## 2026-10-27 · Bloque M.3: MySQL y MariaDB
- Nuevo `Mantenedores.Providers.MySql` (MySqlConnector) para ambos motores, con sus particularidades en el proveedor (D-045).
- Core: UUID de la PK generado por la app si el motor no puede devolverlo (D-044); fechas con zona devueltas como UTC.
- `samples/mysql/01_demo.sql`, equivalente a las otras bases. `docker-compose` con `mysql-demo`.
- La batería común pasó **a la primera** en MySQL 8.0/8.4 y MariaDB 10.6/11.4: 137 pruebas de integración en verde en los cuatro motores.
- CI con MySQL y MariaDB en la matriz; UI con el motor nuevo. Probado en navegador.
- **Bloque M terminado.** Siguiente: Fase 2 (P-041 y P-010).

## 2026-10-27 · Bloque M.1: abstracciones del dialecto
- Estrategia de inserción en tres pasos (devuelta, último id, provista) y prefijo de parámetros en el dialecto (D-040).
- Alias cortos para etiquetas de FK; números de más de 28 dígitos como texto (D-041).
- Capacidades por columna: ordenable y filtrable (D-042).
- Escape de LIKE neutro `!`; versión mínima verificada al conectar, con datos del servidor (D-043).
- Bug encontrado por la batería: Npgsql no podía leer un enum creado después de su primera conexión. Ahora los enums se leen como texto.
- Base de ejemplo: `monto_total` de 38 dígitos en `pedido`.
- 70 unitarias y 65 de integración en verde (PG 12/16, SQL Server 2017/2022). Probado en navegador.
- **Siguiente:** M.3, el proveedor MySQL/MariaDB.

## 2026-10-27 · Cierre Fase 1: errores amigables y despliegue
- Errores de restricciones traducidos por código del motor y nombre de la restricción, sin depender del idioma del servidor (D-038). Probado igual en PG 12/16 y SQL Server 2017/2022.
- `UseWindowsService` y `UseSystemd`. Bug corregido: la carpeta de datos relativa se resolvía contra el directorio de trabajo (D-039).
- Nueva `docs/despliegue.md`: Docker, Compose, Kubernetes, nginx, IIS, servicio de Windows, systemd, respaldo y `GRANT` mínimos.
- **Fase 1 cerrada.** Siguiente: bloque M (P-020 a P-029), empezando por las abstracciones del dialecto.

## 2026-10-27 · Cierre Fase 1: subruta, proxy y health checks
- `Hosting:PathBase` y `Hosting:ForwardedHeaders`. El `index.html` se sirve con `<base href>` ajustada y el frontend usa rutas relativas (D-037).
- Bug detectado por las pruebas: sin `UseRouting()` después de `UsePathBase`, todo caía en el `index.html`, incluso la API sin token.
- Health checks `/api/health/live` y `/ready`. Nuevo `docs/configuracion.md` con todas las claves.
- Probado en navegador bajo `/mantenedores` contra PostgreSQL 12.
- **Siguiente:** P-003 (errores amigables) y P-004 (guía de despliegue).

## 2026-10-27 · Cierre Fase 1: pruebas de integración y versiones mínimas
- El dueño confirmó las versiones mínimas (Q-008 → D-035) y pidió publicarlas en el README ("Requisitos de instalación").
- Nuevo proyecto `Mantenedores.IntegrationTests`: batería común (`ProviderContractTests`) que hereda cada motor. Son 57 pruebas, en verde contra PG 12/16 y SQL Server 2017/2022.
- Problemas que encontró la batería y se corrigieron:
  - La base de ejemplo de PG no funcionaba en PG 12 (faltaba `pgcrypto`).
  - Los motores devolvían fechas y horas en formatos distintos (D-036).
  - Se agregó un trigger a la base de PG para igualarla a la de SQL Server.
- CI: unitarias separadas, matriz de integración (versión mínima y actual), job que compila y arranca la imagen Docker.
- **Siguiente:** P-007 y P-008 (subruta, proxy y health checks), luego P-003 y P-004.

## 2026-10-27 · Oracle fuera de alcance
- El dueño decidió dejar Oracle fuera y mantener MySQL/MariaDB (D-034 reemplaza a D-029).
- P-026 queda ⛔. Se quitó `EmptyStringIsNull` de P-024. El prefijo de parámetros en el dialecto se mantiene, porque es barato y deja la puerta abierta.
- El análisis de Oracle se conservó al final de `docs/motores.md`.
- Bloque M: 2,5 semanas. Total: 17–18 semanas.

## 2026-10-27 · MySQL/MariaDB y Oracle en el plan
- El dueño pidió incluir MySQL y Oracle desde ya para no generar deuda técnica.
- Se registraron D-029 a D-033: cuatro familias de motores con versiones mínimas, drivers, el dialecto absorbe las diferencias, qué es un "esquema" en cada motor y la batería común de pruebas.
- Nuevo bloque **M** (4 semanas) antes de la Fase 2, con P-020 a P-029. Total: 19–20 semanas.
- Se creó `docs/motores.md` con las diferencias técnicas y la guía para agregar un motor.
- Se detectaron 4 supuestos del código actual que fallan en otros motores (prefijo `@`, alias largos, escape `\`, clave como filas) y uno que ya puede fallar en PG (`numeric` grande, P-023).
- **Abierto:** Q-008 (versiones de cada motor en el equipo).

## 2026-10-27 · Decisiones de entorno
- El dueño respondió Q-001 a Q-003:
  - Despliegue en cualquier entorno (servidor propio o nube).
  - Uso por un solo equipo interno.
  - Autenticación libre con cualquier JWT, como Duende IdentityServer.
- Se registraron D-022 a D-028. Principio rector: **todo por configuración y sin dependencias externas por defecto**. La metadata puede estar en SQLite, Postgres o SQL Server; Redis y los vaults son opcionales.
- La autenticación configurable (P-041) se adelantó a la Fase 2. Se agregaron P-007, P-008, P-038, P-039, P-063 y P-064.
- Se ajustaron `docs/siguientes-pasos.md` y `docs/plan.md`. Fase 2: 5–6 semanas; total: 15–16 semanas.

## 2026-10-27 · Memoria del proyecto y hoja de ruta
- Se creó `CLAUDE.md` y la carpeta `docs/memoria/` (estado, decisiones, pendientes, preguntas, convenciones, bitácora) (D-021).
- Se escribió `docs/siguientes-pasos.md` con el cierre de la Fase 1 y el detalle de las Fases 2 a 5.
- Se registraron 21 decisiones tomadas en la Fase 1 y 7 preguntas abiertas para el dueño.
- **Abierto:** responder Q-001 a Q-003 antes de P-010; fusionar el PR de documentación (P-006).

## 2026-10-27 · Fase 1: estructura base
- Análisis de viabilidad, escalabilidad y plan por fases (`docs/plan.md`).
- Se construyó el backend .NET 8 (Core, proveedores SQL Server y Postgres, API), el frontend React, las bases de ejemplo, Docker y la CI.
- Probado contra PostgreSQL 16 y SQL Server 2022 reales, y en navegador con Playwright.
- Bug encontrado por las pruebas y corregido: la conversión de enteros siempre devolvía `long`.
- PR #1 fusionado en `main` con la CI en verde.
- El repo no tenía `main`: se creó con un commit inicial vacío y se cambió la rama por defecto a `main`.
- **Abierto:** validar `docker build` (P-001) y pruebas de integración (P-002).
