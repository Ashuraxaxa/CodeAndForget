using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Mantenedores.Api.Licensing;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Licensing;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Services;

namespace Mantenedores.Api.Endpoints;

/// <summary>
/// Feed OData v4 de solo lectura para Power BI y Excel (P-132, D-112): <c>/odata/{conexión}</c>.
/// <list type="bullet">
/// <item>Cada tabla que el usuario puede exportar es un conjunto de entidades, con sus columnas visibles y los textos de
/// sus FK (<c>columna__label</c>). Los permisos son los del usuario del token, en cada solicitud.</item>
/// <item>Power BI se conecta con "Fuente OData", autenticación Básica y el token de acceso (<c>mnt_…</c>) como
/// contraseña; también acepta <c>Bearer</c>.</item>
/// <item>Páginas de hasta <see cref="CrudService.MaxFeedRows"/> filas con <c>@odata.nextLink</c>. Opciones:
/// <c>$top</c>, <c>$skip</c>, <c>$select</c> y <c>$orderby</c>. <c>$filter</c> y las demás responden 501: así Power
/// BI filtra en su lado en vez de recibir datos sin filtrar.</item>
/// </list>
/// </summary>
public static partial class ODataEndpoints
{
    private const string Namespace = "Celdo";
    private static readonly string[] Unsupported = ["$filter", "$expand", "$apply", "$search", "$compute", "$count", "$levels"];

    public static void MapOData(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/odata/{conn}").RequireAuthorization();
        group.MapGet("/", ServiceAsync);
        group.MapGet("/$metadata", MetadataAsync);
        group.MapGet("/{set}", FeedAsync);
    }

    /// <summary>Una tabla publicada: su nombre en el feed y el de cada propiedad, que debe ser un identificador OData.</summary>
    private sealed record EntitySet(string Name, TableInfo Table, IReadOnlyList<Prop> Props);

    /// <summary>Propiedad del feed: una columna o el texto de una FK.</summary>
    private sealed record Prop(string Name, string Source, string EdmType, bool Nullable, ColumnInfo? Column);

