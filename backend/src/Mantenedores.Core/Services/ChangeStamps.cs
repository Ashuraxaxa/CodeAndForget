using System.Collections.Concurrent;

namespace Mantenedores.Core.Services;

/// <summary>
/// Un contador por "ámbito" en la metadata compartida (D-077): cada instancia lo sube al cambiar algo que las otras
/// tienen en memoria, y las otras lo revisan cada pocos segundos.
/// </summary>
public interface IChangeStampStore
{
    /// <summary>Todos los contadores: ámbito → versión.</summary>
    Task<IReadOnlyDictionary<string, long>> ListAsync(CancellationToken ct);

    /// <summary>Sube en uno el contador del ámbito (lo crea si no existe) y devuelve la versión nueva.</summary>
    Task<long> BumpAsync(string scope, CancellationToken ct);
}

/// <summary>
/// Avisos entre instancias de que algo cambió (P-082). Sin almacén compartido (metadata en archivo: una sola
/// instancia) no hace nada. Los ámbitos son <see cref="Overrides"/>, <see cref="Schema"/> y <see cref="Security"/>.
/// </summary>
public sealed class ChangeStamps(IChangeStampStore? store = null, Action<Exception>? onError = null)
{
    public const string Security = "security";
    public static string Overrides(string connectionId) => "overrides:" + connectionId.Trim().ToLowerInvariant();
    public static string Schema(string connectionId) => "schema:" + connectionId.Trim().ToLowerInvariant();

    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);
    private volatile bool _primed;

    public bool Enabled => store is not null;

    /// <summary>
    /// Avisa a las demás instancias. Si falla, el cambio ya está guardado: se registra el error y las otras instancias
    /// lo verán al refrescar o al vencer sus cachés.
    /// </summary>
    public async Task PublishAsync(string scope, CancellationToken ct)
    {
        if (store is null) return;
        try
        {
            var version = await store.BumpAsync(scope, ct);
            // Lo propio no se vuelve a procesar al revisar.
            _seen.AddOrUpdate(scope, version, (_, old) => Math.Max(old, version));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onError?.Invoke(ex);
        }
    }

    /// <summary>
    /// Ámbitos que otra instancia cambió desde la revisión anterior. La primera revisión solo toma nota de las
    /// versiones actuales: al arrancar, las cachés están vacías.
    /// </summary>
    public async Task<IReadOnlyList<string>> PollAsync(CancellationToken ct)
    {
        if (store is null) return [];
        var current = await store.ListAsync(ct);
        var changed = new List<string>();
        foreach (var (scope, version) in current)
        {
            var known = _seen.TryGetValue(scope, out var seen);
            if (known && seen >= version) continue;
            _seen[scope] = version;
            if (_primed) changed.Add(scope);
        }
        _primed = true;
        return changed;
    }
}
