using Dapper;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Query;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Security;

namespace Mantenedores.Core.Services;

public sealed partial class CrudService
{
    /// <summary>Tope de filas por página del feed: Power BI pide la siguiente con @odata.nextLink.</summary>
    public const int MaxFeedRows = 5000;

    /// <summary>
    /// Una página de la tabla para el feed OData de Power BI y Excel (P-132): con los permisos del usuario, y como
    /// sacar los datos de Celdo es exportar, exige ese permiso. Trae los textos de las FK (<c>columna__label</c>);
    /// los archivos no viajan.
    /// </summary>
    public async Task<(TableInfo Table, IReadOnlyList<IDictionary<string, object?>> Rows)> FeedAsync(
        string conn, string schema, string table, long skip, int top, IReadOnlyList<SortSpec> sort, CancellationToken ct)
    {
        var (ctx, info) = await schemas.GetTableAsync(conn, schema, table, ct);
        Require(info, DataOperation.Export, "exportar");
        var take = Math.Clamp(top, 1, MaxFeedRows);
        var request = new ListRequest { Skip = skip, PageSize = take, Sort = sort };
        var (data, _) = new QueryBuilder(ctx.Provider.Dialect).BuildList(ctx.LabelSchema, info, request, MaxFeedRows);
        await using var db = await OpenAsync(ctx, ct);
        var rows = (await Translated(ctx, info, DbOperation.Read, () => db.QueryAsync(Command(data, ct)))).Cast<object>()
            .Select(r => (IDictionary<string, object?>)Normalize(info, Rename(ToRow(r), data.Renames))).ToList();
        return (info, rows);
    }
}
