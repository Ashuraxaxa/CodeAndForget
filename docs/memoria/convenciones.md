# Convenciones

## Estructura del código

| Proyecto | Responsabilidad | No debe |
|---|---|---|
| `backend/src/Mantenedores.Core` | Modelo de esquema, `QueryBuilder`, conversión de valores, servicios de esquema y CRUD | Referenciar drivers (SqlClient, Npgsql) ni ASP.NET |
| `backend/src/Mantenedores.Providers.*` | Consultas de catálogo, mapeo de tipos y dialecto de cada motor | Tener lógica de negocio o de HTTP |
| `backend/src/Mantenedores.Api` | Endpoints, autenticación, almacén de conexiones, manejo de errores | Armar SQL a mano |
| `frontend/src/api` | Cliente HTTP y tipos que reflejan la API | Tener lógica de pantallas |
| `frontend/src/pages`, `components` | Pantallas y controles generados desde la metadata | Conocer tablas concretas |

**Regla de oro:** ninguna pantalla ni endpoint conoce tablas concretas. Todo sale de la metadata.

**Regla multi-motor (D-031):** en Core no hay `if (motor == X)`. Toda diferencia entre motores es una capacidad del dialecto o del proveedor, y se documenta en `docs/motores.md`. Todo cambio en Core o en un proveedor debe pasar la batería común en todos los motores (D-033).

## Seguridad (ver D-004)

- Nombres de tablas y columnas **solo** desde `TableInfo` / `ColumnInfo`, y **siempre** con `dialect.Quote(...)`.
- Valores **siempre** como parámetros. Nunca interpolar valores en el SQL.
- Toda regla nueva del `QueryBuilder` lleva una prueba en `QueryBuilderTests` con el SQL exacto esperado.
- No devolver nunca la cadena de conexión en una respuesta de la API.
- Nada de credenciales reales en el repo.

## Backend (.NET)

- .NET 10, `Nullable` habilitado y namespaces de archivo.
- Identificadores en inglés; comentarios, mensajes y textos de error en español.
- Errores de negocio con las excepciones de `Mantenedores.Core.Errors`. `ErrorHandler` las traduce a ProblemDetails:
  - `ValidationException` → 400 con errores por campo.
  - `NotFoundException` → 404.
  - `OperationNotAllowedException` → 400.
  - `DbException` → 409.
- Pruebas con xUnit; nombres en español y descriptivos (`Rechaza_columnas_que_no_estan_en_la_metadata`).
- Antes de hacer push: `dotnet build Mantenedores.sln -warnaserror` (igual que la CI: una advertencia rompe el build) y `dotnet test` en verde.

## Frontend (React)

- TypeScript estricto. Los tipos de `src/api/types.ts` reflejan los modelos de la API: si cambia la API, se actualizan.
- Datos del servidor con TanStack Query. Claves de caché: `['meta', conn, schema, table]`, `['rows', ...]`, `['lookup', ...]`.
- `localStorage` solo para el token, siempre dentro de `try/catch`.
- Debe funcionar en pantallas de teléfono (≥ 360 px).
- Antes de hacer push: `npm run lint` y `npm run build` sin errores.

## Git y PR (ver D-019)

- Ramas: `feature/<tema>`, `fix/<tema>`, `docs/<tema>`, siempre desde `main` actualizado.
- Commits y PR en español. Título corto en imperativo o descriptivo.
- Un PR = un tema. Incluye la actualización de `docs/memoria/` que corresponda.
- El PR describe qué cambió, cómo se probó y qué quedó pendiente.
- No se reescribe historia publicada (no force-push ni rebase de ramas compartidas).

## Documentación

- Decisiones nuevas → `docs/memoria/decisiones.md` con su ID, el porqué y la alternativa descartada.
- Trabajo nuevo o descubierto → `docs/memoria/pendientes.md` con su ID y prioridad.
- Dudas que solo puede resolver el dueño → `docs/memoria/preguntas-abiertas.md`.
- Al terminar una sesión → una entrada en `docs/memoria/bitacora.md`.
