using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Query;

/// <summary>
/// Arma el SQL del CRUD genérico. Reglas de seguridad:
/// 1) tablas y columnas solo se aceptan si existen en la metadata leída del catálogo;
/// 2) todo identificador pasa por <see cref="ISqlDialect.Quote"/>;
/// 3) todo valor va como parámetro.
/// Las diferencias entre motores se piden al dialecto; aquí no hay ramas por motor (D-031).
/// </summary>
public sealed class QueryBuilder(ISqlDialect dialect)
{
    public const string LabelSuffix = "__label";

    /// <summary>
    /// Carácter de escape de LIKE, igual en todos los motores (P-024). No se usa "\" porque en MySQL
    /// su significado depende de <c>NO_BACKSLASH_ESCAPES</c>.
    /// </summary>
    public const char LikeEscape = '!';

    private const string Base = "t0";

    /// <param name="maxPageSize">Tope de filas; por defecto el del listado (200). La exportación usa el suyo.</param>
    public (SqlQuery Data, SqlQuery Count) BuildList(DatabaseSchema schema, TableInfo table, ListRequest request, int? maxPageSize = null)
    {
        var p = new ParamBag(dialect.ParameterPrefix);
        var where = BuildWhere(table, request, p);
        var from = dialect.Table(table.Schema, table.Name) + " " + Base;

        var count = new SqlQuery($"SELECT COUNT(*) FROM {from}{where}", p.Snapshot());

        var (select, joins, renames) = BuildSelectWithLabels(schema, table);
        // Sin orden pedido, el del ajuste de la tabla (P-011).
        var sort = request.Sort.Count > 0 ? request.Sort : SortSpec.ParseList(table.DefaultSort);
        var orderBy = BuildOrderBy(table, sort);

        var pageSize = Math.Clamp(request.PageSize, 1, maxPageSize ?? ListRequest.MaxPageSize);
        var page = Math.Max(request.Page, 1);
        var offset = p.Add((long)(page - 1) * pageSize);
        var limit = p.Add((long)pageSize);

        var sql = $"SELECT {select} FROM {from}{joins}{where} ORDER BY {orderBy} {dialect.Paging(offset, limit)}";
        return (new SqlQuery(sql, p.Snapshot(), renames), count);
    }

    public SqlQuery BuildGetByKey(DatabaseSchema schema, TableInfo table, IReadOnlyDictionary<string, object?> key)
    {
        RequireKey(table);
        var p = new ParamBag(dialect.ParameterPrefix);
        var (select, joins, renames) = BuildSelectWithLabels(schema, table);
        var where = KeyPredicate(table, key, p, $"{Base}.");
        return new SqlQuery(
            $"SELECT {select} FROM {dialect.Table(table.Schema, table.Name)} {Base}{joins} WHERE {where}",
            p.Snapshot(), renames);
    }

    /// <summary>
    /// Arma el INSERT y decide cómo se obtiene la clave de la fila creada (P-020), en este orden:
    /// 1) el motor la devuelve (RETURNING / OUTPUT);
    /// 2) PK autonumérica de una columna y el motor da el último id (MySQL: LAST_INSERT_ID());
    /// 3) la PK vino completa en el alta.
    /// Si no se cumple ninguna, se pide el valor de la clave al usuario.
    /// </summary>
    public InsertQuery BuildInsert(TableInfo table, IReadOnlyDictionary<string, object?> values)
    {
        RequireEditable(table);
        var p = new ParamBag(dialect.ParameterPrefix);
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

        var tableSql = dialect.Table(table.Schema, table.Name);
        var keyCols = table.PrimaryKey.Select(k => table.FindColumn(k)!).ToList();

        if (dialect.InsertReturningKeys(tableSql, cols, exprs, keyCols) is { } returning)
            return new InsertQuery(new SqlQuery(returning, p.Snapshot()), null);

        if (keyCols is [{ IsIdentity: true } identity] && dialect.LastInsertIdExpression is { } lastId)
            return new InsertQuery(new SqlQuery(
                $"{dialect.Insert(tableSql, cols, exprs)}; SELECT {lastId} AS {dialect.Quote(identity.Name)}", p.Snapshot()), null);

        var provided = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in keyCols)
        {
            var entry = values.FirstOrDefault(v => string.Equals(v.Key, k.Name, StringComparison.OrdinalIgnoreCase));
            if (entry.Key is not null && entry.Value is not null)
            {
                provided[k.Name] = entry.Value;
            }
            else if (k is { Type: LogicalType.Uuid, HasDefault: true, IsReadOnly: false })
            {
                // El motor generaría el UUID pero no puede devolverlo (MySQL): lo genera la app y lo envía (D-040).
                var id = Guid.NewGuid();
                provided[k.Name] = id;
                cols.Add(dialect.Quote(k.Name));
                exprs.Add(dialect.ParameterFor(k, p.Add(id)));
            }
        }
        var missingKey = keyCols.Where(k => !provided.ContainsKey(k.Name)).ToList();
        if (missingKey.Count > 0)
            throw new ValidationException(missingKey.ToDictionary(k => k.Name,
                _ => "este motor no puede devolver la clave que genera: ingrese el valor"));

