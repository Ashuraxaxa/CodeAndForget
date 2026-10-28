# Preguntas abiertas

Decisiones que necesitan respuesta del dueño del proyecto. Cuando se respondan, se registran
como `D-xxx` en [decisiones.md](decisiones.md) y se borran de aquí.

**Respondidas el 2026-10-27:** Q-001 → D-022 (cualquier entorno), Q-002 → D-027 (equipo interno), Q-003 → D-024 (cualquier emisor JWT/OIDC), Q-008 → D-035 (versiones mínimas), Q-006 → D-053 (un permiso por acción, otorgado a roles). **2026-10-28:** apartado de seguridad → D-062 a D-066; Q-007 → D-065 (auditoría solo de cambios, 1 año).
Ninguna de las abiertas bloquea el trabajo inmediato.

---

### Q-004 · ¿Cuántas bases y de qué tamaño?
- **Datos útiles:** cantidad de conexiones, tablas por base y filas de la tabla más grande.
- **Qué define:** cuándo hace falta la Fase 4 (P-050 a P-053) y si P-052 se adelanta.

### Q-005 · ¿Habrá lógica de negocio propia al guardar?
- **Ejemplos:** recalcular totales, enviar un correo o validar contra otro sistema.
- **Qué define:** si los hooks (P-060) se adelantan a la Fase 2 o 3.
