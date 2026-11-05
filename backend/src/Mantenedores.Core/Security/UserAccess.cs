using Mantenedores.Core.Schema;

namespace Mantenedores.Core.Security;

/// <summary>
/// Lo que puede hacer un usuario concreto (D-062, D-063), calculado a partir de sus perfiles.
/// <list type="bullet">
/// <item>Sin perfiles definidos en la app: todo abierto (compatibilidad con instalaciones previas a la Fase 3).</item>
/// <item>Administrador (<c>Auth:AdminRoles</c>): todo.</item>
/// <item>Si no: la unión de los perfiles que le tocan, con herencia conexión → tabla → columna.</item>
/// </list>
/// </summary>
public sealed class UserAccess
{
    private readonly IReadOnlyList<SecurityProfile> _profiles;

    private UserAccess(bool unrestricted, IReadOnlyList<SecurityProfile> profiles, bool open, string? username = null, bool admin = false)
    {
        Username = username;
        IsAdmin = admin;
        Unrestricted = unrestricted;
        _profiles = profiles;
        IsOpen = open;
    }

    /// <summary>Quién es (para la auditoría, D-065). Null fuera de una solicitud.</summary>
    public string? Username { get; }

    /// <summary>Sin restricciones de datos (administrador o seguridad abierta).</summary>
    public bool Unrestricted { get; }

    /// <summary>Administrador (<c>Auth:AdminRoles</c>): ve y administra todos los dashboards, también los privados.</summary>
    public bool IsAdmin { get; }

    /// <summary>No hay ningún perfil definido: la seguridad de datos está abierta.</summary>
    public bool IsOpen { get; }

    public IReadOnlyList<string> ProfileNames => _profiles.Select(p => p.Name).ToList();

    public static readonly UserAccess All = new(true, [], open: true, admin: true);

    /// <summary>Sesión embebida con un token firmado (P-135, D-113); null en una sesión normal.</summary>
    public EmbedScope? Embed { get; private init; }

    /// <summary>
    /// Una sesión embebida: el perfil de la aplicación, recortado a las tablas y dashboards del token y a sus operaciones.
    /// Nunca es administrador ni tiene permisos de la app. Sin perfil (se borró), no ve nada.
    /// </summary>
    public static UserAccess Embedded(string username, SecurityProfile? profile, EmbedScope scope) =>
        new(false, profile is null ? [] : [profile], open: false, username) { Embed = scope };