        return new InsertQuery(new SqlQuery(dialect.Insert(tableSql, cols, exprs), p.Snapshot()), provided);
    }

    public SqlQuery BuildUpdate(TableInfo table, IReadOnlyDictionary<string, object?> key, IReadOnlyDictionary<string, object?> values)
    {
        RequireEditable(table);
        var p = new ParamBag(dialect.ParameterPrefix);
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
        var p = new ParamBag(dialect.ParameterPrefix);
        var where = KeyPredicate(table, key, p, "");
        return new SqlQuery($"DELETE FROM {dialect.Table(table.Schema, table.Name)} WHERE {where}", p.Snapshot());
    }

    /// <summary>Opciones para un combo de FK: valor de la PK + texto de la columna a mostrar.</summary>
    public SqlQuery BuildLookup(TableInfo table, string? search, int take)
    {
        if (table.PrimaryKey.Count != 1)
            throw new OperationNotAllowedException($"{table.FullName} necesita una PK de una sola columna para usarse como lista.");

        var p = new ParamBag(dialect.ParameterPrefix);
        var pkCol = table.FindColumn(table.PrimaryKey[0])!;
        var displayCol = (table.DisplayColumn is { } d ? table.FindColumn(d) : null) ?? pkCol;
        var pk = $"{Base}.{dialect.Quote(pkCol.Name)}";
        var display = $"{Base}.{dialect.Quote(displayCol.Name)}";

        var where = "";
        if (!string.IsNullOrWhiteSpace(search))
            where = $" WHERE {dialect.Contains(display, p.Add(LikePattern(search)))}";

        // Se ordena por la columna a mostrar solo si el motor puede ordenarla.
        var orderBy = displayCol.IsSortable ? display : pk;
        var offset = p.Add(0L);
        var limit = p.Add((long)Math.Clamp(take, 1, 100));
        var sql = $"SELECT {dialect.SelectExpression(pkCol, pk)} AS {dialect.Quote("value")}, " +
                  $"{dialect.SelectExpression(displayCol, display)} AS {dialect.Quote("label")} " +
                  $"FROM {dialect.Table(table.Schema, table.Name)} {Base}{where} " +
                  $"ORDER BY {orderBy} {dialect.Paging(offset, limit)}";
        return new SqlQuery(sql, p.Snapshot());
    }

    /// <summary>
    /// Valores vinculados en una relación N:M (P-015): la clave del otro lado y su texto a mostrar,
    /// p. ej. las etiquetas de un producto. Sin paginar: una relación N:M de un registro es corta.
    /// </summary>
    public SqlQuery BuildLinkedValues(TableInfo bridge, TableInfo target, ManyToManyLink link, object? localValue)
    {
        var p = new ParamBag(dialect.ParameterPrefix);
        var local = bridge.FindColumn(link.LocalColumn)!;
        var foreign = bridge.FindColumn(link.ForeignColumn)!;
        var targetKey = target.FindColumn(link.TargetColumn)!;
        var display = (target.DisplayColumn is { } d ? target.FindColumn(d) : null) is { IsSelectable: true } dc ? dc : targetKey;

        var value = $"b.{dialect.Quote(foreign.Name)}";
        var label = $"t.{dialect.Quote(display.Name)}";
        var sql = $"SELECT {dialect.SelectExpression(foreign, value)} AS {dialect.Quote("value")}, " +
                  $"{dialect.SelectExpression(display, label)} AS {dialect.Quote("label")} " +
                  $"FROM {dialect.Table(bridge.Schema, bridge.Name)} b " +
                  $"LEFT JOIN {dialect.Table(target.Schema, target.Name)} t ON t.{dialect.Quote(targetKey.Name)} = b.{dialect.Quote(foreign.Name)} " +
                  $"WHERE b.{dialect.Quote(local.Name)} = {dialect.ParameterFor(local, p.Add(localValue))} " +
                  $"ORDER BY {(display.IsSortable ? label : value)}";
        return new SqlQuery(sql, p.Snapshot());
    }

    /// <summary>
    /// Un nivel del árbol de una tabla con autorreferencia (P-016): los hijos de <paramref name="parent"/>, o las raíces
    /// si es null (sin padre, o con un padre que ya no existe). Cada nodo dice si tiene hijos, para expandirlo bajo demanda.
    /// </summary>
    public SqlQuery BuildTreeLevel(TableInfo table, object? parent, int take)
    {
        var fk = table.SelfReference ?? throw new OperationNotAllowedException($"{table.FullName} no tiene una relación consigo misma.");
        var p = new ParamBag(dialect.ParameterPrefix);
        var pkCol = table.FindColumn(table.PrimaryKey[0])!;
        var parentCol = table.FindColumn(fk.Columns[0])!;
        var displayCol = (table.DisplayColumn is { } d ? table.FindColumn(d) : null) is { IsSelectable: true } dc ? dc : pkCol;
        var t = dialect.Table(table.Schema, table.Name);
        string Col(string alias, ColumnInfo c) => $"{alias}.{dialect.Quote(c.Name)}";

        var where = parent is null
            ? $"{Col(Base, parentCol)} IS NULL OR NOT EXISTS (SELECT 1 FROM {t} p WHERE {Col("p", pkCol)} = {Col(Base, parentCol)})"
            : $"{Col(Base, parentCol)} = {dialect.ParameterFor(parentCol, p.Add(parent))}";
        var hasChildren = $"CASE WHEN EXISTS (SELECT 1 FROM {t} c WHERE {Col("c", parentCol)} = {Col(Base, pkCol)}) THEN 1 ELSE 0 END";
        var orderBy = displayCol.IsSortable ? Col(Base, displayCol) : Col(Base, pkCol);
        var offset = p.Add(0L);
        var limit = p.Add((long)Math.Clamp(take, 1, 1000));

        var sql = $"SELECT {dialect.SelectExpression(pkCol, Col(Base, pkCol))} AS {dialect.Quote("value")}, " +
                  $"{dialect.SelectExpression(displayCol, Col(Base, displayCol))} AS {dialect.Quote("label")}, " +
                  $"{hasChildren} AS {dialect.Quote("children")} " +
                  $"FROM {t} {Base} WHERE {where} ORDER BY {orderBy} {dialect.Paging(offset, limit)}";
        return new SqlQuery(sql, p.Snapshot());
    }

    /// <summary>El padre de un registro (un paso hacia la raíz), para detectar ciclos.</summary>
    public SqlQuery BuildParentOf(TableInfo table, object? key)
    {
        var fk = table.SelfReference!;
        var p = new ParamBag(dialect.ParameterPrefix);
        var pkCol = table.FindColumn(table.PrimaryKey[0])!;
        var parentCol = table.FindColumn(fk.Columns[0])!;
        return new SqlQuery(
            $"SELECT {dialect.SelectExpression(parentCol, $"{Base}.{dialect.Quote(parentCol.Name)}")} " +
            $"FROM {dialect.Table(table.Schema, table.Name)} {Base} WHERE {Base}.{dialect.Quote(pkCol.Name)} = {dialect.ParameterFor(pkCol, p.Add(key))}",
            p.Snapshot());
    }

    /// <summary>Patrón "contiene" con los comodines del usuario escapados con <see cref="LikeEscape"/>.</summary>
    public static string LikePattern(string search)
    {
        var e = LikeEscape.ToString();
        var escaped = search.Trim()
            .Replace(e, e + e)
            .Replace("%", e + "%")
            .Replace("_", e + "_")
            .Replace("[", e + "[");
        return $"%{escaped}%";
    }

    /// <summary>Alias del SELECT para la etiqueta de la FK número <paramref name="i"/> (corto para no exceder límites de nombre).</summary>
    public static string LabelAlias(int i) => $"__lbl{i}";

    private (string Select, string Joins, IReadOnlyDictionary<string, string> Renames) BuildSelectWithLabels(
        DatabaseSchema schema, TableInfo table)
    {
        var selectable = table.Columns.Where(c => c.IsSelectable).ToList();
        if (selectable.Count == 0)
            throw new OperationNotAllowedException($"{table.FullName} no tiene columnas de tipos soportados.");

        var parts = selectable.Select(c => SelectColumn(c, Base, c.Name)).ToList();
        var joins = new List<string>();
        var renames = new Dictionary<string, string>();
        var i = 0;

        foreach (var fk in table.ForeignKeys.Where(f => f.IsSingleColumn))
        {
            var target = schema.FindTable(fk.RefSchema, fk.RefTable);
            var display = target?.DisplayColumn is { } d ? target.FindColumn(d) : null;
            // Si la columna a mostrar es la misma clave, la etiqueta no aporta nada.
            // Tampoco si la columna local está oculta: la etiqueta revelaría su valor.
            if (target is null || display is null || !display.IsSelectable || table.FindColumn(fk.Columns[0]) is not { IsSelectable: true } ||
                string.Equals(display.Name, fk.RefColumns[0], StringComparison.OrdinalIgnoreCase))
                continue;

            var join = $"j{i}";
            var alias = LabelAlias(i++);
            joins.Add($" LEFT JOIN {dialect.Table(target.Schema, target.Name)} {join} " +
                      $"ON {join}.{dialect.Quote(fk.RefColumns[0])} = {Base}.{dialect.Quote(fk.Columns[0])}");
            parts.Add(SelectColumn(display, join, alias, forceAlias: true));
            renames[alias] = fk.Columns[0] + LabelSuffix;
        }

        return (string.Join(", ", parts), string.Concat(joins), renames);
    }

    /// <summary>Columna del SELECT; si el dialecto la transforma (p. ej. CAST), se le pone alias para conservar el nombre.</summary>
    private string SelectColumn(ColumnInfo column, string tableAlias, string alias, bool forceAlias = false)
    {
        var raw = $"{tableAlias}.{dialect.Quote(column.Name)}";
        var expr = dialect.SelectExpression(column, raw);
        return forceAlias || expr != raw ? $"{expr} AS {dialect.Quote(alias)}" : raw;
    }

    private string BuildWhere(TableInfo table, ListRequest request, ParamBag p)
    {
        var predicates = new List<string>();

        foreach (var (name, raw) in request.Filters)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            var col = RequireColumn(table, name);
            if (!col.IsFilterable) throw new ValidationException(col.Name, "no se puede filtrar por esta columna");

            var colExpr = $"{Base}.{dialect.Quote(col.Name)}";
            predicates.Add(col.Type is LogicalType.String or LogicalType.Text
                ? dialect.Contains(colExpr, p.Add(LikePattern(raw)))
                : $"{colExpr} = {dialect.ParameterFor(col, p.Add(ValueConverter.FromString(col, raw)))}");
        }

        foreach (var (name, raw) in request.Exact)
        {
            var col = RequireColumn(table, name);
            // Una columna oculta solo se acepta si es parte de una FK: así no se puede adivinar su valor probando.
            var inForeignKey = table.ForeignKeys.Any(f => f.Columns.Contains(col.Name, StringComparer.OrdinalIgnoreCase));
            if (col.Type is LogicalType.Unsupported or LogicalType.Binary or LogicalType.Json || (col.Hidden && !inForeignKey && !col.IsPrimaryKey))
                throw new ValidationException(col.Name, "no se puede filtrar por esta columna");
            var colExpr = $"{Base}.{dialect.Quote(col.Name)}";
            predicates.Add($"{colExpr} = {dialect.ParameterFor(col, p.Add(ValueConverter.FromString(col, raw)))}");
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Las columnas ocultas no se buscan: revelaría su contenido.
            var textCols = table.Columns.Where(c => c.IsSelectable && c.Type is LogicalType.String or LogicalType.Text).ToList();
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
            if (!col.IsSortable) throw new ValidationException(col.Name, "no se puede ordenar por esta columna");
            if (!used.Add(col.Name)) continue;
            parts.Add($"{Base}.{dialect.Quote(col.Name)} {(s.Descending ? "DESC" : "ASC")}");
        }

        // La PK al final garantiza un orden estable entre páginas.
        var tieBreak = table.HasPrimaryKey
            ? table.PrimaryKey
            : table.Columns.Where(c => c.IsSortable).Select(c => c.Name).Take(1).ToList();
        foreach (var k in tieBreak.Where(used.Add))
            parts.Add($"{Base}.{dialect.Quote(k)} ASC");

        // Sin ninguna columna ordenable: orden constante (SQL Server exige ORDER BY para paginar).
        return parts.Count > 0 ? string.Join(", ", parts) : "(SELECT NULL)";
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

    /// <summary>Parámetros numerados (p0, p1…). En el SQL llevan el prefijo del dialecto; en el diccionario, no.</summary>
    private sealed class ParamBag(string prefix)
    {
        private readonly Dictionary<string, object?> _values = new();

        public string Add(object? value)
        {
            var name = $"p{_values.Count}";
            _values[name] = value;
            return prefix + name;
        }

        public IReadOnlyDictionary<string, object?> Snapshot() => new Dictionary<string, object?>(_values);
    }
}