    private static async Task<IReadOnlyList<EntitySet>> SetsAsync(string conn, SchemaService schemas, LicenseService license, CancellationToken ct)
    {
        await license.RequireAsync(Feature.AllFormats, ct);
        var ctx = await schemas.GetContextAsync(conn, ct);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sets = new List<EntitySet>();
        foreach (var t in ctx.Schema.Tables.Where(t => t.CanExport).OrderBy(t => t.Schema).ThenBy(t => t.Name))
        {
            var props = new List<Prop>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in t.Columns.Where(c => c.IsSelectable && !c.IsFile))
                props.Add(new Prop(Unique(Identifier(c.Name), names), c.Name, EdmType(c), c.IsNullable && !c.IsPrimaryKey, c));
            foreach (var (fk, _, _) in QueryBuilder.LabeledForeignKeys(ctx.LabelSchema, t))
            {
                var source = fk.LabelColumn + QueryBuilder.LabelSuffix;
                props.Add(new Prop(Unique(Identifier(source), names), source, "Edm.String", true, null));
            }
            if (props.Count == 0) continue;
            sets.Add(new EntitySet(Unique(Identifier($"{t.Schema}_{t.Name}"), used), t, props));
        }
        return sets;
    }

    private static async Task<IResult> ServiceAsync(string conn, HttpRequest req, SchemaService schemas, LicenseService license, CancellationToken ct)
    {
        var sets = await SetsAsync(conn, schemas, license, ct);
        var body = new Dictionary<string, object?>
        {
            ["@odata.context"] = $"{Base(req, conn)}/$metadata",
            ["value"] = sets.Select(s => new Dictionary<string, object?> { ["name"] = s.Name, ["kind"] = "EntitySet", ["url"] = s.Name }).ToList()
        };
        return Json(body);
    }

    private static async Task<IResult> MetadataAsync(string conn, SchemaService schemas, LicenseService license, CancellationToken ct)
    {
        var sets = await SetsAsync(conn, schemas, license, ct);
        var sb = new StringBuilder();
        using (var x = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false, Encoding = Encoding.UTF8 }))
        {
            const string edmx = "http://docs.oasis-open.org/odata/ns/edmx", edm = "http://docs.oasis-open.org/odata/ns/edm";
            x.WriteStartElement("edmx", "Edmx", edmx);
            x.WriteAttributeString("Version", "4.0");
            x.WriteStartElement("edmx", "DataServices", edmx);
            x.WriteStartElement("Schema", edm);
            x.WriteAttributeString("Namespace", Namespace);
            foreach (var s in sets)
            {
                x.WriteStartElement("EntityType", edm);
                x.WriteAttributeString("Name", s.Name);
                // La clave es la PK; una vista o una tabla sin PK usa su primera columna (Power BI no la exige única).
                var keys = s.Props.Where(p => p.Column is { IsPrimaryKey: true }).ToList();
                if (keys.Count == 0) keys = [s.Props[0]];
                x.WriteStartElement("Key", edm);
                foreach (var k in keys) { x.WriteStartElement("PropertyRef", edm); x.WriteAttributeString("Name", k.Name); x.WriteEndElement(); }
                x.WriteEndElement();
                foreach (var p in s.Props)
                {
                    x.WriteStartElement("Property", edm);
                    x.WriteAttributeString("Name", p.Name);
                    x.WriteAttributeString("Type", p.EdmType);
                    if (!p.Nullable || keys.Contains(p)) x.WriteAttributeString("Nullable", "false");
                    if (p.EdmType == "Edm.Decimal") x.WriteAttributeString("Scale", "variable");
                    x.WriteEndElement();
                }
                x.WriteEndElement();
            }
            x.WriteStartElement("EntityContainer", edm);
            x.WriteAttributeString("Name", "Container");
            foreach (var s in sets)
            {
                x.WriteStartElement("EntitySet", edm);
                x.WriteAttributeString("Name", s.Name);
                x.WriteAttributeString("EntityType", $"{Namespace}.{s.Name}");
                x.WriteEndElement();
            }
            x.WriteEndElement();
            x.WriteEndElement(); x.WriteEndElement(); x.WriteEndElement();
        }
        // XmlWriter sobre StringBuilder declara utf-16: se corrige para lo que de verdad se envía.
        var xml = sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
        return Results.Text(xml, "application/xml", Encoding.UTF8, 200);
    }

    private static async Task<IResult> FeedAsync(string conn, string set, HttpRequest req, SchemaService schemas, CrudService crud,
        LicenseService license, CancellationToken ct)
    {
        foreach (var o in Unsupported)
            if (req.Query.ContainsKey(o))
                return Error(501, "NotImplemented", $"{o} no está disponible: filtre y cuente en Power BI.");
        var es = (await SetsAsync(conn, schemas, license, ct)).FirstOrDefault(s => string.Equals(s.Name, set, StringComparison.OrdinalIgnoreCase))
                 ?? throw new NotFoundException($"La tabla '{set}' no existe en el feed de '{conn}'.");

        var skip = Long(req, "$skiptoken") ?? Long(req, "$skip") ?? 0;
        var top = Long(req, "$top");
        var take = (int)Math.Min(top ?? CrudService.MaxFeedRows, CrudService.MaxFeedRows);
        var props = Select(es, req.Query["$select"].ToString());
        var sort = OrderBy(es, req.Query["$orderby"].ToString());

        var (_, rows) = await crud.FeedAsync(conn, es.Table.Schema, es.Table.Name, skip, Math.Max(take, 1), sort, ct);
        var body = new Dictionary<string, object?>
        {
            ["@odata.context"] = $"{Base(req, conn)}/$metadata#{es.Name}" + (props.Count < es.Props.Count ? $"({string.Join(",", props.Select(p => p.Name))})" : ""),
            ["value"] = top == 0 ? [] : rows.Select(r => props.ToDictionary(p => p.Name, p => Value(p, r.TryGetValue(p.Source, out var v) ? v : null))).ToList()
        };
        // Página llena: hay (o puede haber) más. Sin $top, todo lo que quede; con $top, lo que falte para llegar a él.
        var remaining = top is { } t ? t - rows.Count : long.MaxValue;
        if (rows.Count == take && take > 0 && remaining > 0)
        {
            var q = new List<string>();
            foreach (var kv in req.Query.Where(kv => kv.Key is not ("$skip" or "$skiptoken" or "$top")))
                q.Add($"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value.ToString())}");
            q.Add($"$skiptoken={skip + rows.Count}");
            if (top is not null) q.Add($"$top={remaining}");
            body["@odata.nextLink"] = $"{Base(req, conn)}/{es.Name}?{string.Join("&", q)}";
        }
        return Json(body);
    }

    private static List<Prop> Select(EntitySet es, string select)
    {
        if (string.IsNullOrWhiteSpace(select) || select.Trim() == "*") return [.. es.Props];
        var result = new List<Prop>();
        foreach (var name in select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            result.Add(es.Props.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                       ?? throw new ValidationException("$select", $"'{name}' no es una propiedad de {es.Name}"));
        return result;
    }

    private static List<SortSpec> OrderBy(EntitySet es, string orderBy)
    {
        var sort = new List<SortSpec>();
        foreach (var part in orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bits = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var prop = es.Props.FirstOrDefault(p => string.Equals(p.Name, bits[0], StringComparison.OrdinalIgnoreCase));
            if (prop?.Column is null)
                throw new ValidationException("$orderby", $"no se puede ordenar por '{bits[0]}'");
            sort.Add(new SortSpec(prop.Column.Name, bits.Length > 1 && bits[1].Equals("desc", StringComparison.OrdinalIgnoreCase)));
        }
        return sort;
    }

    /// <summary>Tipo OData de cada columna. Los números más grandes que decimal y lo que no tiene tipo propio van como texto.</summary>
    private static string EdmType(ColumnInfo c) => c.Type switch
    {
        LogicalType.Integer => c.IsLargeNumber ? "Edm.String" : "Edm.Int64",
        LogicalType.Decimal => c.IsLargeNumber ? "Edm.String" : "Edm.Decimal",
        LogicalType.Boolean => "Edm.Boolean",
        LogicalType.Date => "Edm.Date",
        LogicalType.DateTime => "Edm.DateTimeOffset",
        LogicalType.Time => "Edm.TimeOfDay",
        LogicalType.Uuid => "Edm.Guid",
        _ => "Edm.String"
    };

    /// <summary>El valor como lo espera su tipo OData: una fecha y hora sin zona se publica como UTC.</summary>
    private static object? Value(Prop p, object? v)
    {
        if (v is null or DBNull) return null;
        return p.EdmType switch
        {
            "Edm.DateTimeOffset" => v switch
            {
                DateTimeOffset o => o,
                DateTime d => new DateTimeOffset(DateTime.SpecifyKind(d, d.Kind == DateTimeKind.Local ? DateTimeKind.Local : DateTimeKind.Utc)),
                _ => v.ToString()
            },
            "Edm.String" => v switch
            {
                string s => s,
                JsonElement e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText(),
                System.Collections.IEnumerable list => JsonSerializer.Serialize(list),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => v.ToString()
            },
            _ => v
        };
    }

    private static string Base(HttpRequest r, string conn) => $"{r.Scheme}://{r.Host}{r.PathBase}/odata/{Uri.EscapeDataString(conn)}";

    private static long? Long(HttpRequest r, string name) =>
        r.Query.TryGetValue(name, out var v) && long.TryParse(v.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static IResult Json(object body) =>
        Results.Text(JsonSerializer.Serialize(body), "application/json;odata.metadata=minimal;charset=utf-8", Encoding.UTF8, 200);

    private static IResult Error(int status, string code, string message) =>
        Results.Text(JsonSerializer.Serialize(new { error = new { code, message } }), "application/json;charset=utf-8", Encoding.UTF8, status);

    /// <summary>Identificador OData: letras, dígitos y guion bajo, sin empezar con dígito.</summary>
    private static string Identifier(string s)
    {
        var id = NotIdentifier().Replace(s.Normalize(NormalizationForm.FormD), "_").Trim('_');
        id = string.Concat(id.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark));
        if (id.Length == 0 || char.IsDigit(id[0])) id = "_" + id;
        return id.Length > 128 ? id[..128] : id;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var n = name;
        for (var i = 2; !used.Add(n); i++) n = $"{name}_{i}";
        return n;
    }

    [GeneratedRegex(@"[^\p{L}\p{Mn}0-9_]+")]
    private static partial Regex NotIdentifier();
}
