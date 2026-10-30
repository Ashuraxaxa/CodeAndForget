using System.Text.Json;
using Mantenedores.Api.Auth;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Licensing;
using Mantenedores.Core.Security;
using Microsoft.Extensions.Options;

namespace Mantenedores.Api.Licensing;

/// <summary>Sección <c>License</c>: la clave por configuración (p. ej. <c>License__Key</c>), útil en Docker y en modo Jwt.</summary>
public sealed class LicenseOptions
{
    public const string Section = "License";

    /// <summary>Clave de licencia. Si está, manda sobre la pegada en la pantalla Plan.</summary>
    public string Key { get; set; } = "";
}

/// <summary>Tablas elegidas del plan Gratis (hasta 15, fijas, D-106).</summary>
public sealed record FreeSelection(string? Connection, IReadOnlyList<string> Tables, DateTimeOffset? LockedAt);

/// <summary>El plan vigente y por qué (P-116).</summary>
public sealed record PlanStatus(
    Plan Plan, PlanLimits Limits, LicenseInfo? License, string Source, string? LicenseError,
    bool InGrace, DateTimeOffset? GraceUntil, DateTimeOffset? TrialEndsAt, bool TrialAvailable,
    IReadOnlyList<string> RequiresBusiness, FreeSelection Selection, string? FreeConnection)
{
    /// <summary>¿Hay que elegir las tablas antes de usar la app? Solo en Gratis, con conexión y sin tablas elegidas.</summary>
    public bool NeedsTableSelection => Limits.Tables is not null && FreeConnection is not null && Selection.Tables.Count == 0;
}

