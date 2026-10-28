using System.Text.Json;
using System.Text.Json.Nodes;
using Mantenedores.Core.Security;

namespace Mantenedores.Core.Audit;

/// <summary>
/// La auditoría vista por un usuario con perfil (P-095): solo las tablas que puede ver y, en los valores, solo las
/// columnas que puede ver. Se decide por nombres, sin leer el esquema: sirve aunque la base de datos no responda
/// o la tabla ya no exista.
/// </summary>
public static class AuditAccess
{
    /// <summary>Restringe la consulta. Sin restricciones (administrador o seguridad abierta), la deja igual.</summary>
    public static AuditQuery Restrict(AuditQuery query, UserAccess access, IReadOnlyList<AuditTable> audited)
    {
        if (access.Unrestricted) return query;
        var connections = new List<string>();
        var tables = new List<AuditTable>();
        foreach (var group in audited.GroupBy(t => t.Connection, StringComparer.OrdinalIgnoreCase))
        {
            var visible = group.Where(t => access.Table(t.Connection, t.Schema, t.Table).HasFlag(DataOperation.View)).ToList();
            // Si ve todas las tablas auditadas de la conexión, basta filtrar por conexión (consulta más corta).
            if (visible.Count == group.Count()) connections.Add(group.Key);
            else tables.AddRange(visible);
        }
        return query with { AllowedConnections = connections, AllowedTables = tables };
    }

    /// <summary>Quita de antes y después las columnas que el usuario no puede ver.</summary>
    public static AuditEntry Mask(AuditEntry entry, UserAccess access)
    {
        if (access.Unrestricted) return entry;
        string? Filter(string? json)
        {
            if (json is null || JsonNode.Parse(json) is not JsonObject obj) return json;
            // En los vínculos N:M la clave es el nombre de la relación, no una columna.
            if (entry.Operation == AuditOperation.Links) return json;
            foreach (var name in obj.Select(kv => kv.Key).ToList())
                if (!access.Column(entry.Connection, entry.Schema, entry.Table, name).HasFlag(DataOperation.View))
                    obj.Remove(name);
            return obj.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        return entry with { OldValues = Filter(entry.OldValues), NewValues = Filter(entry.NewValues) };
    }

    /// <summary>¿La entrada cabe en lo permitido? (para almacenes que filtran en memoria).</summary>
    public static bool Allows(AuditQuery q, AuditEntry e) =>
        (q.AllowedConnections is null && q.AllowedTables is null) ||
        (q.AllowedConnections?.Contains(e.Connection, StringComparer.OrdinalIgnoreCase) ?? false) ||
        (q.AllowedTables?.Any(t => string.Equals(t.Connection, e.Connection, StringComparison.OrdinalIgnoreCase) &&
                                    t.Schema == e.Schema && t.Table == e.Table) ?? false);
}
