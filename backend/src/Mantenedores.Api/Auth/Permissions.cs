using System.Security.Claims;

namespace Mantenedores.Api.Auth;

/// <summary>
/// Permisos de la app (D-053). Cada uno se otorga a uno o más roles por configuración (<c>Auth:Permissions</c>);
/// los roles de <c>Auth:AdminRoles</c> tienen todos. Cuando exista el apartado de seguridad (perfiles y usuarios,
/// P-040/P-042), estos mismos permisos se asignarán a perfiles desde ahí.
/// </summary>
public static class Permissions
{
    /// <summary>Configurar mantenedores: ajustes por tabla y columna.</summary>
    public const string ConfigureTables = "ConfigureTables";

    /// <summary>Crear, editar, probar y borrar conexiones.</summary>
    public const string ManageConnections = "ManageConnections";

    public static readonly IReadOnlyList<string> All = [ConfigureTables, ManageConnections];

    /// <summary>Rol que recibe cada permiso si no se configura otro.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultRoles = new Dictionary<string, string>
    {
        [ConfigureTables] = "mnt-configurador",
        [ManageConnections] = "mnt-conexiones"
    };

    /// <summary>Nombre de la política de autorización de un permiso.</summary>
    public static string Policy(string permission) => "perm:" + permission;

    /// <summary>Roles que dan el permiso: los configurados (o el de por defecto) más los de administrador.</summary>
    public static IReadOnlyList<string> RolesFor(AuthOptions auth, string permission)
    {
        var configured = auth.Permissions.TryGetValue(permission, out var roles) && roles.Count > 0
            ? roles
            : [DefaultRoles[permission]];
        return configured.Concat(auth.AdminRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Permisos que tiene un conjunto de roles.</summary>
    public static IReadOnlyList<string> Granted(AuthOptions auth, IEnumerable<string> roles)
    {
        var set = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return All.Where(p => RolesFor(auth, p).Any(set.Contains)).ToList();
    }

    public static bool Has(AuthOptions auth, ClaimsPrincipal user, string permission) =>
        Granted(auth, user.FindAll(ClaimTypes.Role).Select(c => c.Value)).Contains(permission);
}