/// <summary>
/// Plan vigente de la instalación (P-116, D-106): licencia por configuración o pegada en Plan, prueba Pro de 30 días
/// o Gratis. Se calcula sin internet y se guarda unos segundos (con varias instancias, cada una lo relee de la metadata).
/// </summary>
public sealed class LicenseService(
    IServiceSettings settings, IConnectionStore connections, ISecurityStore security, IOptions<AuthOptions> auth,
    IOptions<LicenseOptions> options, IReadOnlyList<string> businessFeaturesInUse, TimeProvider? clock = null) : IPlanScope
{
    public const int GraceDays = 14;
    public const int TrialDays = 30;
    public const string LicenseSetting = "license.key";
    public const string TrialSetting = "license.trial";
    public const string TablesSetting = "license.free-tables";

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (DateTimeOffset At, PlanStatus Status, IReadOnlySet<string>? Seats)? _cached;

    /// <summary>El último calculado (para quien no puede esperar, como los avisos por correo). Gratis hasta el primero.</summary>
    public PlanStatus? Current => _cached?.Status;

    public async Task<PlanStatus> GetAsync(CancellationToken ct) => (await StateAsync(ct)).Status;

    public void Invalidate() => _cached = null;

    public async ValueTask<PlanAccess> CurrentAsync(CancellationToken ct)
    {
        var s = await GetAsync(ct);
        if (s.Limits.Connections is null) return new PlanAccess(null, null, s.Plan);
        return new PlanAccess(s.FreeConnection ?? "", s.Selection.Tables.Select(t => t.ToLowerInvariant()).ToHashSet(), s.Plan);
    }

    /// <summary>Falla con un mensaje claro si el plan no incluye la función.</summary>
    public async Task RequireAsync(Feature feature, CancellationToken ct)
    {
        var s = await GetAsync(ct);
        if (!s.Limits.Has(feature))
            throw new PlanLimitException($"{FeatureName(feature)}: disponible desde el plan {PlanName(PlanLimits.Requires(feature))}.");
    }

    /// <summary>¿Este usuario tiene un lugar en el plan? Los primeros N: los de la configuración y luego los propios activos por nombre.</summary>
    public async Task<bool> HasSeatAsync(string username, CancellationToken ct)
    {
        var seats = (await StateAsync(ct)).Seats;
        return seats is null || seats.Contains(username.Trim().ToLowerInvariant());
    }

    public async Task EnsureSeatAsync(string username, CancellationToken ct)
    {
        if (!await HasSeatAsync(username, ct)) throw UsersLimit(await GetAsync(ct));
    }

    /// <summary>Antes de crear o activar un usuario propio.</summary>
    public async Task EnsureRoomForUserAsync(CancellationToken ct)
    {
        var s = await GetAsync(ct);
        if (s.Limits.Users is not { } max) return;
        if (await ActiveUsersAsync(ct) is { Count: var n } && n >= max) throw UsersLimit(s);
    }

    /// <summary>Antes de crear una conexión.</summary>
    public async Task EnsureRoomForConnectionAsync(CancellationToken ct)
    {
        var s = await GetAsync(ct);
        if (s.Limits.Connections is { } max && (await connections.ListAsync(ct)).Count >= max)
            throw new PlanLimitException($"El plan {PlanName(s.Plan)} permite {max} conexión. Pase a Pro para agregar más.");
    }

    public async Task<int> ActiveUserCountAsync(CancellationToken ct) => (await ActiveUsersAsync(ct)).Count;

    /// <summary>Pega una licencia nueva (pantalla Plan). Se valida antes de guardarla.</summary>
    public async Task<PlanStatus> SetLicenseAsync(string? key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) await settings.SetAsync(LicenseSetting, null, ct);
        else
        {
            var info = LicenseKey.Verify(key, TrustedLicenseKeys.All);
            if (info.ExpiresAt is { } exp && exp.AddDays(GraceDays) < _clock.GetUtcNow())
                throw new ValidationException("license", $"La licencia venció el {exp:yyyy-MM-dd}.");
            await settings.SetAsync(LicenseSetting, key.Trim(), ct);
        }
        Invalidate();
        return await GetAsync(ct);
    }

    /// <summary>Empieza la prueba Pro de 30 días. Una sola vez por instalación y solo sin licencia.</summary>
    public async Task<PlanStatus> StartTrialAsync(CancellationToken ct)
    {
        var s = await GetAsync(ct);
        if (!s.TrialAvailable) throw new ValidationException("trial", "La prueba Pro ya se usó en esta instalación o hay una licencia activa.");
        await settings.SetAsync(TrialSetting, _clock.GetUtcNow().ToString("O"), ct);
        Invalidate();
        return await GetAsync(ct);
    }

    /// <summary>
    /// Suma tablas a las elegidas del plan Gratis (D-106). Las ya elegidas no se pueden quitar ni cambiar; el total
    /// no pasa de 15. Los nombres son "esquema.tabla" y se validan contra el catálogo de la conexión.
    /// </summary>
    public async Task<PlanStatus> AddFreeTablesAsync(IReadOnlyCollection<string> tables, Func<string, bool> exists, CancellationToken ct)
    {
        var s = await GetAsync(ct);
        if (s.Limits.Tables is not { } max) throw new ValidationException("tables", "Solo el plan Gratis elige tablas: su plan las incluye todas.");
        if (s.FreeConnection is null) throw new ValidationException("tables", "Cree primero la conexión.");
        var wanted = tables.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
        if (wanted.FirstOrDefault(t => !exists(t)) is { } missing) throw new ValidationException("tables", $"La tabla '{missing}' no existe en '{s.FreeConnection}'.");
        var all = s.Selection.Tables.Concat(wanted).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (all.Count > max) throw new ValidationException("tables", $"El plan Gratis permite {max} tablas: quedan {max - s.Selection.Tables.Count} por elegir.");
        var selection = new FreeSelection(s.FreeConnection, all, s.Selection.LockedAt ?? _clock.GetUtcNow());
        await settings.SetAsync(TablesSetting, JsonSerializer.Serialize(selection, Json), ct);
        Invalidate();
        return await GetAsync(ct);
    }

    /// <summary>Funciones del plan Empresa configuradas en esta instalación: sin esa licencia, la API no atiende.</summary>
    public static IReadOnlyList<string> BusinessFeatures(IConfiguration config, AuthMode mode, int plugins)
    {
        var list = new List<string>();
        if (mode == AuthMode.Jwt) list.Add("inicio de sesión con proveedor de identidad (Auth:Mode=Jwt)");
        if (config.GetSection(Metadata.MetadataOptions.Section).Get<Metadata.MetadataOptions>() is { Provider: not (Metadata.MetadataProvider.Sqlite or Metadata.MetadataProvider.File) })
            list.Add("metadata en PostgreSQL o SQL Server (Metadata:Provider)");
        if (config.GetSection(Metadata.DataProtectionSettings.Section).Get<Metadata.DataProtectionSettings>() is { KeyStore: Metadata.KeyStore.Database })
            list.Add("llaves de cifrado en la base (DataProtection:KeyStore=Database)");
        if (string.Equals(config[$"{Infrastructure.CacheOptions.Section}:Provider"], "Redis", StringComparison.OrdinalIgnoreCase))
            list.Add("caché compartida en Redis (Cache:Provider=Redis)");
        if (plugins > 0) list.Add("motores como plugins (Providers:PluginsDirectory)");
        if (config[$"{Infrastructure.TelemetryOptions.Section}:OtlpEndpoint"] is { Length: > 0 }) list.Add("OpenTelemetry (Telemetry:OtlpEndpoint)");
        return list;
    }

    public static string PlanName(Plan plan) => plan switch { Plan.Free => "Gratis", Plan.Pro => "Pro", _ => "Empresa" };

    public static string FeatureName(Feature feature) => feature switch
    {
        Feature.Profiles => "Perfiles y permisos",
        Feature.AllFormats => "Excel, JSON y XML",
        Feature.ImportUpsert => "Importar actualizando por clave",
        Feature.Notifications => "Avisos por correo",
        Feature.Actions => "Botones con procedimientos",
        Feature.CopySettings => "Copiar ajustes entre conexiones",
        Feature.Ai => "IA por MCP y tokens de acceso",
        Feature.Sso => "Inicio de sesión con proveedor de identidad",
        _ => "Varias instancias, metadata compartida, Redis, plugins y OpenTelemetry"
    };

    private static PlanLimitException UsersLimit(PlanStatus s) =>
        new($"El plan {PlanName(s.Plan)} permite {s.Limits.Users} usuarios. Pase a un plan mayor para sumar más.");

    private async Task<(DateTimeOffset At, PlanStatus Status, IReadOnlySet<string>? Seats)> StateAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        if (_cached is { } c && now - c.At < Ttl) return c;
        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is { } again && now - again.At < Ttl) return again;
            var status = await ComputeAsync(now, ct);
            IReadOnlySet<string>? seats = null;
            if (status.Limits.Users is { } max && auth.Value.Mode == AuthMode.Local)
                seats = (await ActiveUsersAsync(ct)).Take(max).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var state = (now, status, seats);
            _cached = state;
            return state;
        }
        finally { _lock.Release(); }
    }

    private async Task<PlanStatus> ComputeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var configured = options.Value.Key.Trim();
        var key = configured.Length > 0 ? configured : await settings.GetAsync(LicenseSetting, ct);
        var source = configured.Length > 0 ? "config" : key is null ? "none" : "stored";

        LicenseInfo? license = null;
        string? error = null;
        var plan = Plan.Free;
        bool inGrace = false;
        DateTimeOffset? graceUntil = null;
        if (key is not null)
        {
            try
            {
                license = LicenseKey.Verify(key, TrustedLicenseKeys.All);
                if (license.ExpiresAt is not { } exp || now <= exp) plan = license.Plan;
                else if (now <= exp.AddDays(GraceDays)) { plan = license.Plan; inGrace = true; graceUntil = exp.AddDays(GraceDays); }
                else error = $"La licencia venció el {exp:yyyy-MM-dd}.";
            }
            catch (ValidationException ex) { error = ex.Errors.Values.FirstOrDefault() ?? ex.Message; }
        }

        DateTimeOffset? trialEnds = null;
        var trialStarted = await settings.GetAsync(TrialSetting, ct) is { } t && DateTimeOffset.TryParse(t, out var started) ? started : (DateTimeOffset?)null;
        if (trialStarted is { } ts) trialEnds = ts.AddDays(TrialDays);
        if (plan == Plan.Free && license is null && trialEnds is { } te && now < te) { plan = Plan.Pro; source = "trial"; }
        var trialAvailable = trialStarted is null && license is null;

        var limits = PlanLimits.For(plan, license?.Plan == plan ? license.Users : null);
        var selection = await settings.GetAsync(TablesSetting, ct) is { } json
            ? JsonSerializer.Deserialize<FreeSelection>(json, Json) ?? new FreeSelection(null, [], null)
            : new FreeSelection(null, [], null);

        // En Gratis, la conexión es la de las tablas elegidas si sigue existiendo; si no, la primera.
        string? freeConnection = null;
        if (limits.Connections is not null)
        {
            var list = (await connections.ListAsync(ct)).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            freeConnection = list.FirstOrDefault(x => string.Equals(x.Id, selection.Connection, StringComparison.OrdinalIgnoreCase))?.Id
                             ?? list.FirstOrDefault()?.Id;
        }

        var requires = plan >= Plan.Business ? [] : businessFeaturesInUse;
        return new PlanStatus(plan, limits, license, source, error, inGrace, graceUntil, trialEnds, trialAvailable, requires, selection, freeConnection);
    }

    /// <summary>Usuarios activos en el orden en que reciben lugar: los de la configuración y luego los propios por nombre.</summary>
    private async Task<List<string>> ActiveUsersAsync(CancellationToken ct)
    {
        var names = auth.Value.Users.Select(u => u.Username.Trim().ToLowerInvariant()).ToList();
        names.AddRange((await security.ListUsersAsync(ct)).Where(u => u.Active).Select(u => u.Username.ToLowerInvariant())
            .Where(n => !names.Contains(n)).Order(StringComparer.Ordinal));
        return names;
    }
}
