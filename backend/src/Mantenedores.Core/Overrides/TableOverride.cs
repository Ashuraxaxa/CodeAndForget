namespace Mantenedores.Core.Overrides;

/// <summary>
/// Ajustes de un mantenedor, sin tocar código (P-011). Se guardan como un documento JSON por tabla, columnas incluidas
/// (D-049). Todo es opcional: null = lo automático leído del catálogo.
/// </summary>
public sealed record TableOverride
{
    /// <summary>Nombre visible, p. ej. "Productos".</summary>
    public string? Label { get; init; }

    /// <summary>No se muestra en el menú.</summary>
    public bool? Hidden { get; init; }

    /// <summary>De consulta aunque tenga PK.</summary>
    public bool? ReadOnly { get; init; }

    /// <summary>Columna que se muestra cuando otra tabla la referencia (combos de FK).</summary>
    public string? DisplayColumn { get; init; }

    /// <summary>Orden por defecto del listado, con el mismo formato que la API: "-fecha,nombre".</summary>
    public string? DefaultSort { get; init; }

    /// <summary>Grupo del menú, p. ej. "Ventas". Vacío = el esquema.</summary>
    public string? Group { get; init; }

    /// <summary>Clave para tablas sin PK declarada: habilita la edición.</summary>
    public IReadOnlyList<string>? VirtualPrimaryKey { get; init; }

    /// <summary>Relaciones que existen en la práctica pero no están declaradas en la BD.</summary>
    public IReadOnlyList<VirtualForeignKey>? VirtualForeignKeys { get; init; }

    /// <summary>
    /// Tablas hijas que se muestran en pestañas al editar un registro (P-014), por nombre de la FK que las une,
    /// en el orden en que se muestran. Null = todas; lista vacía = ninguna.
    /// </summary>
    public IReadOnlyList<string>? Children { get; init; }

    /// <summary>Ajustes por columna, por nombre de columna.</summary>
    public IReadOnlyDictionary<string, ColumnOverride>? Columns { get; init; }
}

public sealed record VirtualForeignKey(
    IReadOnlyList<string> Columns,
    string RefSchema,
    string RefTable,
    IReadOnlyList<string> RefColumns,
    string? Name = null);

public sealed record ColumnOverride
{
    public string? Label { get; init; }
    public string? HelpText { get; init; }

    /// <summary>Oculta: no se lee ni se escribe por la API (salvo si es PK, que se usa como clave). Sirve para datos sensibles.</summary>
    public bool? Hidden { get; init; }

    /// <summary>Visible en el formulario pero no en la grilla.</summary>
    public bool? ShowInGrid { get; init; }

    /// <summary>Orden en grilla y formulario (menor primero).</summary>
    public int? Order { get; init; }

    public bool? ReadOnly { get; init; }

    /// <summary>Obligatoria aunque la BD acepte nulos.</summary>
    public bool? Required { get; init; }

    /// <summary>Control de la UI: textarea, email, url, color, money, password.</summary>
    public string? Widget { get; init; }

    /// <summary>Expresión regular que debe cumplir un texto.</summary>
    public string? Pattern { get; init; }

    public decimal? Min { get; init; }
    public decimal? Max { get; init; }

    /// <summary>Largo máximo de un texto (más estricto que la BD).</summary>
    public int? MaxLength { get; init; }

    /// <summary>Lista fija de valores (p. ej. para un texto con CHECK).</summary>
    public IReadOnlyList<string>? Options { get; init; }
}

/// <summary>Dónde se guardan los ajustes (metadata del servicio).</summary>
public interface IOverrideStore
{
    Task<IReadOnlyDictionary<(string Schema, string Table), TableOverride>> ListAsync(string connectionId, CancellationToken ct);
    Task SaveAsync(string connectionId, string schema, string table, TableOverride value, string? user, CancellationToken ct);
    Task<bool> DeleteAsync(string connectionId, string schema, string table, CancellationToken ct);

    /// <summary>Al borrar una conexión: así una nueva con el mismo id no hereda ajustes viejos.</summary>
    Task DeleteAllAsync(string connectionId, CancellationToken ct);
}

/// <summary>Sin ajustes (pruebas y usos sin metadata).</summary>
public sealed class NoOverrideStore : IOverrideStore
{
    public Task<IReadOnlyDictionary<(string Schema, string Table), TableOverride>> ListAsync(string connectionId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<(string, string), TableOverride>>(new Dictionary<(string, string), TableOverride>());

    public Task SaveAsync(string connectionId, string schema, string table, TableOverride value, string? user, CancellationToken ct) =>
        throw new NotSupportedException("Este almacén no guarda ajustes.");

    public Task<bool> DeleteAsync(string connectionId, string schema, string table, CancellationToken ct) => Task.FromResult(false);

    public Task DeleteAllAsync(string connectionId, CancellationToken ct) => Task.CompletedTask;
}
