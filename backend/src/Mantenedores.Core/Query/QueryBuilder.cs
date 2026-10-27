using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Query;

/// <summary>
/// Arma el SQL del CRUD genérico. Reglas de seguridad:
/// 1) tablas y columnas solo se aceptan si existen en la metadata leída del catálogo;
/// 2) todo identificador pasa por <see cref="ISqlDialect.Quote"/>;
/// 3) todo valor va como parámetro.
/// </summary>
public sealed class QueryBuilder(ISqlDialect dialect)
{
    public const string LabelSuffix = "__label";
    private const string Base = "t0";

    public (SqlQuery Data, SqlQuery Count) BuildList(DatabaseSchema schema, TableInfo table, ListRequest request)
    {
        var p = new ParamBag();
        var where = BuildWhere(table, request, p);
        var from = dialect.Table(table.Schema, table.Name) + " " + Base;

        var count = new SqlQuery($"SELECT COUNT(*) FROM {from}{where}", p.Snapshot());

        var (select, joins) = BuildSelectWithLabels(schema, table);
        var orderBy = BuildOrderBy(table, request.Sort);

        var pageSize = Math.Clamp(request.PageSize, 1, ListRequest.MaxPageSize);
        var page = Math.Max(request.Page, 1);
        var offset = p.Add((long)(page - 1) * pageSize);
        var limit = p.Add((long)pageSize);

        var sql = $"SELECT {select} FROM {from}{joins}{where} ORDER BY {orderBy} {dialect.Paging(offset, limit)}";
        return (new SqlQuery(sql, p.Snapshot()), count);
    }

    public SqlQuery BuildGetByKey(DatabaseSchema schema, TableInfo table, IReadOnlyDictionary<string, object?> key)
    {
        RequireKey(table);
        var p = new ParamBag();
        var (select, joins) = BuildSelectWithLabels(schema, table);
        var where = KeyPredicate(table, key, p, $"{Base}.");
        return new SqlQuery(
            $"SELECT {select} FROM {dialect.Table(table.Schema, table.Name)} {Base}{joins} WHERE {where}",
            p.Snapshot());
    }

    public SqlQuery BuildInsert(TableInfo table, IReadOnlyDictionary<string, object?> values)
    {
        RequireEditable(table);
        var p = new ParamBag();
        var cols = new List<string>();
        var exprs = new List<string>();

        foreach (var (name, value) in values)
        {
            var col = RequireColumn(table, name);
            if (col.IsReadOnly) continue;
            // Un nulo en una columna con default se omite para que la BD aplique su valor.
            if (value is null && col.HasDefault) continue;
            cols.Add(dialect.Quote(col.Name));
            exprs.Add(dialect.ParameterFor(col, p.Add(value)));
        }

        var missing = table.Columns
            .Where(c => c.IsRequiredOnInsert && !values.Keys.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(c => c.Name, _ => "es obligatorio");
        if (missing.Count > 0) throw new ValidationException(missing);

        var keyCols = table.PrimaryKey.Select(k => table.FindColumn(k)!).ToList();
        var sql = dialect.InsertReturningKeys(dialect.Table(table.Schema, table.Name), cols, exprs, keyCols);
        return new SqlQuery(sql, p.Snapshot());
    }

    public SqlQuery BuildUpdate(TableInfo table, IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values)
    {
        RequireEditable(table);
        var p = new ParamBag();
        var sets = new List<string>();

        foreach (var (name, value) in values)
        {
            var col = RequireColumn(table, name);
            // En Fase 1 la PK no se modifica: identifica la fila.
            if (col.IsReadOnly || col.IsPrimaryKey) continue;
            sets.Add($"{dialect.Quote(col.Name)} = {dialect.ParameterFor(col, p.Add(value))}");
        }

        if (sets.Count == 0)
            throw new ValidationException("_", "No hay columnas editables para actualizar.");

        var where = KeyPredicate(table, key, p, "");
        return new SqlQuery(
            $"UPDATE {dialect.Table(table.Schema, table.Name)} SET {string.Join(", ", sets)} WHERE {where}",
            p.Snapshot());
    }

    public SqlQuery BuildDelete(TableInfo table, IReadOnlyDictionary<string, object?> key)
    {
        RequireEditable(table);
        var p = new ParamBag();
        var where = KeyPredicate(table, key, p, "");
        return new SqlQuery($"DELETE FROM {dialect.Table(table.Schema, table.Name)} WHERE {where}", p.Snapshot());
    }

    /// <summary>Opciones para un combo de FK: valor de la PK + texto de la columna a mostrar.</summary>
    public SqlQuery BuildLookup(TableInfo table, string? search, int take)
    {
        if (table.PrimaryKey.Count != 1)
            throw new OperationNotAllowedException($"{table.FullName} necesita una PK de una sola columna para usarse como lista.");

        var p = new ParamBag();
        var pk = dialect.Quote(table.PrimaryKey[0]);
        var display = dialect.Quote(table.DisplayColumn ?? table.PrimaryKey[0]);
        var where = "";
        if (!string.IsNullOrWhiteSpace(search))
            where = $" WHERE {dialect.Contains($"{Base}.{display}", p.Add(LikePattern(search)))}";

        var offset = p.Add(0L);
        var limit = p.Add((long)Math.Clamp(take, 1, 100));
        var sql = $"SELECT {Base}.{pk} AS {dialect.Quote("value")}, {Base}.{display} AS {dialect.Quote("label")} " +
                  $"FROM {dialect.Table(table.Schema, table.Name)} {Base}{where} " +
                  $"ORDER BY {Base}.{display} {dialect.Paging(offset, limit)}";
        return new SqlQuery(sql, p.Snapshot());
    }

    public static string LikePattern(string search)
    {
        var escaped = search.Trim()
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_")
            .Replace("[", "\\[");
        return $"%{escaped}%";
    }

    private (string Select, string Joins) BuildSelectWithLabels(DatabaseSchema schema, TableInfo table)
    {
        var selectable = table.Columns.Where(c => c.IsSelectable).ToList();
        if (selectable.Count == 0)
            throw new OperationNotAllowedException($"{table.FullName} no tiene columnas de tipos soportados.");

        var parts = selectable.Select(c => $"{Base}.{dialect.Quote(c.Name)}").ToList();
        var joins = new List<string>();
        var i = 0;

        foreach (var fk in table.ForeignKeys.Where(f => f.IsSingleColumn))
        {
            var target = schema.FindTable(fk.RefSchema, fk.RefTable);
            var display = target?.DisplayColumn is { } d ? target.FindColumn(d) : null;
            // Si la columna a mostrar es la misma clave, la etiqueta no aporta nada.
            if (target is null || display is null || !display.IsSelectable ||
                string.Equals(display.Name, fk.RefColumns[0], StringComparison.OrdinalIgnoreCase))
                continue;

            var alias = $"j{i++}";
            joins.Add($" LEFT JOIN {dialect.Table(target.Schema, target.Name)} {alias} " +
                      $"ON {alias}.{dialect.Quote(fk.RefColumns[0])} = {Base}.{dialect.Quote(fk.Columns[0])}");
            parts.Add($"{alias}.{dialect.Quote(display.Name)} AS {dialect.Quote(fk.Columns[0] + LabelSuffix)}");
        }

        return (string.Join(", ", parts), string.Concat(joins));
    }

    private string BuildWhere(TableInfo table, ListRequest request, ParamBag p)
    {
        var predicates = new List<string>();

        foreach (var (name, raw) in request.Filters)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            var col = RequireColumn(table, name);
            if (!col.IsSelectable) throw new ValidationException(col.Name, "no se puede filtrar por esta columna");

            var colExpr = $"{Base}.{dialect.Quote(col.Name)}";
            predicates.Add(col.Type is LogicalType.String or LogicalType.Text
                ? dialect.Contains(colExpr, p.Add(LikePattern(raw)))
                : $"{colExpr} = {dialect.ParameterFor(col, p.Add(ValueConverter.FromString(col, raw)))}");
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var textCols = table.Columns.Where(c => c.Type is LogicalType.String or LogicalType.Text).ToList();
            if (textCols.Count > 0)
            {
                var param = p.Add(LikePattern(request.Search));
                predicates.Add("(" + string.Join(" OR ",
                    textCols.Select(c => dialect.Contains($"{Base}.{dialect.Quote(c.Name)}", param))) + ")");
            }
        }

        return predicates.Count == 0 ? "" : " WHERE " + string.Join(" AND ", predicates);
    }

