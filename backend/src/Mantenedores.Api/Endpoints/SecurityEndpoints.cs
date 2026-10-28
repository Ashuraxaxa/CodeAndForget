using System.Security.Claims;
using System.Text.RegularExpressions;
using Mantenedores.Api.Auth;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Security;
using Microsoft.Extensions.Options;

namespace Mantenedores.Api.Endpoints;

/// <summary>Perfiles y usuarios propios (D-062, D-063). Todo exige el permiso ManageSecurity (D-066).</summary>
public static partial class SecurityEndpoints
{
    /// <summary><c>Source</c>: "local" (editable aquí) o "config" (de <c>Auth:Users</c>, solo lectura).</summary>
    public sealed record UserResponse(string Username, string? DisplayName, bool Active, string Source);

    /// <summary>Al editar, lo que no se envía se conserva (la contraseña, el nombre y si está activo).</summary>
    public sealed record UserRequest(string? DisplayName, string? Password, bool? Active);

    /// <summary>Perfil a guardar; el id va en la ruta.</summary>
    public sealed record ProfileRequest(string? Name, string? Description, IReadOnlyList<string>? Roles, IReadOnlyList<string>? Users,
        IReadOnlyList<string>? AppPermissions, IReadOnlyDictionary<string, ConnectionRule>? Connections);

    /// <summary>Lo que la pantalla necesita para armar un perfil.</summary>
    public sealed record SecurityInfo(AuthMode Mode, IReadOnlyList<string> AppPermissions, IReadOnlyList<string> Operations,
        IReadOnlyList<string> AdminRoles, int MinPasswordLength);

