namespace Mantenedores.Core.Embed;

/// <summary>
/// Una aplicación que embebe Celdo con tokens firmados (P-135, D-113): desde qué sitios, con qué perfil como máximo y con
/// qué clave firma sus tokens.
/// </summary>
public sealed record EmbedApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Sitios que pueden embeber sus páginas (esquema y host, como <c>Embed:AllowedOrigins</c>).</summary>
    public IReadOnlyList<string> Origins { get; init; } = [];
    /// <summary>Perfil con el máximo de permisos que dan sus tokens.</summary>
    public required string ProfileId { get; init; }
    public bool Enabled { get; init; } = true;
    /// <summary>
    /// Filtros por fila (P-136): otra columna para un filtro en una tabla, "esquema.tabla.filtro" → columna
    /// (p. ej. "ventas.pedido.cliente_id" → "id_cliente").
    /// </summary>
    public IReadOnlyDictionary<string, string> FilterColumns { get; init; } = new Dictionary<string, string>();
    /// <summary>Tablas ("esquema.tabla") que se abren sin filtro aunque no tengan la columna (catálogos como categoría).</summary>
    public IReadOnlyList<string> Unfiltered { get; init; } = [];
    /// <summary>Clave para firmar los tokens (HS256), cifrada con las llaves de la app. Nunca sale en claro después de crearla.</summary>
    public required string ProtectedSecret { get; init; }
    /// <summary>Cuándo se creó o rotó la clave: las sesiones abiertas con una clave anterior dejan de valer.</summary>
    public DateTimeOffset SecretSetAt { get; init; }
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public interface IEmbedAppStore
{
    Task<IReadOnlyList<EmbedApp>> ListAsync(CancellationToken ct);
    Task SaveAsync(EmbedApp app, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}

/// <summary>En memoria (pruebas).</summary>
public sealed class InMemoryEmbedAppStore : IEmbedAppStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, EmbedApp> _items = new();
    public Task<IReadOnlyList<EmbedApp>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EmbedApp>>(_items.Values.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id, StringComparer.Ordinal).ToList());
    public Task SaveAsync(EmbedApp app, CancellationToken ct) { _items[app.Id] = app; return Task.CompletedTask; }
    public Task<bool> DeleteAsync(string id, CancellationToken ct) => Task.FromResult(_items.TryRemove(id, out _));
}
