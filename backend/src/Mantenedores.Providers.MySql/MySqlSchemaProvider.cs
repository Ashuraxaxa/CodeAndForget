using System.Data.Common;
using Dapper;
using Mantenedores.Core.Providers;
using Mantenedores.Core.Schema;

namespace Mantenedores.Providers.MySql;

/// <summary>
/// Lee el catálogo desde information_schema. En MySQL el "esquema" es la base de datos (D-032):
/// por defecto se expone solo la base de la cadena de conexión; la conexión puede listar más.
/// </summary>
public sealed class MySqlSchemaProvider : ISchemaProvider
{
    // Filtro de esquemas: la base actual, o la lista configurada en la conexión.
    private const string SchemaFilter = "(@all = 0 AND {0} = DATABASE() OR @all = 1 AND {0} IN @schemas)";

    private static string Filter(string column) => string.Format(SchemaFilter, column);

    private static readonly string TablesSql = $"""
        SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS TableName, TABLE_TYPE AS Kind
        FROM information_schema.TABLES
        WHERE {Filter("TABLE_SCHEMA")}
        ORDER BY TABLE_SCHEMA, TABLE_NAME
        """;

    private static readonly string ColumnsSql = $"""
        SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName,
               CAST(ORDINAL_POSITION AS SIGNED) AS Ordinal, DATA_TYPE AS DataType, COLUMN_TYPE AS ColumnType,
               IS_NULLABLE AS IsNullable, COLUMN_DEFAULT AS DefaultValue, EXTRA AS Extra,
               CAST(CHARACTER_MAXIMUM_LENGTH AS SIGNED) AS MaxLength,
               CAST(NUMERIC_PRECISION AS SIGNED) AS NumericPrecision, CAST(NUMERIC_SCALE AS SIGNED) AS NumericScale
        FROM information_schema.COLUMNS
        WHERE {Filter("TABLE_SCHEMA")}
        ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION
        """;

    // Solo KEY_COLUMN_USAGE (P-073): MariaDB no muestra TABLE_CONSTRAINTS a un usuario con solo SELECT, y esta vista
    // ya trae lo necesario. En MySQL y MariaDB la PK se llama siempre PRIMARY, una FK tiene tabla referenciada y lo
    // demás son UNIQUE (los índices comunes no aparecen aquí).
    private static readonly string KeysSql = $"""
        SELECT k.CONSTRAINT_NAME AS ConstraintName,
               CASE WHEN k.CONSTRAINT_NAME = 'PRIMARY' THEN 'PRIMARY KEY'
                    WHEN k.REFERENCED_TABLE_NAME IS NOT NULL THEN 'FOREIGN KEY'
                    ELSE 'UNIQUE' END AS ConstraintType,
               k.TABLE_SCHEMA AS SchemaName, k.TABLE_NAME AS TableName, k.COLUMN_NAME AS ColumnName,
               CAST(k.ORDINAL_POSITION AS SIGNED) AS Position,
               k.REFERENCED_TABLE_SCHEMA AS RefSchema, k.REFERENCED_TABLE_NAME AS RefTable, k.REFERENCED_COLUMN_NAME AS RefColumn
        FROM information_schema.KEY_COLUMN_USAGE k
        WHERE {Filter("k.TABLE_SCHEMA")}
        ORDER BY k.TABLE_SCHEMA, k.TABLE_NAME, k.CONSTRAINT_NAME, k.ORDINAL_POSITION
        """;

    private static readonly string SridSql = $"""
        SELECT TABLE_SCHEMA AS SchemaName, TABLE_NAME AS TableName, COLUMN_NAME AS ColumnName, SRS_ID AS SrsId
        FROM information_schema.COLUMNS
        WHERE SRS_ID IS NOT NULL AND {Filter("TABLE_SCHEMA")}
        """;

