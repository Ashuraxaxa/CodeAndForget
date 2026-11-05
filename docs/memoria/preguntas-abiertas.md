# Preguntas abiertas

Decisiones que necesitan respuesta del dueño del proyecto. Cuando se respondan, se registran
como `D-xxx` en [decisiones.md](decisiones.md) y se borran de aquí.

**Respondidas el 2026-10-27:** Q-001 → D-022 (cualquier entorno), Q-002 → D-027 (equipo interno), Q-003 → D-024 (cualquier emisor JWT/OIDC), Q-008 → D-035 (versiones mínimas), Q-006 → D-053 (un permiso por acción, otorgado a roles). **2026-10-28:** apartado de seguridad → D-062 a D-066; Q-007 → D-065 (auditoría solo de cambios, 1 año); Q-004 → D-072 (tablas de hasta ~1 millón de filas); Q-005 → D-073 (sin lógica propia al guardar; avisos por correo opcionales).

---

Q-009, Q-010 y Q-011 se respondieron el 2026-10-30 → D-105.

## Dashboards (plan en [../dashboards.md](../dashboards.md))

### Q-012 · ¿El tope de dashboards es por instalación o por usuario?
Gratis 1, Pro 5, Empresa sin tope. Se propone **por instalación**, como conexiones y usuarios.

### Q-013 · ¿Se comparte con perfiles, o también con usuarios sueltos?
Se propone solo con perfiles (privado por defecto), con "puede ver" o "puede editar".

### Q-014 · ¿Tope de tarjetas por dashboard?
Se propone 6 en Gratis, 20 en Pro y 50 en Empresa.

### Q-015 · ¿Otras tarjetas en la primera versión?
Se proponen número, barras, líneas, torta y tabla top N. Fuera por ahora: mapa y tabla dinámica.
