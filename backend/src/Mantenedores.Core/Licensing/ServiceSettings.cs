namespace Mantenedores.Core.Licensing;

/// <summary>
/// Ajustes del servicio clave → valor (P-116): la licencia pegada en la pantalla Plan, la prueba Pro y las tablas
/// elegidas del plan Gratis. Va en la metadata (migración 008) o en <c>settings.json</c>.
/// </summary>
public interface IServiceSettings
{
    Task<string?> GetAsync(string name, CancellationToken ct);

    /// <summary>Guarda el valor; nulo lo borra.</summary>
    Task SetAsync(string name, string? value, CancellationToken ct);
}

/// <summary>En memoria (pruebas de Core).</summary>
public sealed class MemoryServiceSettings : IServiceSettings
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    public Task<string?> GetAsync(string name, CancellationToken ct) => Task.FromResult(_values.TryGetValue(name, out var v) ? v : null);
    public Task SetAsync(string name, string? value, CancellationToken ct)
    {
        if (value is null) _values.TryRemove(name, out _); else _values[name] = value;
        return Task.CompletedTask;
    }
}
