namespace Mantenedores.Core.Providers;

public enum DatabaseEngine
{
    SqlServer,
    Postgres,

    /// <summary>MySQL 8.0+ y MariaDB 10.6+ (un solo proveedor, D-034).</summary>
    MySql
}
