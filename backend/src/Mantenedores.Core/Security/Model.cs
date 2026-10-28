namespace Mantenedores.Core.Security;

/// <summary>Operaciones sobre los datos (D-063). En el JSON van como texto: "view", "create"…</summary>
[Flags]
public enum DataOperation
{
    None = 0,
    View = 1,
    Create = 2,
    Update = 4,
    Delete = 8,
    Export = 16,
    Import = 32,

    /// <summary>Ejecutar las acciones de la tabla (procedimientos almacenados, P-061).</summary>
    Execute = 64,
    All = View | Create | Update | Delete | Export | Import | Execute
}

public static class DataOperations
{
    public static readonly IReadOnlyDictionary<string, DataOperation> Names = new Dictionary<string, DataOperation>(StringComparer.OrdinalIgnoreCase)
    {
        ["view"] = DataOperation.View,
        ["create"] = DataOperation.Create,
        ["update"] = DataOperation.Update,
        ["delete"] = DataOperation.Delete,
        ["export"] = DataOperation.Export,
        ["import"] = DataOperation.Import,
        ["execute"] = DataOperation.Execute
    };

    public static DataOperation Parse(IEnumerable<string>? names) =>
        (names ?? []).Aggregate(DataOperation.None, (acc, n) => acc | (Names.TryGetValue(n.Trim(), out var op) ? op : DataOperation.None));

    public static IReadOnlyList<string> ToNames(DataOperation ops) =>
        Names.Where(kv => ops.HasFlag(kv.Value)).Select(kv => kv.Key).ToList();
}

/// <summary>
/// Perfil de seguridad (D-062): a quién se aplica (roles del proveedor de identidad y usuarios, de cualquier origen)
/// y qué puede hacer. Se guarda como un documento JSON.
/// </summary>
public sealed record SecurityProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Roles del token (modo Jwt) o de los usuarios de configuración que reciben este perfil.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Usuarios (por nombre) que reciben este perfil: locales o del proveedor de identidad.</summary>
    public IReadOnlyList<string> Users { get; init; } = [];

    /// <summary>Permisos de la app: ConfigureTables, ManageConnections, ManageSecurity (D-053, D-066).</summary>
    public IReadOnlyList<string> AppPermissions { get; init; } = [];

    /// <summary>Permisos sobre los datos, por id de conexión ("*" = todas).</summary>
    public IReadOnlyDictionary<string, ConnectionRule> Connections { get; init; } = new Dictionary<string, ConnectionRule>();
}

/// <summary>Operaciones en toda la conexión, salvo las tablas que digan otra cosa (D-063).</summary>
public sealed record ConnectionRule
{
    public IReadOnlyList<string> Operations { get; init; } = [];

    /// <summary>Por "esquema.tabla": reemplaza las operaciones de la conexión para esa tabla.</summary>
    public IReadOnlyDictionary<string, TableRule> Tables { get; init; } = new Dictionary<string, TableRule>();
}

public sealed record TableRule
{
    public IReadOnlyList<string> Operations { get; init; } = [];

    /// <summary>Por columna: "view" (se ve) y "update" (se edita). Sin regla, la columna sigue a la tabla.</summary>
    public IReadOnlyDictionary<string, ColumnRule> Columns { get; init; } = new Dictionary<string, ColumnRule>();
}

public sealed record ColumnRule
{
    public IReadOnlyList<string> Operations { get; init; } = [];
}

/// <summary>Usuario propio de la app (modo Local), guardado en la metadata con la contraseña en hash (D-062).</summary>
public sealed record LocalUser
{
    public required string Username { get; init; }
    public string? DisplayName { get; init; }
    public required string PasswordHash { get; init; }
    public bool Active { get; init; } = true;
}

public interface ISecurityStore
{
    Task<IReadOnlyList<SecurityProfile>> ListProfilesAsync(CancellationToken ct);
    Task SaveProfileAsync(SecurityProfile profile, string? user, CancellationToken ct);
    Task<bool> DeleteProfileAsync(string id, CancellationToken ct);

    Task<IReadOnlyList<LocalUser>> ListUsersAsync(CancellationToken ct);
    Task<LocalUser?> GetUserAsync(string username, CancellationToken ct);
    Task SaveUserAsync(LocalUser user, CancellationToken ct);
    Task<bool> DeleteUserAsync(string username, CancellationToken ct);
}

/// <summary>Sin almacén: no hay perfiles, así que la seguridad de datos queda abierta (como antes de la Fase 3).</summary>
public sealed class NoSecurityStore : ISecurityStore
{
    public Task<IReadOnlyList<SecurityProfile>> ListProfilesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<SecurityProfile>>([]);
    public Task SaveProfileAsync(SecurityProfile profile, string? user, CancellationToken ct) => throw new NotSupportedException("Este almacén no guarda perfiles.");
    public Task<bool> DeleteProfileAsync(string id, CancellationToken ct) => Task.FromResult(false);
    public Task<IReadOnlyList<LocalUser>> ListUsersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LocalUser>>([]);
    public Task<LocalUser?> GetUserAsync(string username, CancellationToken ct) => Task.FromResult<LocalUser?>(null);
    public Task SaveUserAsync(LocalUser user, CancellationToken ct) => throw new NotSupportedException("Este almacén no guarda usuarios.");
    public Task<bool> DeleteUserAsync(string username, CancellationToken ct) => Task.FromResult(false);
}
