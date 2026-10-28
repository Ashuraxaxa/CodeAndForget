using System.Security.Claims;
using Mantenedores.Core.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Mantenedores.Api.Auth;

/// <summary>
/// El acceso del usuario de la solicitud actual (D-062): sus perfiles según su nombre y roles. Se calcula una vez por
/// solicitud. Fuera de una solicitud (tareas internas) no hay restricción.
/// </summary>
public sealed class HttpAccessContext(IHttpContextAccessor http, SecurityService security, IOptions<AuthOptions> auth) : IAccessContext
{
    private static readonly object Key = new();

    public async ValueTask<UserAccess> CurrentAsync(CancellationToken ct)
    {
        if (http.HttpContext is not { } ctx) return UserAccess.All;
        return await ForAsync(ctx.User, ctx, ct);
    }

    public async ValueTask<UserAccess> ForAsync(ClaimsPrincipal user, HttpContext? ctx, CancellationToken ct)
    {
        if (ctx?.Items[Key] is UserAccess cached) return cached;
        var roles = Permissions.Roles(user);
        var access = await security.AccessForAsync(user.Identity?.Name, roles,
            ClaimsMapper.IsAdmin(roles, auth.Value.AdminRoles), ct);
        if (ctx is not null) ctx.Items[Key] = access;
        return access;
    }

    /// <summary>Permisos de la app: los de sus roles (configuración) más los de sus perfiles.</summary>
    public async ValueTask<IReadOnlyList<string>> PermissionsAsync(ClaimsPrincipal user, HttpContext? ctx, CancellationToken ct)
    {
        var granted = Permissions.Granted(auth.Value, Permissions.Roles(user)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var access = await ForAsync(user, ctx, ct);
        foreach (var p in access.AppPermissions)
            if (Permissions.Canonical(p) is { } name) granted.Add(name);
        return Permissions.All.Where(granted.Contains).ToList();
    }

    public async ValueTask<bool> HasAsync(ClaimsPrincipal user, HttpContext? ctx, string permission, CancellationToken ct) =>
        (await PermissionsAsync(user, ctx, ct)).Contains(permission);
}

/// <summary>Política <c>perm:&lt;permiso&gt;</c>: el permiso viene de un rol configurado o de un perfil.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionHandler(HttpAccessContext access) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        var http = context.Resource as HttpContext;
        if (await access.HasAsync(context.User, http, requirement.Permission, http?.RequestAborted ?? default))
            context.Succeed(requirement);
    }
}
