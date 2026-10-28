using System.Data.Common;

namespace Mantenedores.Core.Providers;

/// <summary>
/// Tope de conexiones abiertas por cada conexión configurada y cierre de las inactivas (P-051, D-070), sección
/// <c>Database:Pool</c>. Se agregan a la cadena de conexión solo si esta no los trae: lo que escribió el usuario manda.
/// </summary>
public sealed class PoolOptions
{
    public const string Section = "Database:Pool";

    /// <summary>Máximo de conexiones abiertas a la vez hacia una misma base. 0 = lo que use el driver.</summary>
    public int MaxSize { get; set; } = 20;

    /// <summary>Segundos que una conexión puede quedar sin usar antes de cerrarse. 0 = lo que use el driver.</summary>
    public int IdleSeconds { get; set; } = 300;

    /// <summary>
    /// Agrega <paramref name="value"/> con la primera clave de <paramref name="keys"/> si la cadena no trae ninguna.
    /// Los proveedores lo usan con los nombres (y sinónimos) que acepta su driver.
    /// </summary>
    public static void SetIfMissing(DbConnectionStringBuilder b, int value, params string[] keys)
    {
        if (value <= 0 || keys.Any(b.ContainsKey)) return;
        b[keys[0]] = value;
    }
}
