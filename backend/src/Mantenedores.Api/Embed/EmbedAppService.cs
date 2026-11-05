using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mantenedores.Api.Infrastructure;
using Mantenedores.Api.Licensing;
using Mantenedores.Core.Dashboards;
using Mantenedores.Core.Embed;
using Mantenedores.Core.Errors;
using Mantenedores.Core.Licensing;
using Mantenedores.Core.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Mantenedores.Api.Embed;

/// <summary>Una aplicación como la ve el administrador: sin la clave, y si el plan la deja usar.</summary>
public sealed record EmbedAppView(string Id, string Name, IReadOnlyList<string> Origins, string ProfileId, bool Enabled, bool Locked,
    DateTimeOffset SecretSetAt, string CreatedBy, DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string> FilterColumns, IReadOnlyList<string> Unfiltered);

/// <summary>Lo que se guarda al crear o editar una aplicación.</summary>
public sealed record EmbedAppInput(string? Name, IReadOnlyList<string>? Origins, string? ProfileId, bool? Enabled,
    IReadOnlyDictionary<string, string>? FilterColumns = null, IReadOnlyList<string>? Unfiltered = null);

/// <summary>La lista de aplicaciones y el tope del plan.</summary>
public sealed record EmbedAppList(IReadOnlyList<EmbedAppView> Items, int? Limit, bool CanCreate);

/// <summary>Sesión embebida que se entrega a la página: un token propio (<c>emb_…</c>) que vence con el firmado.</summary>
public sealed record EmbedSession(string Token, DateTimeOffset ExpiresAt, string App, string User);

/// <summary>Lo que lleva una sesión embebida, cifrado dentro de su token.</summary>
public sealed record EmbedSessionPayload(string App, long SecretSetAt, string Sub, string? Name, int Operations,
    IReadOnlyList<string> Tables, IReadOnlyList<string> Dashboards, IReadOnlyDictionary<string, string>? Filters = null);

/// <summary>Cómo se aplican los filtros de una aplicación, tomado de su configuración actual en cada solicitud.</summary>
public sealed record EmbedRowConfig(IReadOnlyDictionary<string, string> FilterColumns, IReadOnlyList<string> Unfiltered);

