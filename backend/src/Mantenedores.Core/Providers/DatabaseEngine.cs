using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mantenedores.Core.Providers;

/// <summary>
/// Nombre del motor de una conexión ("SqlServer", "Postgres", "MySql"…). Es un nombre abierto y no una lista fija para
/// que un proveedor cargado como plugin (P-064, D-092) pueda traer el suyo. Se compara sin distinguir mayúsculas y se
/// guarda y se envía como texto, igual que cuando era un enum.
/// </summary>
[JsonConverter(typeof(DatabaseEngineJsonConverter))]
public readonly partial struct DatabaseEngine : IEquatable<DatabaseEngine>
{
    public static readonly DatabaseEngine SqlServer = new("SqlServer");
    public static readonly DatabaseEngine Postgres = new("Postgres");

    /// <summary>MySQL 8.0+ y MariaDB 10.6+ (un solo proveedor, D-034).</summary>
    public static readonly DatabaseEngine MySql = new("MySql");

    private readonly string? _name;

    /// <summary>Letras, números y guion bajo, empezando por letra: va en rutas, en la metadata y en la interfaz.</summary>
    public DatabaseEngine(string name)
    {
        if (!IsValidName(name)) throw new ArgumentException($"Nombre de motor inválido: '{name}'.", nameof(name));
        _name = name;
    }

    public string Name => _name ?? "";

    public static bool IsValidName([NotNullWhen(true)] string? name) => name is not null && ValidName().IsMatch(name);

    public static DatabaseEngine Parse(string name) => new(name);

    public bool Equals(DatabaseEngine other) => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is DatabaseEngine other && Equals(other);
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
    public override string ToString() => Name;

    public static bool operator ==(DatabaseEngine a, DatabaseEngine b) => a.Equals(b);
    public static bool operator !=(DatabaseEngine a, DatabaseEngine b) => !a.Equals(b);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,39}$")]
    private static partial Regex ValidName();
}

public sealed class DatabaseEngineJsonConverter : JsonConverter<DatabaseEngine>
{
    /// <summary>Orden del enum anterior: los archivos guardados antes del cambio tienen el número (0, 1, 2).</summary>
    private static readonly DatabaseEngine[] Legacy = [DatabaseEngine.SqlServer, DatabaseEngine.Postgres, DatabaseEngine.MySql];

    public override DatabaseEngine Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n) && n >= 0 && n < Legacy.Length)
            return Legacy[n];
        var name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return DatabaseEngine.IsValidName(name) ? new DatabaseEngine(name) : throw new JsonException($"Motor inválido: '{name}'.");
    }

    public override void Write(Utf8JsonWriter writer, DatabaseEngine value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Name);
}
