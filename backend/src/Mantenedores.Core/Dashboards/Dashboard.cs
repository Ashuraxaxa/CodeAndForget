using Mantenedores.Core.Query;

namespace Mantenedores.Core.Dashboards;

/// <summary>Tipo de tarjeta (D-111). Cuántas agrupaciones lleva: Number 0, Pivot 2, Map (etapa 5), el resto 1.</summary>
public enum WidgetType
{
    Number,
    Bar,
    Line,
    Pie,
    Top,
    Pivot,
    Map
}

/// <summary>Ancho de la tarjeta en la cuadrícula de 12 columnas: S = 3, M = 6, L = 12.</summary>
public enum WidgetSize
{
    S,
    M,
    L
}

/// <summary>Una tarjeta: de qué tabla sale y qué resume. La consulta usa los permisos de quien mira.</summary>
public sealed record DashboardWidget
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public WidgetType Type { get; init; } = WidgetType.Number;
    public WidgetSize Size { get; init; } = WidgetSize.M;
    public required string Connection { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }

    /// <summary>
    /// Métrica, agrupaciones, orden, tope y filtros. <c>DateColumn</c> marca la fecha a la que aplica el rango del
    /// dashboard; From y To se toman del dashboard al pedir los datos.
    /// </summary>
    public AggregateRequest Query { get; init; } = new();
}

/// <summary>Con qué perfil se comparte (D-111: solo perfiles) y si ese perfil puede editarlo.</summary>
public sealed record DashboardShare(string Profile, bool CanEdit = false);

/// <summary>Dashboard de un usuario (P-128, D-111). El orden de <see cref="Widgets"/> es el de la cuadrícula.</summary>
public sealed record Dashboard
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    /// <summary>Quien lo creó: lo ve, edita, comparte y borra siempre.</summary>
    public required string Owner { get; init; }
    public IReadOnlyList<DashboardWidget> Widgets { get; init; } = [];
    public IReadOnlyList<DashboardShare> Shares { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

public interface IDashboardStore
{
    Task<IReadOnlyList<Dashboard>> ListAsync(CancellationToken ct);
    Task<Dashboard?> GetAsync(string id, CancellationToken ct);
    Task SaveAsync(Dashboard dashboard, CancellationToken ct);
    Task<bool> DeleteAsync(string id, CancellationToken ct);
}

/// <summary>Sin almacén (p. ej. en pruebas que no los usan): no hay dashboards y no se pueden crear.</summary>
public sealed class NoDashboardStore : IDashboardStore
{
    public Task<IReadOnlyList<Dashboard>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Dashboard>>([]);
    public Task<Dashboard?> GetAsync(string id, CancellationToken ct) => Task.FromResult<Dashboard?>(null);
    public Task SaveAsync(Dashboard dashboard, CancellationToken ct) => throw new NotSupportedException("Este almacén no guarda dashboards.");
    public Task<bool> DeleteAsync(string id, CancellationToken ct) => Task.FromResult(false);
}

/// <summary>Almacén en memoria: para pruebas y para la metadata en modo archivo se usa el de archivo.</summary>
public sealed class InMemoryDashboardStore : IDashboardStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dashboard> _items = new();
    public Task<IReadOnlyList<Dashboard>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Dashboard>>(_items.Values.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id).ToList());
    public Task<Dashboard?> GetAsync(string id, CancellationToken ct) => Task.FromResult(_items.GetValueOrDefault(id));
    public Task SaveAsync(Dashboard dashboard, CancellationToken ct) { _items[dashboard.Id] = dashboard; return Task.CompletedTask; }
    public Task<bool> DeleteAsync(string id, CancellationToken ct) => Task.FromResult(_items.TryRemove(id, out _));
}
