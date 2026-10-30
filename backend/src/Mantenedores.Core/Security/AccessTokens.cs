using System.Security.Cryptography;
using System.Text;

namespace Mantenedores.Core.Security;

/// <summary>
/// Token de acceso para clientes que no inician sesión (una IA por MCP, P-113, D-100). Tiene exactamente el acceso de
/// quien lo creó: en cada uso se arma la identidad de ese usuario (sus roles y perfiles actuales); si el usuario se
/// desactiva o deja de existir, el token deja de servir. Solo se guarda el hash del secreto.
/// </summary>
public sealed record AccessToken
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Usuario que lo creó: el token actúa como él.</summary>
    public required string Owner { get; init; }
    /// <summary>
    /// Roles del dueño al crearlo. Se usan solo cuando no hay de dónde leerlos al momento (modo Jwt, donde los roles
    /// vienen del proveedor de identidad); en modo Local se toman los actuales del usuario.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
    /// <summary>Primeros caracteres del secreto, para reconocerlo en la lista.</summary>
    public required string Prefix { get; init; }
    public required string Hash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }

    public const string SecretPrefix = "mnt_";

    /// <summary>Un secreto nuevo: "mnt_" + 32 bytes aleatorios en base64url.</summary>
    public static string NewSecret() =>
        SecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 del secreto: alcanza porque el secreto es aleatorio y largo (no es una contraseña).</summary>
    public static string HashOf(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
}

public interface IAccessTokenStore
{
    Task<IReadOnlyList<AccessToken>> ListAsync(string? owner, CancellationToken ct);
    Task<AccessToken?> FindByHashAsync(string hash, CancellationToken ct);
    Task SaveAsync(AccessToken token, CancellationToken ct);
    Task<bool> DeleteAsync(string id, string? owner, CancellationToken ct);
    Task TouchAsync(string id, DateTimeOffset at, CancellationToken ct);
}

/// <summary>Sin almacén (pruebas): no hay tokens.</summary>
public sealed class NoAccessTokenStore : IAccessTokenStore
{
    public Task<IReadOnlyList<AccessToken>> ListAsync(string? owner, CancellationToken ct) => Task.FromResult<IReadOnlyList<AccessToken>>([]);
    public Task<AccessToken?> FindByHashAsync(string hash, CancellationToken ct) => Task.FromResult<AccessToken?>(null);
    public Task SaveAsync(AccessToken token, CancellationToken ct) => throw new NotSupportedException("Este almacén no guarda tokens.");
    public Task<bool> DeleteAsync(string id, string? owner, CancellationToken ct) => Task.FromResult(false);
    public Task TouchAsync(string id, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;
}