    private string BuildOrderBy(TableInfo table, IReadOnlyList<SortSpec> sort)
    {
        var parts = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in sort)
        {
            var col = RequireColumn(table, s.Column);
            if (!col.IsSelectable || !used.Add(col.Name)) continue;
            parts.Add($"{Base}.{dialect.Quote(col.Name)} {(s.Descending ? "DESC" : "ASC")}");
        }

        // La PK al final garantiza un orden estable entre páginas.
        var tieBreak = table.HasPrimaryKey
            ? table.PrimaryKey
            : [table.Columns.First(c => c.IsSelectable).Name];
        foreach (var k in tieBreak.Where(used.Add))
            parts.Add($"{Base}.{dialect.Quote(k)} ASC");

        return string.Join(", ", parts);
    }

    private string KeyPredicate(TableInfo table, IReadOnlyDictionary<string, object?> key, ParamBag p, string prefix)
    {
        RequireKey(table);
        var parts = new List<string>();
        foreach (var k in table.PrimaryKey)
        {
            var entry = key.FirstOrDefault(e => string.Equals(e.Key, k, StringComparison.OrdinalIgnoreCase));
            if (entry.Key is null || entry.Value is null)
                throw new ValidationException(k, "falta el valor de la clave");
            var col = table.FindColumn(k)!;
            parts.Add($"{prefix}{dialect.Quote(k)} = {dialect.ParameterFor(col, p.Add(entry.Value))}");
        }
        return string.Join(" AND ", parts);
    }

    private static ColumnInfo RequireColumn(TableInfo table, string name) =>
        table.FindColumn(name) ?? throw new ValidationException(name, "la columna no existe");

    private static void RequireKey(TableInfo table)
    {
        if (!table.HasPrimaryKey)
            throw new OperationNotAllowedException($"{table.FullName} no tiene clave primaria; solo se puede listar.");
    }

    private static void RequireEditable(TableInfo table)
    {
        if (!table.IsEditable)
            throw new OperationNotAllowedException(table.Kind == TableKind.View
                ? $"{table.FullName} es una vista; solo se puede listar."
                : $"{table.FullName} no tiene clave primaria; solo se puede listar.");
    }

    private sealed class ParamBag
    {
        private readonly Dictionary<string, object?> _values = new();

        public string Add(object? value)
        {
            var name = $"@p{_values.Count}";
            _values[name[1..]] = value;
            return name;
        }

        public IReadOnlyDictionary<string, object?> Snapshot() => new Dictionary<string, object?>(_values);
    }
}