    public const int MinPasswordLength = 8;

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,49}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9._@-]{0,99}$")]
    private static partial Regex UsernamePattern();

    public static void MapSecurity(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/security")
            .RequireAuthorization(Permissions.Policy(Permissions.ManageSecurity));

        group.MapGet("/info", (IOptions<AuthOptions> auth) => new SecurityInfo(auth.Value.Mode, Permissions.All,
            DataOperations.Names.Keys.ToList(), auth.Value.AdminRoles, MinPasswordLength));

        group.MapGet("/profiles", (SecurityService security, CancellationToken ct) => security.Store.ListProfilesAsync(ct));

        group.MapPut("/profiles/{id}", async (string id, ProfileRequest body, SecurityService security, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var profile = Normalize(id, body);
            await security.Store.SaveProfileAsync(profile, user.Identity?.Name, ct);
            await security.ChangedAsync(ct);
            return Results.Ok(profile);
        });

        group.MapDelete("/profiles/{id}", async (string id, SecurityService security, CancellationToken ct) =>
        {
            var removed = await security.Store.DeleteProfileAsync(id, ct);
            await security.ChangedAsync(ct);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapGet("/users", async (ISecurityStore store, IOptions<AuthOptions> auth, CancellationToken ct) =>
            auth.Value.Users.Select(u => new UserResponse(u.Username.ToLowerInvariant(), null, true, "config"))
                .Concat((await store.ListUsersAsync(ct)).Select(u => new UserResponse(u.Username, u.DisplayName, u.Active, "local")))
                .OrderBy(u => u.Username, StringComparer.Ordinal)
                .ToList());

        group.MapPut("/users/{username}", async (string username, UserRequest body, ISecurityStore store, TokenService tokens,
            SecurityService security, IOptions<AuthOptions> auth, CancellationToken ct) =>
        {
            if (auth.Value.Mode != AuthMode.Local)
                throw new OperationNotAllowedException("Los usuarios propios solo inician sesión en modo Local; en modo Jwt asocie a los perfiles los usuarios o roles del proveedor.");
            var name = (username ?? "").Trim().ToLowerInvariant();
            if (!UsernamePattern().IsMatch(name))
                throw new ValidationException("username", "use minúsculas, números, . _ @ o - (máx. 100)");
            if (tokens.IsConfigUser(name))
                throw new ValidationException("username", "ya existe en la configuración (Auth:Users); edítelo ahí");

            var existing = await store.GetUserAsync(name, ct);
            string hash;
            if (!string.IsNullOrEmpty(body.Password))
            {
                if (body.Password.Length < MinPasswordLength)
                    throw new ValidationException("password", $"debe tener al menos {MinPasswordLength} caracteres");
                hash = PasswordHasher.Hash(body.Password);
            }
            else hash = existing?.PasswordHash ?? throw new ValidationException("password", "es obligatoria para un usuario nuevo");

            var user = new LocalUser
            {
                Username = name,
                // Sin el campo se conserva; vacío lo borra.
                DisplayName = body.DisplayName is null ? existing?.DisplayName
                    : body.DisplayName.Trim() is { Length: > 0 } d ? d : null,
                PasswordHash = hash,
                Active = body.Active ?? existing?.Active ?? true
            };
            await store.SaveUserAsync(user, ct);
            tokens.InvalidateUsers();
            await security.ChangedAsync(ct);
            return Results.Ok(new UserResponse(user.Username, user.DisplayName, user.Active, "local"));
        });

        group.MapDelete("/users/{username}", async (string username, ISecurityStore store, TokenService tokens,
            SecurityService security, CancellationToken ct) =>
        {
            var removed = await store.DeleteUserAsync(username, ct);
            tokens.InvalidateUsers();
            await security.ChangedAsync(ct);
            return removed ? Results.NoContent() : Results.NotFound();
        });
    }

    /// <summary>Valida y limpia un perfil: id, nombres de permisos y operaciones, y claves de conexión y tabla.</summary>
    public static SecurityProfile Normalize(string id, ProfileRequest body)
    {
        id = (id ?? "").Trim().ToLowerInvariant();
        if (!IdPattern().IsMatch(id))
            throw new ValidationException("id", "use solo minúsculas, números, - o _ (máx. 50)");
        if (string.IsNullOrWhiteSpace(body.Name))
            throw new ValidationException("name", "es obligatorio");

        var appPermissions = new List<string>();
        foreach (var p in body.AppPermissions ?? [])
            appPermissions.Add(Permissions.Canonical(p) ?? throw new ValidationException("appPermissions",
                $"'{p}' no existe. Válidos: {string.Join(", ", Permissions.All)}"));

        var connections = new Dictionary<string, ConnectionRule>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, rule) in body.Connections ?? new Dictionary<string, ConnectionRule>())
        {
            var conn = key.Trim().ToLowerInvariant();
            if (conn != "*" && !IdPattern().IsMatch(conn))
                throw new ValidationException("connections", $"'{key}' no es un id de conexión válido (o \"*\" para todas)");
            var tables = new Dictionary<string, TableRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var (tkey, table) in rule.Tables ?? new Dictionary<string, TableRule>())
            {
                var t = tkey.Trim();
                if (t.IndexOf('.') is <= 0 || t.EndsWith('.'))
                    throw new ValidationException("connections", $"la tabla '{tkey}' debe escribirse como esquema.tabla");
                var columns = (table.Columns ?? new Dictionary<string, ColumnRule>())
                    .Where(kv => kv.Key.Trim().Length > 0)
                    .ToDictionary(kv => kv.Key.Trim(), kv => new ColumnRule
                    {
                        Operations = Ops(kv.Value.Operations, $"{conn} {t}.{kv.Key}", DataOperation.View | DataOperation.Update)
                    }, StringComparer.OrdinalIgnoreCase);
                tables[t] = new TableRule { Operations = Ops(table.Operations, $"{conn} {t}", DataOperation.All), Columns = columns };
            }
            connections[conn] = new ConnectionRule { Operations = Ops(rule.Operations, conn, DataOperation.All), Tables = tables };
        }

        return new SecurityProfile
        {
            Id = id,
            Name = body.Name!.Trim(),
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            Roles = Clean(body.Roles),
            Users = Clean(body.Users).Select(u => u.ToLowerInvariant()).Distinct().ToList(),
            AppPermissions = appPermissions.Distinct().ToList(),
            Connections = connections
        };
    }

    private static IReadOnlyList<string> Ops(IReadOnlyList<string>? names, string where, DataOperation allowed)
    {
        var result = new List<string>();
        foreach (var n in names ?? [])
        {
            if (!DataOperations.Names.TryGetValue(n.Trim(), out var op) || !allowed.HasFlag(op))
                throw new ValidationException("connections",
                    $"operación '{n}' no válida en {where}. Válidas: {string.Join(", ", DataOperations.ToNames(allowed))}");
            result.Add(DataOperations.ToNames(op)[0]);
        }
        return result.Distinct().ToList();
    }

    private static IReadOnlyList<string> Clean(IReadOnlyList<string>? values) =>
        (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
