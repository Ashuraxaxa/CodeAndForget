using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Services;

/// <summary>
/// Caché compartida del esquema leído del catálogo (P-050, D-091): con varias instancias, la que lo lee lo deja aquí y
/// las demás lo toman sin volver a la base. Por defecto no hay ninguna (<see cref="NoSchemaCache"/>); con
/// <c>Cache:Provider = Redis</c> se usa Redis. Una falla de la caché nunca corta la solicitud: se lee el catálogo.
/// </summary>
public interface ISchemaCache
{
    Task<DatabaseSchema?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, DatabaseSchema schema, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}

public sealed class NoSchemaCache : ISchemaCache
{
    public Task<DatabaseSchema?> GetAsync(string key, CancellationToken ct) => Task.FromResult<DatabaseSchema?>(null);
    public Task SetAsync(string key, DatabaseSchema schema, CancellationToken ct) => Task.CompletedTask;
    public Task RemoveAsync(string key, CancellationToken ct) => Task.CompletedTask;
}

public static class SchemaCacheKeys
{
    /// <summary>Sube si cambia la forma de <see cref="DatabaseSchema"/>: así una versión nueva no lee lo que dejó otra.</summary>
    public const int Format = 1;

    /// <summary>
    /// "schema:v1:ventas:3f2a…": la conexión y una huella de lo que define qué se lee (motor, esquemas y cadena tal como
    /// está guardada, sin la contraseña). Editar el servidor, la base o los esquemas cambia la clave, así nunca se toma
    /// un esquema de otra base. Ni la contraseña ni su hash quedan en la caché.
    /// </summary>
    public static string For(ConnectionDefinition def)
    {
        var what = $"{def.Engine}|{string.Join(",", def.Schemas ?? [])}|{WithoutSecrets(def.ConnectionString)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(what)))[..16].ToLowerInvariant();
        return $"schema:v{Format}:{def.Id.ToLowerInvariant()}:{hash}";
    }

    /// <summary>La cadena sin las claves que llevan contraseña. Si no se puede leer, solo cuentan motor y esquemas.</summary>
    public static string WithoutSecrets(string connectionString)
    {
        try
        {
            var b = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
            foreach (var key in b.Keys.Cast<string>().ToList())
                if (key.Contains("password", StringComparison.OrdinalIgnoreCase) || key.Equals("pwd", StringComparison.OrdinalIgnoreCase))
                    b.Remove(key);
            return b.ConnectionString;
        }
        catch (ArgumentException) { return ""; }
    }
}

/// <summary>Formato del esquema en la caché compartida.</summary>
public static class SchemaJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] Serialize(DatabaseSchema schema) => JsonSerializer.SerializeToUtf8Bytes(schema, Options);

    public static DatabaseSchema? Deserialize(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<DatabaseSchema>(json, Options);
}
