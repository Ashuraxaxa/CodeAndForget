# Licencias y planes

Celdo funciona en el plan **Gratis** sin clave. Pro y Empresa se activan con una **clave de licencia firmada** que se
valida sin internet, así sirve también en redes cerradas (P-116, D-104, D-106). Precios y comparación de planes:
[comercial/plan-comercial.md](comercial/plan-comercial.md).

## Qué permite cada plan

| | Gratis | Pro | Empresa |
|---|---|---|---|
| Usuarios | 2 | 25 (o los de la licencia) | Sin tope (o los de la licencia) |
| Conexiones | 1 | Sin tope | Sin tope |
| Tablas | 15, elegidas una vez y fijas | Todas | Todas |
| Auditoría visible | Últimos 7 días | Último año | Según `Audit:RetentionDays` |
| Perfiles y permisos (crear y editar) | — | ✓ | ✓ |
| Exportar e importar Excel, JSON y XML; importar actualizando | — (solo CSV, insertando) | ✓ | ✓ |
| Avisos por correo, botones con procedimientos, copiar ajustes | — | ✓ | ✓ |
| IA por MCP y tokens de acceso | — | ✓ | ✓ |
| Login con proveedor de identidad (`Auth:Mode=Jwt`) | — | — | ✓ |
| Metadata en PostgreSQL o SQL Server, llaves en la base, Redis, plugins, OpenTelemetry | — | — | ✓ |

**Cómo se aplica.** Siempre en el servidor:
- **Usuarios:** los que tienen lugar son los primeros N: los de la configuración (`Auth:Users`) y luego los propios
  activos, por nombre. Quien no tiene lugar no puede iniciar sesión, y sus sesiones abiertas reciben 403. No se puede
  crear ni reactivar un usuario sin lugar.
- **Conexiones:** no se crea una más allá del tope. En Gratis se usa la conexión de las tablas elegidas; si no hay
  tablas elegidas, la primera por id. Las demás conexiones quedan guardadas, pero no se usan.
- **Tablas de Gratis:** el administrador las elige en **Plan**. Solo esas se ven y se usan: listado, formularios,
  combos, exportar, la IA. Se pueden sumar más hasta llegar a 15, pero **nunca quitar ni cambiar** las elegidas.
  Conviene elegir también las tablas de los combos (Producto → Categoría).
- **Auditoría:** al bajar de plan no se borra nada. Solo se ve el período del plan, y al subir vuelve a verse todo.
- **Funciones del plan Empresa:** si están configuradas sin esa licencia, la API responde 403 con la lista. Siempre
  responden la salud, el login y la pantalla Plan, para poder activar la licencia.
- **Errores:** son 403 con `type: "mantenedores:plan"` y un mensaje que dice qué plan hace falta.

## Activar una licencia

- **En la pantalla Plan:** el administrador (permiso `ManageSecurity`) pega la clave en **Activar licencia**. Queda en
  la metadata, así la ven todas las instancias.
- **Por configuración:** `License:Key` (variable `License__Key`). Manda sobre la pegada en Plan. Es lo práctico en
  Docker y obligatorio en modo `Jwt` si nadie puede entrar todavía.

**Vencimiento:** hay 14 días de gracia con un aviso. Después, la instalación vuelve a Gratis **sin perder ajustes ni
datos**.

**Prueba Pro:** 30 días, una vez por instalación y solo sin licencia. Se empieza desde Plan.

## Emitir licencias (dueño)

La clave es `CELDO1.<datos>.<firma>`: JSON en base64url, firmado con ECDSA P-256 (SHA-256). La app trae la **llave
pública** (`TrustedLicenseKeys`); la **privada** la guarda el dueño fuera del repositorio, y quien la tenga puede
emitir licencias.

```bash
# Emitir (plan pro o business; --users y --days son opcionales: sin --days no vence)
dotnet Mantenedores.Api.dll license issue --key celdo-licencias-privada.pem \
  --org "Lima Data SAC" --plan pro --users 25 --days 365 --email compras@limadata.pe

# Leer una clave (valida la firma)
dotnet Mantenedores.Api.dll license show CELDO1.…

# Generar un par nuevo (solo si se cambia de llave)
dotnet Mantenedores.Api.dll license keygen ./llaves
```

**Cambiar de llave:**
1. Genere el par nuevo con `keygen`.
2. Agregue la pública nueva a `TrustedLicenseKeys` y publique una versión.
3. Emita las licencias nuevas con la privada nueva.
4. Quite la pública anterior cuando ya no queden licencias firmadas con ella.

## Pruebas

Las pruebas firman con una llave propia, que agregan con `TrustedLicenseKeys.AddForTests` (solo visible desde los
proyectos de pruebas). Además ponen una licencia Empresa en `License__Key`; las pruebas de planes la reemplazan.
