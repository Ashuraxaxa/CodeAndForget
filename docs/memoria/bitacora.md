# Bitácora

Una entrada por sesión, la más reciente arriba. Breve: qué se hizo, qué se decidió y qué quedó abierto.

---

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
