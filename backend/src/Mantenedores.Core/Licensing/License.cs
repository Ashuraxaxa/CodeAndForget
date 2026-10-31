using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mantenedores.Core.Errors;

namespace Mantenedores.Core.Licensing;

/// <summary>Planes de Celdo (D-104). El orden importa: cada uno incluye lo del anterior.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Plan { Free = 0, Pro = 1, Business = 2 }

/// <summary>Lo que dice una clave de licencia. <see cref="Users"/> nulo = el tope del plan (25 en Pro, sin tope en Empresa).</summary>
public sealed record LicenseInfo
{
    public required string Id { get; init; }
    public required string Organization { get; init; }
    public required Plan Plan { get; init; }
    public int? Users { get; init; }
    public string? Email { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Clave de licencia (P-116): <c>CELDO1.&lt;datos&gt;.&lt;firma&gt;</c>, con los datos en JSON y la firma ECDSA P-256
/// (SHA-256), ambos en base64url. Se valida sin conexión a internet contra las llaves públicas que trae la app
/// (<see cref="TrustedLicenseKeys"/>), así sirve en redes cerradas.
/// </summary>
public static class LicenseKey
{
    public const string Prefix = "CELDO1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Issue(LicenseInfo info, ECDsa privateKey)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(info, Json));
        var signature = privateKey.SignData(payload, HashAlgorithmName.SHA256);
        return $"{Prefix}.{Base64Url(payload)}.{Base64Url(signature)}";
    }

    /// <summary>Valida la firma y lee los datos. No mira el vencimiento: eso lo decide quien la usa (hay días de gracia).</summary>
    public static LicenseInfo Verify(string key, IEnumerable<ECDsa> trusted)
    {
        var parts = (key ?? "").Trim().Split('.');
        if (parts.Length != 3 || parts[0] != Prefix) throw Invalid("no tiene el formato de una licencia de Celdo");
        byte[] payload, signature;
        try { (payload, signature) = (FromBase64Url(parts[1]), FromBase64Url(parts[2])); }
        catch (FormatException) { throw Invalid("no tiene el formato de una licencia de Celdo"); }
        if (!trusted.Any(k => k.VerifyData(payload, signature, HashAlgorithmName.SHA256)))
            throw Invalid("la firma no es válida");
        try
        {
            var info = JsonSerializer.Deserialize<LicenseInfo>(payload, Json) ?? throw Invalid("no tiene datos");
            return info;
        }
        catch (JsonException) { throw Invalid("no tiene datos válidos"); }
    }

    private static ValidationException Invalid(string why) => new("license", $"La licencia no es válida: {why}.");

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
