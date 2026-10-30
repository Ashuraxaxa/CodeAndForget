using System.Text.Json;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Endpoints;

/// <summary>
/// Texto a mostrar de los valores de FK y de vínculos N:M en la auditoría (P-094): "categoria_id: 2" pasa a mostrarse
/// como "Bebidas (2)". Se consulta una vez por tabla referenciada y página, con los permisos del usuario. Si la base no
/// responde o la tabla ya no existe, la auditoría se muestra igual, solo con los ids.
/// </summary>
public static class AuditLabels
{
    public static async Task<Dictionary<long, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> ForAsync(
        IReadOnlyList<AuditEntry> entries, CrudService crud, SchemaService schemas, CancellationToken ct)
    {
        // Tabla referenciada → valores a buscar, y quién los usa (entrada y campo).
        var wanted = new Dictionary<Target, Dictionary<string, JsonElement>>();
        var uses = new List<(long Entry, string Field, Target Target, string Value)>();

        foreach (var group in entries.GroupBy(e => (e.Connection, e.Schema, e.Table)))
        {
            Core.Schema.TableInfo table;
            try { table = (await schemas.GetTableAsync(group.Key.Connection, group.Key.Schema, group.Key.Table, ct)).Table; }
            catch (Exception ex) when (ex is not OperationCanceledException) { continue; }

            var targets = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in table.ForeignKeys.Where(f => f.IsSingleColumn))
                targets.TryAdd(fk.Columns[0], new Target(group.Key.Connection, fk.RefSchema, fk.RefTable, fk.RefColumns[0]));
            foreach (var link in table.ManyToMany)
                targets.TryAdd(link.Name, new Target(group.Key.Connection, link.TargetSchema, link.TargetTable, link.TargetColumn));
            if (targets.Count == 0) continue;

            foreach (var e in group)
                foreach (var json in new[] { e.OldValues, e.NewValues })
                {
                    if (json is null) continue;
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (!targets.TryGetValue(prop.Name, out var target)) continue;
                        var values = prop.Value.ValueKind == JsonValueKind.Array ? prop.Value.EnumerateArray().ToList() : [prop.Value];
                        foreach (var v in values.Where(v => v.ValueKind is JsonValueKind.Number or JsonValueKind.String))
                        {
                            var text = Text(v);
                            if (!wanted.TryGetValue(target, out var set)) wanted[target] = set = [];
                            set.TryAdd(text, v.Clone());
                            uses.Add((e.Id, prop.Name, target, text));
                        }
                    }
                }
        }

        var found = new Dictionary<Target, IReadOnlyDictionary<string, string>>();
        foreach (var (target, values) in wanted)
        {
            try { found[target] = await crud.LabelsAsync(target.Connection, target.Schema, target.Table, target.Column, values.Values, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* sin acceso o base caída: solo ids */ }
        }

        var result = new Dictionary<long, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>();
        foreach (var byEntry in uses.GroupBy(u => u.Entry))
        {
            var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            foreach (var byField in byEntry.GroupBy(u => u.Field))
            {
                var labels = new Dictionary<string, string>();
                foreach (var u in byField)
                    if (found.TryGetValue(u.Target, out var map) && map.TryGetValue(u.Value, out var label)) labels[u.Value] = label;
                if (labels.Count > 0) fields[byField.Key] = labels;
            }
            if (fields.Count > 0) result[byEntry.Key] = fields;
        }
        return result;
    }

    /// <summary>"precio: 10; categoria_id: Bebidas (2)", para la exportación.</summary>
    public static string? ValuesText(string? json, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? labels)
    {
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;
        string One(string field, JsonElement v)
        {
            var text = v.ValueKind == JsonValueKind.Null ? "∅" : Text(v);
            return labels?.GetValueOrDefault(field)?.GetValueOrDefault(text) is { } label ? $"{label} ({text})" : text;
        }
        return string.Join("; ", doc.RootElement.EnumerateObject().Select(p => $"{p.Name}: " + (p.Value.ValueKind == JsonValueKind.Array
            ? "[" + string.Join(", ", p.Value.EnumerateArray().Select(v => One(p.Name, v))) + "]"
            : One(p.Name, p.Value))));
    }

    /// <summary>"id=5&amp;linea=2" → "id=5, linea=2".</summary>
    public static string KeyText(string rowKey) =>
        string.Join(", ", rowKey.Split('&').Select(kv => Uri.UnescapeDataString(kv.Replace('+', ' '))));

    /// <summary>Encabezados de la exportación en el idioma de la interfaz (P-101).</summary>
    public static IReadOnlyList<string> Headers(string language) => language switch
    {
        "en" => ["Date (UTC)", "User", "Operation", "Connection", "Schema", "Table", "Record", "Before", "After"],
        "ru" => ["Дата (UTC)", "Пользователь", "Операция", "Подключение", "Схема", "Таблица", "Запись", "До", "После"],
        _ => ["Fecha (UTC)", "Usuario", "Operación", "Conexión", "Esquema", "Tabla", "Registro", "Antes", "Después"]
    };

    private static string Text(JsonElement v) => v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.GetRawText();

    private sealed record Target(string Connection, string Schema, string Table, string Column);
}