    private sealed class SridRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public long SrsId { get; set; }
    }

    // MariaDB guarda JSON como LONGTEXT con un CHECK json_valid(`columna`): así se detecta.
    private static readonly string MariaDbJsonSql = $"""
        SELECT CONSTRAINT_SCHEMA AS SchemaName, TABLE_NAME AS TableName, CHECK_CLAUSE AS Clause
        FROM information_schema.CHECK_CONSTRAINTS
        WHERE CHECK_CLAUSE LIKE 'json_valid(%' AND {Filter("CONSTRAINT_SCHEMA")}
        """;

    // Procedimientos y funciones (P-061). La posición 0 de PARAMETERS es el valor que devuelve una función.
    private static readonly string RoutinesSql = $"""
        SELECT CONCAT(r.ROUTINE_TYPE, ':', r.ROUTINE_SCHEMA, '.', r.ROUTINE_NAME) AS RoutineId, r.ROUTINE_SCHEMA AS SchemaName,
               r.ROUTINE_NAME AS RoutineName, r.ROUTINE_TYPE AS Kind, p.PARAMETER_NAME AS ParamName,
               CAST(COALESCE(p.ORDINAL_POSITION, 0) AS SIGNED) AS Ordinal, p.DATA_TYPE AS DataType, p.DTD_IDENTIFIER AS ColumnType,
               p.CHARACTER_MAXIMUM_LENGTH AS MaxLength, p.PARAMETER_MODE AS Mode
        FROM information_schema.ROUTINES r
        LEFT JOIN information_schema.PARAMETERS p ON p.SPECIFIC_SCHEMA = r.ROUTINE_SCHEMA AND p.SPECIFIC_NAME = r.ROUTINE_NAME
             AND p.ROUTINE_TYPE = r.ROUTINE_TYPE AND p.ORDINAL_POSITION > 0
        WHERE {Filter("r.ROUTINE_SCHEMA")}
        ORDER BY 2, 3, 6
        """;

    public async Task<IReadOnlyList<RoutineInfo>> ReadRoutinesAsync(DbConnection connection, IReadOnlyCollection<string>? includeSchemas, CancellationToken ct)
    {
        var args = new { all = includeSchemas is { Count: > 0 } ? 1 : 0, schemas = includeSchemas?.ToArray() ?? ["-"] };
        var rows = await connection.QueryAsync<RoutineRow>(new CommandDefinition(RoutinesSql, args, cancellationToken: ct));
        // En MySQL un procedimiento y una función pueden llamarse igual: van separados por tipo en RoutineId, pero para
        // el usuario serían el mismo nombre; RoutineAssembler los descarta como sobrecarga.
        return RoutineAssembler.Assemble(rows.Select(r => new RawRoutineParameter(
            r.RoutineId, r.SchemaName, r.RoutineName, r.Kind == "PROCEDURE" ? RoutineKind.Procedure : RoutineKind.Function,
            r.ParamName, (int)r.Ordinal, r.ColumnType,
            r.DataType is null ? LogicalType.Unsupported
                : MySqlTypeMapper.Map(r.DataType, r.ColumnType ?? r.DataType, r.MaxLength is > int.MaxValue ? null : (int?)r.MaxLength),
            r.Mode is "OUT" or "INOUT")), includeSchemas);
    }

    private sealed class RoutineRow
    {
        public string RoutineId { get; set; } = "";
        public string SchemaName { get; set; } = "";
        public string RoutineName { get; set; } = "";
        public string Kind { get; set; } = "";
        public string? ParamName { get; set; }
        public long Ordinal { get; set; }
        public string? DataType { get; set; }
        public string? ColumnType { get; set; }
        public long? MaxLength { get; set; }
        public string? Mode { get; set; }
    }

    public async Task<DatabaseSchema> ReadAsync(DbConnection connection, IReadOnlyCollection<string>? includeSchemas, CancellationToken ct)
    {
        var args = new { all = includeSchemas is { Count: > 0 } ? 1 : 0, schemas = includeSchemas?.ToArray() ?? ["-"] };

        var tables = await connection.QueryAsync<TableRow>(new CommandDefinition(TablesSql, args, cancellationToken: ct));
        var columns = await connection.QueryAsync<ColumnRow>(new CommandDefinition(ColumnsSql, args, cancellationToken: ct));
        var keys = await connection.QueryAsync<KeyRow>(new CommandDefinition(KeysSql, args, cancellationToken: ct));

        var version = await connection.ExecuteScalarAsync<string>(new CommandDefinition("SELECT VERSION()", cancellationToken: ct)) ?? "";
        var mariaJson = new HashSet<(string, string, string)>();
        var srids = new Dictionary<(string, string, string), int>();
        var isMariaDb = version.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
        if (!isMariaDb)
        {
            // SRID de cada columna espacial (P-104): MySQL 8 lo informa en SRS_ID; MariaDB no tiene la columna.
            foreach (var s in await connection.QueryAsync<SridRow>(new CommandDefinition(SridSql, args, cancellationToken: ct)))
                srids[(s.SchemaName.ToLowerInvariant(), s.TableName.ToLowerInvariant(), s.ColumnName.ToLowerInvariant())] = (int)s.SrsId;
        }
        if (isMariaDb)
        {
            foreach (var j in await connection.QueryAsync<JsonCheckRow>(new CommandDefinition(MariaDbJsonSql, args, cancellationToken: ct)))
            {
                var col = j.Clause.Split('`').ElementAtOrDefault(1);
                if (col is not null) mariaJson.Add((j.SchemaName.ToLowerInvariant(), j.TableName.ToLowerInvariant(), col.ToLowerInvariant()));
            }
        }

        return SchemaAssembler.Assemble(
            tables.Select(t => new RawTable(t.SchemaName, t.TableName, t.Kind == "VIEW" ? TableKind.View : TableKind.Table)),
            columns.Select(c =>
            {
                var k = (c.SchemaName.ToLowerInvariant(), c.TableName.ToLowerInvariant(), c.ColumnName.ToLowerInvariant());
                var col = ToColumn(c, mariaJson.Contains(k));
                return new RawColumn(c.SchemaName, c.TableName, srids.TryGetValue(k, out var srid) ? col with { Srid = srid } : col);
            }),
            keys.Select(ToKey),
            includeSchemas);
    }

    private static ColumnInfo ToColumn(ColumnRow c, bool isMariaDbJson)
    {
        var length = c.MaxLength is > int.MaxValue ? null : (int?)c.MaxLength;
        var type = isMariaDbJson ? LogicalType.Json : MySqlTypeMapper.Map(c.DataType, c.ColumnType, length);
        var extra = c.Extra.ToLowerInvariant();
        var isDecimal = c.DataType is "decimal" or "numeric";

        // MariaDB informa "NULL" (texto) como default de las columnas nullable sin default.
        var hasDefault = c.DefaultValue is not null && !string.Equals(c.DefaultValue, "NULL", StringComparison.OrdinalIgnoreCase);

        return new ColumnInfo
        {
            Name = c.ColumnName,
            NativeType = MySqlTypeMapper.NativeType(c.DataType, c.ColumnType),
            Type = type,
            Ordinal = (int)c.Ordinal,
            IsNullable = c.IsNullable == "YES",
            IsIdentity = extra.Contains("auto_increment"),
            // "DEFAULT_GENERATED" (MySQL 8) es un default por expresión, no una columna calculada.
            IsComputed = extra.Contains("virtual generated") || extra.Contains("stored generated") ||
                         extra.Contains("persistent generated"),
            HasDefault = hasDefault || extra.Contains("auto_increment"),
            // TIMESTAMP se guarda en UTC y se convierte con la zona de la sesión, que se fija en UTC.
            HasTimeZone = c.DataType == "timestamp",
            MaxLength = type is LogicalType.String or LogicalType.Text ? length : null,
            Precision = isDecimal ? (int?)c.NumericPrecision : null,
            Scale = isDecimal ? (int?)c.NumericScale : null,
            EnumValues = type == LogicalType.Enum ? MySqlTypeMapper.EnumValues(c.ColumnType) : null,
            // DECIMAL admite hasta 65 dígitos: más de 28 no cabe en decimal de .NET (P-023).
            IsLargeNumber = isDecimal && c.NumericPrecision > 28,
            // La igualdad sobre JSON no compara texto, compara documentos: no se ofrece como filtro.
            Filterable = type != LogicalType.Json
        };
    }

    private static RawKeyColumn ToKey(KeyRow k) => new(
        k.ConstraintName,
        k.ConstraintType switch { "PRIMARY KEY" => KeyKind.PrimaryKey, "UNIQUE" => KeyKind.Unique, _ => KeyKind.ForeignKey },
        k.SchemaName, k.TableName, k.ColumnName, (int)k.Position,
        k.RefSchema, k.RefTable, k.RefColumn);

    private sealed class TableRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Kind { get; set; } = "";
    }

    private sealed class ColumnRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public long Ordinal { get; set; }
        public string DataType { get; set; } = "";
        public string ColumnType { get; set; } = "";
        public string IsNullable { get; set; } = "";
        public string? DefaultValue { get; set; }
        public string Extra { get; set; } = "";
        public long? MaxLength { get; set; }
        public long? NumericPrecision { get; set; }
        public long? NumericScale { get; set; }
    }

    private sealed class KeyRow
    {
        public string ConstraintName { get; set; } = "";
        public string ConstraintType { get; set; } = "";
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string ColumnName { get; set; } = "";
        public long Position { get; set; }
        public string? RefSchema { get; set; }
        public string? RefTable { get; set; }
        public string? RefColumn { get; set; }
    }

    private sealed class JsonCheckRow
    {
        public string SchemaName { get; set; } = "";
        public string TableName { get; set; } = "";
        public string Clause { get; set; } = "";
    }
}
