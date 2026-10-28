using System.Data.Common;
using System.Text.RegularExpressions;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Providers;
using MySqlConnector;

namespace Mantenedores.Providers.MySql;

/// <summary>Proveedor único para MySQL 8.0+ y MariaDB 10.6+ (D-034), con MySqlConnector (D-030).</summary>
public sealed partial class MySqlProvider : IDatabaseProvider
{
    public static readonly Version MinimumMySql = new(8, 0);
    public static readonly Version MinimumMariaDb = new(10, 6);

    public DatabaseEngine Engine => DatabaseEngine.MySql;
    public string DisplayName => "MySQL / MariaDB";
    public string? ExampleConnectionString => "Server=mi-servidor;Port=3306;Database=mibase;User ID=usuario;Password=***";
    public ISqlDialect Dialect { get; } = new MySqlDialect();
    public ISchemaProvider Schema { get; } = new MySqlSchemaProvider();

    /// <summary>
    /// Se fuerza <c>ConvertZeroDateTime</c>: las fechas "0000-00-00" (permitidas con modos SQL laxos)
    /// se leen como el valor mínimo en vez de fallar.
    /// </summary>
    public DbConnection CreateConnection(string connectionString) =>
        new MySqlConnection(new MySqlConnectionStringBuilder(connectionString) { ConvertZeroDateTime = true }.ConnectionString);

    public string ApplyPool(string connectionString, PoolOptions pool)
    {
        var b = new DbConnectionStringBuilder { ConnectionString = connectionString };
        PoolOptions.SetIfMissing(b, pool.MaxSize, "Maximum Pool Size", "Max Pool Size", "MaxPoolSize", "MaximumPoolsize");
        PoolOptions.SetIfMissing(b, pool.IdleSeconds, "Connection Idle Timeout", "ConnectionIdleTimeout");
        return b.ConnectionString;
    }

    /// <summary>TIMESTAMP se convierte con la zona de la sesión: se fija en UTC para que sea predecible.</summary>
    public async Task InitializeSessionAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SET time_zone = '+00:00'";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)")]
    private static partial Regex LeadingVersion();

    public async Task<ServerInfo> GetServerInfoAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT VERSION(), @@version_comment, @@sql_mode, @@collation_database, " +
                          "CAST(@@lower_case_table_names AS CHAR), @@time_zone";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);

        var raw = r.GetString(0); // p. ej. "8.0.36" o "10.6.18-MariaDB-ubu2004"
        var isMariaDb = raw.Contains("MariaDB", StringComparison.OrdinalIgnoreCase);
        var m = LeadingVersion().Match(raw);
        var version = m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : new Version(0, 0);

        return new ServerInfo(isMariaDb ? "MariaDB" : "MySQL", version, isMariaDb ? MinimumMariaDb : MinimumMySql,
            new Dictionary<string, string>
            {
                ["version"] = raw,
                ["version_comment"] = r.IsDBNull(1) ? "" : r.GetString(1),
                // Ajustes que cambian el comportamiento (docs/motores.md): se muestran para diagnosticar.
                ["sql_mode"] = r.IsDBNull(2) ? "" : r.GetString(2),
                ["collation_database"] = r.IsDBNull(3) ? "" : r.GetString(3),
                ["lower_case_table_names"] = r.IsDBNull(4) ? "" : r.GetString(4),
                ["time_zone"] = r.IsDBNull(5) ? "" : r.GetString(5)
            });
    }

    [GeneratedRegex(@"CONSTRAINT `([^`]+)`")]
    private static partial Regex ConstraintInBackticks();

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex SingleQuoted();

    /// <summary>
    /// Clasifica por número de error; el nombre de la restricción sale del texto entre comillas o backticks,
    /// que no cambia con el idioma del servidor (D-038).
    /// </summary>
    public DbErrorInfo ClassifyError(DbException exception)
    {
        if (exception is not MySqlException my) return DbErrorInfo.Unknown;
        var msg = my.Message;
        return my.Number switch
        {
            // Duplicate entry 'x' for key 'tabla.indice' (MySQL 8) o 'indice' (MariaDB).
            1062 => new(DbErrorKind.Unique, LastQuoted(msg)?.Split('.').Last()),
            // 1452: FK al insertar o actualizar; 1451: FK al borrar.
            1451 or 1452 => new(DbErrorKind.ForeignKey, Backticked(msg)),
            // 3819: Check constraint 'x' is violated (MySQL); 4025: CONSTRAINT `x` failed (MariaDB).
            3819 => new(DbErrorKind.Check, FirstQuoted(msg)),
            4025 => new(DbErrorKind.Check, Backticked(msg)),
            // 1048: Column 'x' cannot be null; 1364: Field 'x' doesn't have a default value.
            1048 or 1364 => new(DbErrorKind.NotNull, Column: FirstQuoted(msg)),
            1142 or 1143 => new(DbErrorKind.PermissionDenied), // comando o columna denegados al usuario
            1054 or 1146 => new(DbErrorKind.SchemaMismatch),   // columna o tabla inexistente
            _ => DbErrorInfo.Unknown
        };
    }

    private static string? Backticked(string msg) => ConstraintInBackticks().Match(msg) is { Success: true } m ? m.Groups[1].Value : null;
    private static string? FirstQuoted(string msg) => SingleQuoted().Match(msg) is { Success: true } m ? m.Groups[1].Value : null;
    private static string? LastQuoted(string msg)
    {
        var all = SingleQuoted().Matches(msg);
        return all.Count > 0 ? all[^1].Groups[1].Value : null;
    }
}
