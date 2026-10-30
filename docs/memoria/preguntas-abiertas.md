# Preguntas abiertas

Decisiones que necesitan respuesta del dueño del proyecto. Cuando se respondan, se registran
como `D-xxx` en [decisiones.md](decisiones.md) y se borran de aquí.

**Respondidas el 2026-10-27:** Q-001 → D-022 (cualquier entorno), Q-002 → D-027 (equipo interno), Q-003 → D-024 (cualquier emisor JWT/OIDC), Q-008 → D-035 (versiones mínimas), Q-006 → D-053 (un permiso por acción, otorgado a roles). **2026-10-28:** apartado de seguridad → D-062 a D-066; Q-007 → D-065 (auditoría solo de cambios, 1 año); Q-004 → D-072 (tablas de hasta ~1 millón de filas); Q-005 → D-073 (sin lógica propia al guardar; avisos por correo opcionales).

---

### Q-009 · ¿Código abierto o cerrado?
Hoy el repositorio no tiene licencia. Hay tres opciones:
- **Cerrado:** solo se distribuye la imagen Docker. Protege más y no da la visibilidad de un proyecto abierto.
- **Núcleo abierto** (*open core*): el plan Gratis es abierto, por ejemplo con AGPL, y Pro y Empresa son cerrados. Atrae a la comunidad de TI.
- **Código visible con licencia comercial** (*source available*, como BSL). Se puede leer el código, pero no revenderlo.

Recomendación: empezar cerrado y decidir a los 6 meses con datos de uso.

### Q-010 · ¿Qué empresa factura y desde qué país?
Define los impuestos, el contrato de licencia, la oficina de marcas y si conviene un *merchant of record* (recomendado al inicio).

### Q-011 · ¿Qué dominio?
Las opciones son `celdo.app`, `celdo.io` y `getceldo.com`. Hay que revisar si están libres y comprar el elegido junto con P-119.

