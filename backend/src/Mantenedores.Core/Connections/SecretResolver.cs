using System.Text.RegularExpressions;
using Mantenedores.Core.Errors;

namespace Mantenedores.Core.Connections;

/// <summary>Qué referencias a secretos se aceptan en las cadenas de conexión (P-045, D-025).</summary>
public sealed class SecretOptions
{
    public const string Section = "Secrets";

    /// <summary>Solo se leen variables de entorno con este prefijo: así no se puede pedir, p. ej., la llave de la app.</summary>
    public string EnvPrefix { get; set; } = "MNT_";

    /// <summary>
    /// Carpeta de donde se pueden leer archivos (la de los secretos de Docker y Kubernetes por defecto).
    /// Vacío = no se aceptan referencias a archivos.
    /// </summary>
    public string FilesDirectory { get; set; } = "/run/secrets";
}

/// <summary>
/// Reemplaza <c>${env:NOMBRE}</c> y <c>${file:nombre}</c> dentro de una cadena de conexión justo antes de conectar.
/// En la metadata se guarda la referencia, no el secreto. Un error nunca incluye el valor leído.
/// </summary>
public sealed partial class SecretResolver(SecretOptions options)
{
    [GeneratedRegex(@"\$\{(env|file):([^}]+)\}")]
    private static partial Regex Reference();

    public SecretResolver() : this(new SecretOptions()) { }

    public bool HasReferences(string text) => Reference().IsMatch(text);

    public string Resolve(string text) => Reference().Replace(text, m =>
    {
        var name = m.Groups[2].Value.Trim();
        return m.Groups[1].Value == "env" ? FromEnv(name) : FromFile(name);
    });

    private string FromEnv(string name)
    {
        if (options.EnvPrefix.Length > 0 && !name.StartsWith(options.EnvPrefix, StringComparison.Ordinal))
            throw new ValidationException("connectionString",
                $"la variable '{name}' no está permitida: debe empezar con '{options.EnvPrefix}' (Secrets:EnvPrefix)");
        return Environment.GetEnvironmentVariable(name)
               ?? throw new ValidationException("connectionString", $"la variable de entorno '{name}' no existe");
    }

    private string FromFile(string name)
    {
        if (options.FilesDirectory.Length == 0)
            throw new ValidationException("connectionString", "las referencias a archivos están desactivadas (Secrets:FilesDirectory)");
        var root = Path.GetFullPath(options.FilesDirectory);
        var path = Path.GetFullPath(Path.Combine(root, name));
        // Nada fuera de la carpeta permitida (ni "../", ni rutas absolutas a otro lado).
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ValidationException("connectionString", $"el archivo '{name}' está fuera de {options.FilesDirectory} (Secrets:FilesDirectory)");
        if (!File.Exists(path))
            throw new ValidationException("connectionString", $"el archivo de secreto '{name}' no existe en {options.FilesDirectory}");
        // Los archivos de secretos suelen terminar en salto de línea: se quita.
        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }
}
