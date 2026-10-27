# Memoria del proyecto

Esta carpeta es la fuente de verdad sobre **dónde está el proyecto y por qué es como es**.
Cualquier persona (o asistente) que retome el trabajo debe leerla antes de tocar código.

| Archivo | Qué contiene | Cuándo se actualiza |
|---|---|---|
| [estado.md](estado.md) | Foto actual: hecho, en curso, ramas, PR, entorno | Al cerrar cada tarea |
| [pendientes.md](pendientes.md) | Backlog con ID, prioridad y estado | Al terminar o descubrir trabajo |
| [preguntas-abiertas.md](preguntas-abiertas.md) | Decisiones que esperan respuesta del dueño | Al surgir o responderse una |
| [decisiones.md](decisiones.md) | Registro de decisiones (qué, por qué, alternativas) | Al tomar una decisión de diseño |
| [convenciones.md](convenciones.md) | Reglas de código, seguridad, git y docs | Al acordar una regla nueva |
| [bitacora.md](bitacora.md) | Historial breve por sesión | Al final de cada sesión |

Documentos relacionados fuera de esta carpeta:
- [../plan.md](../plan.md): viabilidad, escalabilidad y fases (visión general).
- [../siguientes-pasos.md](../siguientes-pasos.md): hoja de ruta detallada con criterios de "listo".

## Identificadores

- `D-xxx`: decisión tomada. Nunca se borra; si cambia, se marca **Reemplazada por D-yyy**.
- `P-xxx`: pendiente. Al terminar se marca ✅ con la fecha y el PR.
- `Q-xxx`: pregunta abierta. Al responderse se convierte en una `D-xxx`.

## Reglas para mantenerla útil

- Frases cortas y concretas; enlazar archivos con su ruta (`backend/src/...`).
- Registrar el **porqué**, no solo el qué: es lo que se olvida primero.
- Si algo en el código contradice la memoria, gana el código, pero se corrige la memoria en el mismo PR.
