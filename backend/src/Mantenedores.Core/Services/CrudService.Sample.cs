using Dapper;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Services;

/// <summary>Muestra de una tabla para una IA: filas y el orden que se usó para elegir "las más recientes".</summary>
public sealed record SampleResult(IReadOnlyList<IDictionary<string, object?>> Rows, IReadOnlyList<string> OrderedBy);

public sealed partial class CrudService
{
    /// <summary>Tope de filas de una muestra (P-113): las más recientes.</summary>
    public const int MaxSampleRows = 1000;

    private static readonly string[] ModifiedNames =
        ["modifieddate", "modifiedat", "updatedat", "updated", "updatedon", "modifiedon", "lastmodified", "fechamodificacion",
         "fechaactualizacion", "modificadoen", "actualizadoen"];
    private static readonly string[] CreatedNames =
        ["creationdate", "createdate", "createdat", "created", "createdon", "fechacreacion", "fechaalta", "fecharegistro", "creadoen", "fecha"];

    /// <summary>
    /// Las filas más recientes de la tabla, con los permisos del usuario (P-113, D-100): por la fecha de modificación o de
    /// creación si la tabla tiene una, y si no por la PK descendente. Solo si la conexión lo permite
    /// (<see cref="Connections.ConnectionDefinition.AllowSamples"/>); los archivos no viajan (solo su tamaño).
    /// </summary>
    public async Task<SampleResult> SampleAsync(string conn, string schema, string table, int take, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        if (!ctx.Definition.AllowSamples)
            throw new OperationNotAllowedException(
                $"La conexión '{ctx.Definition.Name}' no permite ver una muestra de datos: actívelo en la conexión (muestra para la IA).");

        var order = RecencyOrder(info);
        var request = new ListRequest { Page = 1, PageSize = Math.Clamp(take, 1, MaxSampleRows), Sort = order };
        var (data, _) = new QueryBuilder(ctx.Provider.Dialect).BuildList(ctx.Schema, info, request, MaxSampleRows);
        await using var db = await OpenAsync(ctx, ct);
        var rows = (await Translated(ctx, info, DbOperation.Read, () => db.QueryAsync(Command(data, ct)))).Cast<object>()
            .Select(r => (IDictionary<string, object?>)Normalize(info, Rename(ToRow(r), data.Renames))).ToList();
        return new SampleResult(rows, order.Select(o => o.Column).ToList());
    }

    /// <summary>Fecha de modificación, de creación o la PK, de mayor a menor; vacío si no hay ninguna ordenable.</summary>
    public static IReadOnlyList<SortSpec> RecencyOrder(TableInfo table)
    {
        static string Compact(string n) => n.Replace("_", "").ToLowerInvariant();
        ColumnInfo? Find(string[] names) => names
            .Select(n => table.Columns.FirstOrDefault(c => c.IsSortable && !c.Hidden && c.Type is LogicalType.Date or LogicalType.DateTime && Compact(c.Name) == n))
            .FirstOrDefault(c => c is not null);
        var keys = table.PrimaryKey.Select(table.FindColumn).OfType<ColumnInfo>().Where(c => c.IsSortable).ToList();
        var byKey = keys.Count == table.PrimaryKey.Count ? keys.Select(k => new SortSpec(k.Name, Descending: true)).ToList() : [];
        // Con fecha, la PK desempata las filas con la misma fecha (p. ej. cargadas juntas).
        return (Find(ModifiedNames) ?? Find(CreatedNames)) is { } date ? [new SortSpec(date.Name, Descending: true), .. byKey] : byKey;
    }
}