    /// <param name="allProfiles">Todos los perfiles de la app.</param>
    public static UserAccess For(string? username, IEnumerable<string> roles, bool isAdmin, IReadOnlyList<SecurityProfile> allProfiles)
    {
        if (allProfiles.Count == 0) return new UserAccess(true, [], open: true, username, isAdmin);
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mine = allProfiles.Where(p =>
                p.Roles.Any(roleSet.Contains) ||
                (username is not null && p.Users.Contains(username, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        return new UserAccess(isAdmin, mine, open: false, username, isAdmin);
    }

    /// <summary>Ids de sus perfiles: un dashboard compartido con uno de ellos se le muestra (D-111).</summary>
    public IReadOnlySet<string> ProfileIds => _profiles.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Qué puede hacer con los dashboards (P-128): sin restricciones (administrador o sin perfiles), todo; si no, la
    /// unión de sus perfiles. Solo cuentan View, Create, Update y Delete.
    /// </summary>
    public DataOperation Dashboards => Unrestricted
        ? DataOperation.View | DataOperation.Create | DataOperation.Update | DataOperation.Delete
        : _profiles.Aggregate(DataOperation.None, (acc, p) => acc | DataOperations.Parse(p.Dashboards))
          // Embebida solo se mira (y si el perfil de la aplicación deja ver dashboards).
          & (Embed is null ? DataOperation.View | DataOperation.Create | DataOperation.Update | DataOperation.Delete : DataOperation.View);

    /// <summary>Permisos de la app que dan sus perfiles (se suman a los de los roles de la configuración).</summary>
    public IReadOnlyCollection<string> AppPermissions => Embed is not null ? []
        : _profiles.SelectMany(p => p.AppPermissions).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>¿Tiene alguna operación en la conexión (o en alguna de sus tablas)?</summary>
    public bool CanSeeConnection(string connectionId) =>
        Unrestricted || (Embed is null || Embed.HasConnection(connectionId)) && _profiles.Any(p => Rules(p, connectionId).Any(r =>
            DataOperations.Parse(r.Operations) != DataOperation.None || r.Tables.Values.Any(t => DataOperations.Parse(t.Operations) != DataOperation.None)));

    /// <summary>Operaciones sobre una tabla: la regla de la tabla si existe; si no, la de la conexión. Unión de perfiles.</summary>
    public DataOperation Table(string connectionId, string schema, string table)
    {
        if (Unrestricted) return DataOperation.All;
        if (Embed is not null && !Embed.HasTable(connectionId, schema, table)) return DataOperation.None;
        var ops = DataOperation.None;
        foreach (var p in _profiles)
            foreach (var rule in Rules(p, connectionId))
                ops |= TableRuleFor(rule, schema, table) is { } t ? DataOperations.Parse(t.Operations) : DataOperations.Parse(rule.Operations);
        return Embed is null ? ops : ops & Embed.Operations;
    }

    /// <summary>
    /// Operaciones sobre una columna (solo View y Update cuentan): su regla si existe; si no, las de la tabla. Update
    /// significa "se puede escribir": sin regla propia, crear en la tabla basta para llenarla en el alta (D-107); editar
    /// un registro sigue exigiendo Update en la tabla.
    /// </summary>
    public DataOperation Column(string connectionId, string schema, string table, string column)
    {
        if (Unrestricted) return DataOperation.All;
        if (Embed is not null && !Embed.HasTable(connectionId, schema, table)) return DataOperation.None;
        var ops = DataOperation.None;
        foreach (var p in _profiles)
            foreach (var rule in Rules(p, connectionId))
            {
                var t = TableRuleFor(rule, schema, table);
                var tableOps = t is not null ? DataOperations.Parse(t.Operations) : DataOperations.Parse(rule.Operations);
                var c = t?.Columns.FirstOrDefault(kv => string.Equals(kv.Key, column, StringComparison.OrdinalIgnoreCase)).Value;
                ops |= c is not null ? DataOperations.Parse(c.Operations)
                    : tableOps.HasFlag(DataOperation.Create) ? tableOps | DataOperation.Update : tableOps;
            }
        // Embebida, escribir una columna exige que el token deje crear o editar.
        if (Embed is not null && (Embed.Operations & (DataOperation.Create | DataOperation.Update)) == 0) ops &= ~DataOperation.Update;
        return ops & (DataOperation.View | DataOperation.Update);
    }

    /// <summary>
    /// El esquema como lo ve este usuario: sin las tablas que no puede ver, con las columnas que no puede ver ocultas
    /// (no se leen ni se buscan, D-051) y de solo lectura las que no puede editar.
    /// </summary>
    public DatabaseSchema Restrict(string connectionId, DatabaseSchema schema)
    {
        if (Unrestricted) return schema;
        var tables = new List<TableInfo>();
        foreach (var t0 in schema.Tables)
        {
            var ops = Table(connectionId, t0.Schema, t0.Name);
            if (!ops.HasFlag(DataOperation.View)) continue;
            // Embebida con filtros fijos (P-136): la tabla lleva sus valores fijos; sin la columna del filtro, no se abre.
            var t = t0;
            if (Embed is { HasFilters: true } e)
            {
                if (e.WithRowFilter(t0, block: true) is not { } scoped) continue;
                t = scoped;
            }
            var writesTable = (ops & (DataOperation.Create | DataOperation.Update)) != 0;
            var columns = t.Columns.Select(c =>
            {
                var colOps = Column(connectionId, t.Schema, t.Name, c.Name);
                // D-108: quien crea o edita en la tabla ve y escribe sus columnas obligatorias, diga lo que diga la regla
                // de la columna. Si no, una obligatoria en "solo Ver" u oculta haría imposible crear registros.
                if (writesTable && IsRequired(c)) colOps = DataOperation.View | DataOperation.Update;
                return c with
                {
                    Hidden = c.Hidden || !colOps.HasFlag(DataOperation.View),
                    ForcedReadOnly = c.ForcedReadOnly || !colOps.HasFlag(DataOperation.Update)
                };
            }).ToList();
            tables.Add(t with { Columns = columns, Access = ops });
        }
        var visible = tables.Select(t => (t.Schema, t.Name)).ToHashSet();
        // Las relaciones hacia tablas que no ve se quitan de las listas que la UI muestra.
        tables = tables.Select(t => t with
        {
            ReferencedBy = t.ReferencedBy.Where(f => visible.Contains((f.Schema, f.Table))).ToList(),
            Children = t.Children?.Where(f => visible.Contains((f.Schema, f.Table))).ToList(),
            ManyToMany = t.ManyToMany.Where(l => visible.Contains((l.BridgeSchema, l.BridgeTable)) && visible.Contains((l.TargetSchema, l.TargetTable))).ToList()
        }).ToList();
        // Los avisos de ajustes nombran tablas y columnas: son para quien configura, sin restricciones.
        return schema with { Tables = tables, OverrideWarnings = [] };
    }

    /// <summary>
    /// Las tablas que no ve, para textos de FK y combos (D-107), con el filtro fijo de una sesión embebida (P-136): la que
    /// tiene la columna del filtro se filtra por ella, y la que apunta una FK con valor fijo (p. ej. cliente, desde
    /// pedido.cliente_id = 42) se filtra por su clave. Así un combo no lista datos de otro alcance.
    /// </summary>
    public IEnumerable<TableInfo> ScopeLabelTables(IReadOnlyList<TableInfo> visible, IEnumerable<TableInfo> others)
    {
        if (Embed is not { HasFilters: true } e) return others;
        return others.Select(o =>
        {
            var scoped = e.WithRowFilter(o, block: false) ?? o;
            foreach (var v in visible)
                foreach (var fk in v.ForeignKeys.Where(f => f.Columns.Count == 1 &&
                             string.Equals(f.RefSchema, o.Schema, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(f.RefTable, o.Name, StringComparison.OrdinalIgnoreCase)))
                    if (v.FindColumn(fk.Columns[0]) is { FixedValue: { } value } && fk.RefColumns.Count == 1)
                        scoped = scoped with { Columns = scoped.Columns.Select(c =>
                            string.Equals(c.Name, fk.RefColumns[0], StringComparison.OrdinalIgnoreCase) ? c with { FixedValue = value, ForcedReadOnly = true } : c).ToList() };
            return scoped;
        }).ToList();
    }

    /// <summary>Obligatoria al crear según la base: no acepta nulos, no tiene default y la base no la genera.</summary>
    public static bool IsRequired(ColumnInfo c) =>
        !c.IsNullable && !c.HasDefault && !c.IsIdentity && !c.IsComputed && !c.IsRowVersion;

    private static IEnumerable<ConnectionRule> Rules(SecurityProfile p, string connectionId) =>
        p.Connections.Where(kv => kv.Key == "*" || string.Equals(kv.Key, connectionId, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value);

    private static TableRule? TableRuleFor(ConnectionRule rule, string schema, string table) =>
        rule.Tables.FirstOrDefault(kv => string.Equals(kv.Key, $"{schema}.{table}", StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>
/// Hasta dónde llega una sesión embebida (P-135): las tablas del token (más las de las tarjetas de sus dashboards), sus
/// dashboards y las operaciones que el token deja. Los nombres se comparan sin distinguir mayúsculas.
/// </summary>
public sealed record EmbedScope(string AppId, DataOperation Operations, IReadOnlySet<string> Tables, IReadOnlySet<string> Dashboards)
{
    /// <summary>Filtros fijos por fila del token (P-136): nombre del filtro → valor, como texto invariante.</summary>
    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();

    /// <summary>Otra columna para un filtro en una tabla: "esquema.tabla.filtro" (minúsculas) → columna.</summary>
    public IReadOnlyDictionary<string, string> FilterColumns { get; init; } = new Dictionary<string, string>();

    /// <summary>Tablas que se abren sin el filtro aunque no tengan su columna (catálogos): "esquema.tabla" en minúsculas.</summary>
    public IReadOnlySet<string> Unfiltered { get; init; } = new HashSet<string>();

    public bool HasFilters => Filters.Count > 0;

    /// <summary>
    /// La tabla con sus columnas de filtro fijas y de solo lectura. Sin ninguna columna del filtro (o con un valor que no
    /// calza con su tipo): null si <paramref name="block"/> y la tabla no es de las "sin filtro"; si no, la tabla tal cual.
    /// </summary>
    public TableInfo? WithRowFilter(TableInfo t, bool block)
    {
        var fixedValues = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, raw) in Filters)
        {
            var columnName = FilterColumns.TryGetValue($"{t.Schema}.{t.Name}.{name}".ToLowerInvariant(), out var alias) ? alias : name;
            if (t.FindColumn(columnName) is not { } col) continue;
            try { fixedValues[col.Name] = Query.ValueConverter.FromString(col, raw); }
            catch (Errors.ValidationException) { return block ? null : t; } // un valor que no calza no abre nada
        }
        if (fixedValues.Count == 0)
            return !block || Unfiltered.Contains($"{t.Schema}.{t.Name}".ToLowerInvariant()) ? t : null;
        return t with
        {
            Columns = t.Columns.Select(c => fixedValues.TryGetValue(c.Name, out var v) ? c with { FixedValue = v, ForcedReadOnly = true } : c).ToList()
        };
    }

    public static string TableKey(string connectionId, string schema, string table) =>
        $"{connectionId}\u001f{schema}\u001f{table}".ToLowerInvariant();

    public bool HasTable(string connectionId, string schema, string table) => Tables.Contains(TableKey(connectionId, schema, table));

    public bool HasConnection(string connectionId) =>
        Tables.Any(k => k.StartsWith(connectionId.ToLowerInvariant() + "\u001f", StringComparison.Ordinal));
}

/// <summary>Quién hace la solicitud actual. Core no conoce HTTP: la API lo implementa con el usuario del token.</summary>
public interface IAccessContext
{
    ValueTask<UserAccess> CurrentAsync(CancellationToken ct);
}

/// <summary>Sin usuario (pruebas, tareas internas): todo permitido.</summary>
public sealed class UnrestrictedAccess : IAccessContext
{
    public ValueTask<UserAccess> CurrentAsync(CancellationToken ct) => ValueTask.FromResult(UserAccess.All);
}

/// <summary>
/// Perfiles con una caché corta. Con metadata compartida, un cambio se avisa a las demás instancias (D-077);
/// el vencimiento de 30 s queda de respaldo.
/// </summary>
public sealed class SecurityService(ISecurityStore store, Services.ChangeStamps? stamps = null)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private (DateTimeOffset At, IReadOnlyList<SecurityProfile> Profiles)? _cache;

    public ISecurityStore Store => store;

    public async Task<IReadOnlyList<SecurityProfile>> ProfilesAsync(CancellationToken ct)
    {
        if (_cache is { } c && DateTimeOffset.UtcNow - c.At < Ttl) return c.Profiles;
        var profiles = await store.ListProfilesAsync(ct);
        _cache = (DateTimeOffset.UtcNow, profiles);
        return profiles;
    }

    /// <summary>Solo en esta instancia.</summary>
    public void Invalidate() => _cache = null;

    /// <summary>Cambió un perfil o un usuario: se recarga aquí y en las demás instancias.</summary>
    public async Task ChangedAsync(CancellationToken ct)
    {
        Invalidate();
        if (stamps is not null) await stamps.PublishAsync(Services.ChangeStamps.Security, ct);
    }

    public async Task<UserAccess> AccessForAsync(string? username, IEnumerable<string> roles, bool isAdmin, CancellationToken ct) =>
        UserAccess.For(username, roles, isAdmin, await ProfilesAsync(ct));
}
