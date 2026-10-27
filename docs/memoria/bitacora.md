# Bitácora

Una entrada por sesión, la más reciente arriba. Breve: qué se hizo, qué se decidió y qué quedó abierto.

---

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
