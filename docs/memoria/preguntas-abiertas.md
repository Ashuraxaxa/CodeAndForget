# Preguntas abiertas

Decisiones que necesitan respuesta del dueño del proyecto. Cuando se respondan, se registran
como `D-xxx` en [decisiones.md](decisiones.md) y se borran de aquí.

**Conviene responder Q-001 a Q-003 antes de empezar P-010 (semana 2).**

---

### Q-001 · ¿Dónde se va a desplegar?
- **Opciones:** Azure · AWS · servidor propio (on-premise) · otro.
- **Qué define:** el vault de secretos (P-045), el servicio de Postgres para la metadata (P-010), Redis (P-050) y la forma de publicar (contenedor, App Service, Kubernetes).
- **Sugerencia:** si el ecosistema es Microsoft (SQL Server, Entra ID), **Azure** simplifica Key Vault, Postgres administrado y SSO.

### Q-002 · ¿Quién lo va a usar?
- **Opciones:** solo un equipo interno · varios clientes separados (multi-tenant).
- **Qué define:** si el metadata store lleva `tenant_id` en todas las tablas, cómo se aíslan las conexiones y los usuarios, y los límites por cliente.
- **Sugerencia:** aunque hoy sea solo interno, agregar `tenant_id` desde P-010 cuesta poco y evita una migración grande después.

### Q-003 · ¿Qué proveedor de identidad (SSO)?
- **Opciones:** Microsoft Entra ID · Google Workspace · Keycloak (autoalojado) · solo login local.
- **Qué define:** P-041 y cómo se mapean los grupos a roles (P-042).

### Q-004 · ¿Cuántas bases y de qué tamaño?
- **Datos útiles:** cantidad de conexiones, tablas por base y filas de la tabla más grande.
- **Qué define:** cuándo hace falta la Fase 4 (P-050 a P-053) y si P-052 se adelanta.

### Q-005 · ¿Habrá lógica de negocio propia al guardar?
- **Ejemplos:** recalcular totales, enviar un correo o validar contra otro sistema.
- **Qué define:** si los hooks (P-060) se adelantan a la Fase 2 o 3.

### Q-006 · ¿Quién puede configurar los mantenedores?
- **Opciones:** solo administradores · cualquier usuario con permiso de "configurar" en esa conexión.
- **Qué define:** el editor de overrides (P-012) antes de que exista RBAC (P-042).
- **Sugerencia:** solo administradores en la Fase 2; permiso específico cuando llegue RBAC.

### Q-007 · ¿Cuánto tiempo se guarda la auditoría?
- **Opciones:** 90 días · 1 año · indefinido · según la tabla.
- **Qué define:** el tamaño del metadata store y si se necesita un archivado (P-044).
