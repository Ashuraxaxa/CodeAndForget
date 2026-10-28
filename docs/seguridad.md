# Seguridad: perfiles, usuarios y permisos

Apartado propio de la app (P-040, P-042). Decisiones: D-062 a D-067.

## En corto

- Un **perfil** dice a quién se aplica (roles y usuarios) y qué puede hacer.
- Si alguien tiene varios perfiles, sus permisos **se suman**.
- **Sin ningún perfil, la seguridad de datos está abierta:** todos ven y editan todo, como antes de la Fase 3.
  Al crear el primer perfil, solo ven datos los administradores y quienes tengan un perfil.
- Los roles de `Auth:AdminRoles` siempre tienen acceso total.
- Se administra en **Seguridad** (enlace arriba a la derecha), con el permiso `ManageSecurity`.

## A quién se aplica un perfil

| Campo | Qué es |
|---|---|
| Roles | Roles del token (modo `Jwt`) o de `Auth:Users:N:Roles` (modo `Local`). |
| Usuarios | Nombres de usuario. Sirven para usuarios propios y para usuarios del proveedor de identidad (el nombre que da `Auth:Jwt:NameClaim`). |

## Permisos de la app

Un perfil puede dar también los permisos de D-053 y D-066:

| Permiso | Qué permite |
|---|---|
| `ConfigureTables` | Configurar mantenedores (⚙ Configurar). |
| `ManageConnections` | Crear, editar, probar y borrar conexiones; ver todas. |
| `ManageSecurity` | Administrar perfiles y usuarios propios. |
| `ViewAudit` | Consultar la auditoría de cambios (todas las conexiones). |

Se suman a los que dan los roles por configuración (`Auth:Permissions`).

## Permisos sobre los datos

Operaciones: **ver, crear, editar, borrar, exportar, importar**. Importar exige además crear.

Se definen en tres niveles, con herencia:

1. **Conexión** (o `*` para todas): vale para todas sus tablas.
2. **Tabla** (`esquema.tabla`): si aparece, **reemplaza** a la conexión para esa tabla.
3. **Columna**: si aparece, reemplaza a la tabla para esa columna. Solo cuentan *ver* y *editar*:
   - sin *ver*, la columna queda oculta (no se lee, no se busca ni se exporta);
   - con *ver* y sin *editar*, queda de solo lectura.

Ejemplo: ver y exportar todo `demo`, editar solo `ventas.producto` y sin tocar su precio:

```json
{
  "name": "Vendedores",
  "users": ["ana"],
  "roles": ["ventas"],
  "connections": {
    "demo": {
      "operations": ["view", "export"],
      "tables": {
        "ventas.producto": {
          "operations": ["view", "update"],
          "columns": { "precio": { "operations": ["view"] } }
        }
      }
    }
  }
}
```

Se aplica **en el servidor**. La UI solo esconde lo que no se puede hacer:

- las tablas que no ve no salen en el menú, ni en "Referenciada por", ni como pestañas o selectores N:M;
- una FK hacia una tabla que no ve muestra la clave, sin etiqueta;
- sin *editar*, el formulario se abre en modo consulta ("Ver").

Cambiar los vínculos N:M de un registro cuenta como editar ese registro (D-067).

Tampoco se revelan nombres de lo oculto: el aviso de cambios del esquema solo muestra lo que el usuario ve, y leer
los ajustes de los mantenedores exige `ConfigureTables`.

## Usuarios propios (modo `Local`)

- Se crean en **Seguridad → Usuarios**, con contraseña de al menos 8 caracteres, guardada como hash PBKDF2 en la metadata.
- Conviven con los de `Auth:Users`. Si un nombre está en los dos, manda la configuración, y no se puede crear uno propio con ese nombre.
- Un usuario propio no trae roles: recibe permisos de los perfiles que lo nombran.
- Al desactivarlo o borrarlo, su token deja de servir en la siguiente solicitud.
- En modo `Jwt` los usuarios vienen del proveedor de identidad; en los perfiles se nombran sus usuarios o roles.

## API

Todo bajo `/api/security`, con el permiso `ManageSecurity`:

| Método y ruta | Qué hace |
|---|---|
| `GET /info` | Modo, permisos y operaciones válidas, roles de administrador. |
| `GET /profiles` · `PUT /profiles/{id}` · `DELETE /profiles/{id}` | Perfiles. |
| `GET /users` · `PUT /users/{usuario}` · `DELETE /users/{usuario}` | Usuarios (`source`: `local` o `config`). Al editar, lo que no se envía se conserva. |

`GET /api/auth/me` devuelve además `securityOpen` (no hay perfiles) y `profiles` (los del usuario).

## Dónde se guarda

En la metadata del servicio (`Metadata:Provider`), tablas `mnt_profile` y `mnt_user` (migración `004_security`).
Con `Metadata:Provider=File`, en `security.json` dentro de `Storage:DataDirectory`.
Los perfiles se cachean 30 segundos: con varias instancias, un cambio llega a las demás en ese plazo.
