using System.Security.Claims;
using System.Text.Json;

namespace Mantenedores.Api.Auth;

/// <summary>
/// Saca nombre y roles de los claims de cualquier emisor según la configuración (D-024):
/// claims simples, repetidos, arreglos JSON y objetos anidados ("realm_access.roles").
/// </summary>
public static class ClaimsMapper
{
    private static readonly string[] DefaultNameClaims = ["preferred_username", "name", "email", "sub"];
    private static readonly string[] DefaultRoleClaims = ["role", "roles", "groups"];

    public static string? Name(ClaimsPrincipal principal, string configured)
    {
        var paths = Paths(configured, DefaultNameClaims);
        return paths.SelectMany(p => Values(principal, p)).FirstOrDefault(v => v.Length > 0);
    }

    public static IReadOnlyList<string> Roles(ClaimsPrincipal principal, string configured) =>
        Paths(configured, DefaultRoleClaims)
            .SelectMany(p => Values(principal, p))
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsAdmin(IEnumerable<string> roles, IEnumerable<string> adminRoles) =>
        roles.Intersect(adminRoles, StringComparer.OrdinalIgnoreCase).Any();

    private static string[] Paths(string configured, string[] defaults) =>
        string.IsNullOrWhiteSpace(configured)
            ? defaults
            : configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Valores de una ruta "claim" o "claim.campo.subcampo".</summary>
    public static IEnumerable<string> Values(ClaimsPrincipal principal, string path)
    {
        var parts = path.Split('.');
        foreach (var claim in principal.FindAll(parts[0]))
        {
            if (parts.Length == 1 && !LooksLikeJson(claim.Value))
            {
                yield return claim.Value;
                continue;
            }

            var element = TryParse(claim.Value);
            if (element is null)
            {
                // No era JSON: es un valor simple que empieza con [ o {.
                if (parts.Length == 1) yield return claim.Value;
                continue;
            }

            foreach (var v in Navigate(element.Value, parts[1..]))
                yield return v;
        }
    }

    private static IEnumerable<string> Navigate(JsonElement element, string[] rest)
    {
        if (rest.Length > 0)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(rest[0], out var child))
                foreach (var v in Navigate(child, rest[1..])) yield return v;
            yield break;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String) yield return item.GetString()!;
                break;
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
        }
    }

    private static JsonElement? TryParse(string value)
    {
        try { return JsonDocument.Parse(value).RootElement; }
        catch (JsonException) { return null; }
    }

    private static bool LooksLikeJson(string value) => value.StartsWith('[') || value.StartsWith('{');
}
