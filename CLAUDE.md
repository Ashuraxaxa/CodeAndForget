# CLAUDE.md

Servicio de **mantenedores dinámicos**: se conecta a SQL Server, PostgreSQL, MySQL o MariaDB
(Oracle fuera de alcance, D-034), lee su esquema y genera el CRUD de cada tabla.
Backend .NET 8 + Dapper, frontend React + Vite. Diferencias entre motores: `docs/motores.md`.

## Antes de cualquier tarea: leer la memoria del proyecto

Leer **siempre primero**, en este orden:

1. `docs/memoria/estado.md`: qué está hecho, qué está en curso y en qué rama.
2. `docs/memoria/pendientes.md`: backlog con prioridades (IDs `P-xxx`).
3. `docs/memoria/preguntas-abiertas.md`: decisiones que esperan respuesta del dueño del proyecto (`Q-xxx`).
4. `docs/memoria/decisiones.md`: decisiones tomadas y su porqué (`D-xxx`). No contradecirlas sin registrar una nueva.
5. `docs/memoria/convenciones.md`: reglas de código, seguridad, git y documentación.

Contexto amplio: `docs/plan.md` (visión y viabilidad) y `docs/siguientes-pasos.md` (hoja de ruta detallada).

## Al terminar una tarea: actualizar la memoria

- `estado.md`: lo que cambió (fecha, rama, PR).
- `pendientes.md`: marcar lo terminado y agregar lo descubierto.
- `decisiones.md`: toda decisión nueva de diseño, con su alternativa descartada.
- `preguntas-abiertas.md`: mover a `decisiones.md` las que se respondan.
- `bitacora.md`: una entrada corta por sesión.

La actualización de la memoria va en el mismo PR que el cambio que la motiva.

## Comandos

```bash
cd backend && dotnet build && dotnet test tests/Mantenedores.Tests       # backend + pruebas unitarias
cd backend && dotnet test tests/Mantenedores.IntegrationTests            # batería común contra bases reales (Docker; versiones mínimas por defecto)
cd backend && dotnet run --project src/Mantenedores.Api   # API en :5080 (admin / admin en Development)
cd frontend && npm install && npm run dev            # UI en :5173 (proxy /api → :5080)
cd frontend && npm run lint && npm run build
docker compose up --build                            # app + bases de ejemplo en :8080
```

## Reglas que no se negocian

- **Todo se adapta al entorno por configuración** (D-022): nada de dependencias obligatorias de una nube o de un proveedor de identidad; cada pieza nueva tiene un valor por defecto que funciona sin servicios externos y se documenta en `docs/configuracion.md`.
- **Diferencias entre motores solo en el dialecto o el proveedor**, nunca con `if (motor == X)` en Core; y todo motor debe pasar la batería común de pruebas (D-031, D-033).
- Nombres de tablas y columnas **solo** desde la metadata leída del catálogo; **siempre** delimitados con `ISqlDialect.Quote`. Valores **siempre** como parámetros (ver D-004).
- **Versiones mínimas** SQL Server 2016, PostgreSQL 12, MySQL 8.0, MariaDB 10.6 (D-035): no usar funciones posteriores; la batería común corre contra la mínima y la actual.
- Nada de credenciales reales en el repo. Las de `appsettings.Development.json` y `docker-compose.yml` son solo de demo.
- Trabajar en una rama y abrir PR hacia `main`; no reescribir historia ya publicada (ver `convenciones.md`).