/// <summary>
/// Aplicaciones que embeben con tokens firmados (P-135, D-113).
/// <list type="bullet">
/// <item>Cada una tiene sitios permitidos, un perfil con el máximo de permisos y una clave para firmar (HS256), que se
/// muestra una sola vez y se guarda cifrada.</item>
/// <item>El servidor de la otra app firma un JWT corto (<c>kid</c> = id de la aplicación) y Celdo lo canjea por una sesión
/// propia recortada a lo que dice el token.</item>
/// <item>Tope del plan: Gratis 0, Pro 3, Empresa sin tope. Al bajar de plan quedan en uso las más antiguas.</item>
/// </list>
/// </summary>
public sealed class EmbedAppService(IEmbedAppStore store, SecurityService security, IDashboardStore dashboards, LicenseService license,
    IDataProtectionProvider protection, IReadOnlyList<string> configuredOrigins, TimeProvider? clock = null)
{
    public const string SessionPrefix = "emb_";
    public const string SecretPrefix = "csk_";
    public const int MaxNameLength = 100;
    public const int MaxResources = 50;
    public const int MaxSubLength = 200;
    public const int MaxFilters = 10;
    public static readonly TimeSpan MaxTokenLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lo que puede pedir un token en <c>perm</c>, y la operación de datos de cada uno.</summary>
    public static readonly IReadOnlyDictionary<string, DataOperation> PermissionNames = new Dictionary<string, DataOperation>(StringComparer.OrdinalIgnoreCase)
    {
        ["view"] = DataOperation.View, ["create"] = DataOperation.Create, ["edit"] = DataOperation.Update,
        ["delete"] = DataOperation.Delete, ["export"] = DataOperation.Export, ["import"] = DataOperation.Import,
        ["execute"] = DataOperation.Execute
    };

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly IDataProtector _secrets = protection.CreateProtector("Celdo.EmbedApp.Secret");
    private readonly ITimeLimitedDataProtector _sessions = protection.CreateProtector("Celdo.EmbedSession").ToTimeLimitedDataProtector();
    private (DateTimeOffset At, IReadOnlyList<EmbedApp> Apps, int? Limit)? _cache;

    // ---------- Administración ----------

    public async Task<EmbedAppList> ListAsync(CancellationToken ct)
    {
        var (apps, limit) = await StateAsync(ct);
        return new EmbedAppList(apps.Select((a, i) => ViewOf(a, limit, i)).ToList(), limit, limit is null || apps.Count < limit);
    }

    /// <summary>Crea la aplicación y devuelve su clave en claro, la única vez que se muestra.</summary>
    public async Task<(EmbedAppView App, string Secret)> CreateAsync(EmbedAppInput input, string createdBy, CancellationToken ct)
    {
        await license.RequireAsync(Feature.Embed, ct);
        var (apps, limit) = await StateAsync(ct, fresh: true);
        if (limit is { } max && apps.Count >= max)
            throw new PlanLimitException($"El plan {LicenseService.PlanName((await license.GetAsync(ct)).Plan)} permite {max} aplicaciones que embeben. Pase a Empresa para sumar más.");
        var (name, origins, profile) = await ValidateAsync(input, null, ct);
        var (columns, unfiltered) = ValidateRows(input, null);
        var now = _clock.GetUtcNow();
        var secret = NewSecret();
        var app = new EmbedApp
        {
            Id = NewId(), Name = name, Origins = origins, ProfileId = profile, Enabled = input.Enabled ?? true,
            FilterColumns = columns, Unfiltered = unfiltered,
            ProtectedSecret = _secrets.Protect(secret), SecretSetAt = now, CreatedBy = createdBy, CreatedAt = now, UpdatedAt = now
        };
        await store.SaveAsync(app, ct);
        Invalidate();
        return (ViewOf(app, null, 0), secret);
    }

    public async Task<EmbedAppView> UpdateAsync(string id, EmbedAppInput input, CancellationToken ct)
    {
        await license.RequireAsync(Feature.Embed, ct);
        var app = await FindAsync(id, ct);
        var (name, origins, profile) = await ValidateAsync(input, app, ct);
        var (columns, unfiltered) = ValidateRows(input, app);
        var saved = app with
        {
            Name = name, Origins = origins, ProfileId = profile, Enabled = input.Enabled ?? app.Enabled,
            FilterColumns = columns, Unfiltered = unfiltered, UpdatedAt = _clock.GetUtcNow()
        };
        await store.SaveAsync(saved, ct);
        Invalidate();
        return (await ListAsync(ct)).Items.First(a => a.Id == id);
    }

    /// <summary>Nueva clave: los tokens firmados con la anterior y las sesiones abiertas con ella dejan de valer.</summary>
    public async Task<string> RotateSecretAsync(string id, CancellationToken ct)
    {
        var app = await FindAsync(id, ct);
        var secret = NewSecret();
        var now = _clock.GetUtcNow();
        await store.SaveAsync(app with { ProtectedSecret = _secrets.Protect(secret), SecretSetAt = now, UpdatedAt = now }, ct);
        Invalidate();
        return secret;
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        if (!await store.DeleteAsync(id, ct)) throw new NotFoundException($"La aplicación '{id}' no existe.");
        Invalidate();
    }

    /// <summary>Sitios que pueden embeber /embed: los de la configuración y los de las aplicaciones en uso.</summary>
    public async Task<IReadOnlyList<string>> AllowedOriginsAsync(CancellationToken ct)
    {
        var (apps, limit) = await StateAsync(ct);
        return configuredOrigins.Concat(apps.Where((a, i) => a.Enabled && !IsLocked(limit, i)).SelectMany(a => a.Origins))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    // ---------- Canje del token firmado ----------

    /// <summary>
    /// Valida el JWT de la app anfitriona y entrega una sesión propia. Errores de firma, vencimiento o aplicación dan
    /// <see cref="EmbedTokenException"/> (401); un token bien firmado pero mal armado, <see cref="ValidationException"/>.
    /// </summary>
    public async Task<EmbedSession> ExchangeAsync(string token, CancellationToken ct)
    {
        if (!(await license.GetAsync(ct)).Limits.Has(Feature.Embed))
            throw new PlanLimitException("Embeber en otras aplicaciones: disponible desde el plan Pro.");
        var handler = new JsonWebTokenHandler();
        JsonWebToken jwt;
        try { jwt = handler.ReadJsonWebToken(token); }
        catch (Exception e) when (e is ArgumentException or SecurityTokenException) { throw new EmbedTokenException("El token no es un JWT válido."); }

        var (apps, limit) = await StateAsync(ct);
        var index = apps.ToList().FindIndex(a => a.Id == jwt.Kid);
        if (index < 0 || !apps[index].Enabled || IsLocked(limit, index))
            throw new EmbedTokenException("La aplicación del token (kid) no existe, está desactivada o supera el tope del plan.");
        var app = apps[index];

        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = Skew,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secrets.Unprotect(app.ProtectedSecret))) { KeyId = app.Id },
            TryAllIssuerSigningKeys = false
        });
        if (!result.IsValid)
            throw new EmbedTokenException(result.Exception is SecurityTokenExpiredException ? "El token venció." : "La firma del token no es válida.");

        var now = _clock.GetUtcNow();
        var expires = new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero);
        if (expires - now > MaxTokenLifetime + Skew)
            throw new ValidationException("exp", "El token puede durar 1 hora como máximo.");
        var filters = Filters(jwt);

        var sub = jwt.Subject?.Trim() ?? "";
        if (sub.Length == 0 || sub.Length > MaxSubLength || sub.Any(char.IsControl))
            throw new ValidationException("sub", $"El token necesita 'sub' (el usuario de la otra app), de hasta {MaxSubLength} caracteres.");
        var name = jwt.TryGetPayloadValue<string>("name", out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim()[..Math.Min(n.Trim().Length, MaxNameLength)] : null;

        var ops = DataOperation.View;
        foreach (var p in Strings(jwt, "perm"))
            ops |= PermissionNames.TryGetValue(p, out var op) ? op
                : throw new ValidationException("perm", $"'{p}' no es un permiso válido. Use: {string.Join(", ", PermissionNames.Keys)}.");

        var resources = Strings(jwt, "res");
        if (resources.Count is 0 or > MaxResources)
            throw new ValidationException("res", $"El token necesita 'res' con lo que puede abrir (de 1 a {MaxResources}).");
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var boards = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in resources)
        {
            if (r.StartsWith("dashboard:", StringComparison.OrdinalIgnoreCase))
            {
                var id = r["dashboard:".Length..];
                var d = await dashboards.GetAsync(id, ct) ?? throw new ValidationException("res", $"El dashboard '{id}' no existe.");
                boards.Add(d.Id);
                // Las tarjetas leen sus tablas: entran al alcance, siempre dentro del perfil de la aplicación.
                foreach (var w in d.Widgets) tables.Add(EmbedScope.TableKey(w.Connection, w.Schema, w.Table));
            }
            else if (r.StartsWith("table:", StringComparison.OrdinalIgnoreCase) && r["table:".Length..].Split('/') is [{ Length: > 0 } c, { Length: > 0 } s, { Length: > 0 } t])
                tables.Add(EmbedScope.TableKey(c, s, t));
            else
                throw new ValidationException("res", $"'{r}' no es válido. Use 'dashboard:<id>' o 'table:<conexión>/<esquema>/<tabla>'.");
        }

        var payload = new EmbedSessionPayload(app.Id, app.SecretSetAt.ToUnixTimeMilliseconds(), sub, name, (int)ops, [.. tables], [.. boards],
            filters.Count > 0 ? filters : null);
        var session = SessionPrefix + _sessions.Protect(JsonSerializer.Serialize(payload, Json), expires);
        return new EmbedSession(session, expires, app.Name, name ?? sub);
    }

    /// <summary>Abre una sesión embebida. Null si venció, se alteró o la aplicación ya no la permite.</summary>
    public async Task<(EmbedApp App, EmbedSessionPayload Payload)?> ReadSessionAsync(string token, CancellationToken ct)
    {
        if (!token.StartsWith(SessionPrefix, StringComparison.Ordinal)) return null;
        EmbedSessionPayload? payload;
        try { payload = JsonSerializer.Deserialize<EmbedSessionPayload>(_sessions.Unprotect(token[SessionPrefix.Length..]), Json); }
        catch (CryptographicException) { return null; }
        if (payload is null) return null;
        var (apps, limit) = await StateAsync(ct);
        var index = apps.ToList().FindIndex(a => a.Id == payload.App);
        if (index < 0 || !apps[index].Enabled || IsLocked(limit, index)) return null;
        if (apps[index].SecretSetAt.ToUnixTimeMilliseconds() != payload.SecretSetAt) return null; // se rotó la clave
        if (!(await license.GetAsync(ct)).Limits.Has(Feature.Embed)) return null;
        return (apps[index], payload);
    }

    /// <summary>El acceso de una sesión embebida: el perfil de la aplicación recortado al token.</summary>
    public static UserAccess AccessFor(string username, IReadOnlyList<SecurityProfile> profiles, string profileId, EmbedSessionPayload p,
        EmbedRowConfig? rows = null) =>
        UserAccess.Embedded(username, profiles.FirstOrDefault(x => x.Id == profileId), new EmbedScope(p.App, (DataOperation)p.Operations,
            p.Tables.ToHashSet(StringComparer.Ordinal), p.Dashboards.ToHashSet(StringComparer.Ordinal))
        {
            Filters = p.Filters ?? new Dictionary<string, string>(),
            FilterColumns = (rows?.FilterColumns ?? new Dictionary<string, string>())
                .ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value, StringComparer.Ordinal),
            Unfiltered = (rows?.Unfiltered ?? []).Select(u => u.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal)
        });

    public static string SerializePayload(EmbedSessionPayload p) => JsonSerializer.Serialize(p, Json);
    public static EmbedSessionPayload? DeserializePayload(string json) => JsonSerializer.Deserialize<EmbedSessionPayload>(json, Json);
    public static string SerializeRows(EmbedApp a) => JsonSerializer.Serialize(new EmbedRowConfig(a.FilterColumns, a.Unfiltered), Json);
    public static EmbedRowConfig? DeserializeRows(string json) => JsonSerializer.Deserialize<EmbedRowConfig>(json, Json);

    /// <summary>Nombre del usuario externo en la auditoría: <c>app:{aplicación}/{usuario}</c>.</summary>
    public static string UserName(EmbedApp app, EmbedSessionPayload p) => $"app:{app.Name}/{p.Sub}";

    // ---------- Internos ----------

    private async Task<(string Name, IReadOnlyList<string> Origins, string Profile)> ValidateAsync(EmbedAppInput input, EmbedApp? current, CancellationToken ct)
    {
        var name = (input.Name ?? current?.Name ?? "").Trim();
        if (name.Length is 0 or > MaxNameLength) throw new ValidationException("name", $"El nombre es obligatorio y de hasta {MaxNameLength} caracteres.");
        if (name.Contains('/')) throw new ValidationException("name", "El nombre no puede llevar '/' (se usa en la auditoría: app:nombre/usuario).");
        var others = (await StateAsync(ct)).Apps.Where(a => a.Id != current?.Id);
        if (others.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationException("name", $"Ya hay una aplicación '{name}'.");

        IReadOnlyList<string> origins;
        try { origins = EmbedSetup.Validate(new EmbedOptions { AllowedOrigins = [.. input.Origins ?? current?.Origins ?? []] }); }
        catch (InvalidOperationException e) { throw new ValidationException("origins", e.Message.Replace("Embed:AllowedOrigins: ", "")); }
        if (origins.Count == 0) throw new ValidationException("origins", "Indique al menos un sitio desde donde se embebe, p. ej. https://portal.empresa.com.");

        var profile = (input.ProfileId ?? current?.ProfileId ?? "").Trim();
        if (!(await security.ProfilesAsync(ct)).Any(p => p.Id == profile))
            throw new ValidationException("profileId", "Elija el perfil con el máximo de permisos que dan sus tokens.");
        return (name, origins, profile);
    }

    /// <summary>"esquema.tabla.filtro" → columna, y "esquema.tabla" sin filtro. Solo nombres: se usan contra el catálogo.</summary>
    private static (IReadOnlyDictionary<string, string> Columns, IReadOnlyList<string> Unfiltered) ValidateRows(EmbedAppInput input, EmbedApp? current)
    {
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, column) in input.FilterColumns ?? current?.FilterColumns ?? new Dictionary<string, string>())
        {
            var k = key.Trim();
            if (k.Split('.') is not [{ Length: > 0 }, { Length: > 0 }, { Length: > 0 }] || string.IsNullOrWhiteSpace(column))
                throw new ValidationException("filterColumns", $"'{key}' no es válido. Use esquema.tabla.filtro = columna, p. ej. ventas.pedido.cliente_id = id_cliente.");
            columns[k] = column.Trim();
        }
        var unfiltered = new List<string>();
        foreach (var raw in input.Unfiltered ?? current?.Unfiltered ?? [])
        {
            var u = raw.Trim();
            if (u.Length == 0) continue;
            if (u.Split('.') is not [{ Length: > 0 }, { Length: > 0 }])
                throw new ValidationException("unfiltered", $"'{raw}' no es válido. Use esquema.tabla, p. ej. ventas.categoria.");
            if (!unfiltered.Contains(u, StringComparer.OrdinalIgnoreCase)) unfiltered.Add(u);
        }
        return (columns, unfiltered);
    }

    /// <summary>Filtros fijos del token: <c>{ "cliente_id": 42 }</c>. Textos, números o sí/no; nunca nulos, listas ni objetos.</summary>
    private static Dictionary<string, string> Filters(JsonWebToken jwt)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var present = jwt.Claims.Any(c => c.Type == "filters");
        if (!jwt.TryGetPayloadValue<JsonElement>("filters", out var el) || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return present ? throw new ValidationException("filters", "'filters' debe ser un objeto, p. ej. { \"cliente_id\": 42 }.") : result;
        if (el.ValueKind != JsonValueKind.Object)
            throw new ValidationException("filters", "'filters' debe ser un objeto, p. ej. { \"cliente_id\": 42 }.");
        foreach (var prop in el.EnumerateObject())
        {
            var value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString()!,
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => throw new ValidationException("filters", $"El filtro '{prop.Name}' debe ser un texto, un número o sí/no.")
            };
            if (prop.Name.Trim().Length is 0 or > 100 || value.Length is 0 or > 200)
                throw new ValidationException("filters", $"El filtro '{prop.Name}' necesita un nombre y un valor (hasta 200 caracteres).");
            result[prop.Name.Trim()] = value;
        }
        if (result.Count > MaxFilters) throw new ValidationException("filters", $"Hasta {MaxFilters} filtros por token.");
        return result;
    }

    private async Task<EmbedApp> FindAsync(string id, CancellationToken ct) =>
        (await StateAsync(ct, fresh: true)).Apps.FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException($"La aplicación '{id}' no existe.");

    private async Task<(IReadOnlyList<EmbedApp> Apps, int? Limit)> StateAsync(CancellationToken ct, bool fresh = false)
    {
        var now = _clock.GetUtcNow();
        if (!fresh && _cache is { } c && now - c.At < Ttl) return (c.Apps, c.Limit);
        var apps = await store.ListAsync(ct);
        var limit = (await license.GetAsync(ct)).Limits.EmbedApps;
        _cache = (now, apps, limit);
        return (apps, limit);
    }

    public void Invalidate() => _cache = null;

    private static bool IsLocked(int? limit, int index) => limit is { } max && index >= max;

    private static EmbedAppView ViewOf(EmbedApp a, int? limit, int index) =>
        new(a.Id, a.Name, a.Origins, a.ProfileId, a.Enabled, IsLocked(limit, index), a.SecretSetAt, a.CreatedBy, a.CreatedAt,
            a.FilterColumns, a.Unfiltered);

    private static List<string> Strings(JsonWebToken jwt, string claim)
    {
        if (!jwt.TryGetPayloadValue<JsonElement>(claim, out var el)) return [];
        return el.ValueKind switch
        {
            JsonValueKind.Array => el.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()!.Trim()
                : throw new ValidationException(claim, $"'{claim}' debe ser una lista de textos.")).Where(x => x.Length > 0).ToList(),
            JsonValueKind.String => [el.GetString()!.Trim()],
            _ => throw new ValidationException(claim, $"'{claim}' debe ser una lista de textos.")
        };
    }

    private static string NewSecret() => SecretPrefix + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
}

/// <summary>El token firmado no se pudo usar (firma, vencimiento o aplicación): 401.</summary>
public sealed class EmbedTokenException(string message) : Exception(message);
