# Preguntas abiertas

Decisiones que necesitan respuesta del dueño del proyecto. Cuando se respondan, se registran
como `D-xxx` en [decisiones.md](decisiones.md) y se borran de aquí.

**Respondidas el 2026-10-27:** Q-001 → D-022 (cualquier entorno), Q-002 → D-027 (equipo interno), Q-003 → D-024 (cualquier emisor JWT/OIDC), Q-008 → D-035 (versiones mínimas).
Ninguna de las abiertas bloquea el trabajo inmediato.

---

### Q-004 · ¿Cuántas bases y de qué tamaño?
- **Datos útiles:** cantidad de conexiones, tablas por base y filas de la tabla más grande.
- **Qué define:** cuándo hace falta la Fase 4 (P-050 a P-053) y si P-052 se adelanta.

### Q-005 · ¿Habrá lógica de negocio propia al guardar?
- **Ejemplos:** recalcular totales, enviar un correo o validar contra otro sistema.
- **Qué define:** si los hooks (P-060) se adelantan a la Fase 2 o 3.

### Q-006 · ¿Quién puede configurar los mantenedores?
- **Opciones:** solo los roles listados en `Auth:AdminRoles` · cualquier usuario con permiso de "configurar" en esa conexión.
- **Qué define:** el editor de overrides (P-012) antes de que exista RBAC (P-042).
- **Sugerencia:** en la Fase 2, solo `Auth:AdminRoles` (en modo `Local`, el usuario marcado como admin); en la Fase 3, un permiso específico por conexión.
- **Nota (2026-10-27):** se aplicó la sugerencia de forma provisional (D-050): solo `Auth:AdminRoles` cambia ajustes. Falta confirmar si alcanza o si se quiere un permiso por conexión.

### Q-007 · ¿Cuánto tiempo se guarda la auditoría?
- **Opciones:** 90 días · 1 año · indefinido · según la tabla.
- **Qué define:** el tamaño del metadata store y si se necesita un archivado (P-044).
