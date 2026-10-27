using System.Text.Json;
using System.Text.Json.Serialization;
using Mantenedores.Core.Overrides;

namespace Mantenedores.Metadata;

/// <summary>Formato del documento de ajustes: el mismo JSON de la API (camelCase, sin nulos).</summary>
internal static class OverrideJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(TableOverride value) => JsonSerializer.Serialize(value, Options);

    public static TableOverride Deserialize(string json) =>
        JsonSerializer.Deserialize<TableOverride>(json, Options) ?? new TableOverride();
}
