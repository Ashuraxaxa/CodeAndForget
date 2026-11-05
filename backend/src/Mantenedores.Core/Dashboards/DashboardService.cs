using Mantenedores.Core.Errors;
using Mantenedores.Core.Licensing;
using Mantenedores.Core.Query;
using Mantenedores.Core.Security;
using Mantenedores.Core.Services;

namespace Mantenedores.Core.Dashboards;

/// <summary>Un dashboard tal como lo ve un usuario: qué puede hacer con él y si el plan lo deja usar.</summary>
/// <param name="Locked">Pasa el tope del plan (se bajó de plan): se lista, pero no se abre ni se edita hasta subir.</param>
public sealed record DashboardView(Dashboard Dashboard, bool CanEdit, bool CanDelete, bool CanShare, bool Locked);

/// <summary>La lista de dashboards de un usuario y si puede crear otro.</summary>
public sealed record DashboardList(IReadOnlyList<DashboardView> Items, bool CanCreate, int Used, int? Limit, int WidgetLimit);

/// <summary>Lo que se guarda al crear o editar.</summary>
public sealed record DashboardInput(string Name, string? Description, IReadOnlyList<DashboardWidget>? Widgets, IReadOnlyList<DashboardShare>? Shares);

/// <summary>
/// Dashboards (P-128, D-111). Reglas:
/// <list type="bullet">
/// <item>Permisos del perfil: Ver, Crear, Editar y Borrar dashboards (<see cref="UserAccess.Dashboards"/>).</item>
/// <item>El dueño ve, edita, comparte y borra el suyo; los demás lo ven si se compartió con uno de sus perfiles, y lo
/// editan o borran si además se compartió con "puede editar". El administrador, todos.</item>
/// <item>Tope por instalación del plan: 1 / 5 / sin tope, y de tarjetas por dashboard. Al bajar de plan quedan en uso
/// los más antiguos; los demás se bloquean, nunca se borran.</item>
/// <item>Los datos de cada tarjeta salen con los permisos de quien mira (<see cref="CrudService.AggregateAsync"/>).</item>
/// </list>
/// </summary>
public sealed class DashboardService(IDashboardStore store, SchemaService schemas, CrudService crud, IPlanScope plan)
{
    public const int MaxNameLength = 100;

    public async Task<DashboardList> ListAsync(CancellationToken ct)
    {
        var access = await schemas.AccessAsync(ct);
        var limits = await LimitsAsync(ct);
        var all = await store.ListAsync(ct);
        var unlocked = Unlocked(all, limits);
        var items = access.Dashboards.HasFlag(Security.DataOperation.View)
            ? all.Where(d => CanSee(access, d)).Select(d => View(access, d, unlocked)).ToList()
            : [];
        var canCreate = access.Dashboards.HasFlag(Security.DataOperation.Create);
        return new DashboardList(items, canCreate, all.Count, limits.Dashboards, limits.Widgets);
    }

    public async Task<DashboardView> GetAsync(string id, CancellationToken ct)
    {
        var (access, dashboard, view) = await LoadAsync(id, ct);
        if (view.Locked) throw Locked(await LimitsAsync(ct));
        return view;
    }

    public async Task<DashboardView> CreateAsync(DashboardInput input, CancellationToken ct)
    {
        var access = await schemas.AccessAsync(ct);
        if (!access.Dashboards.HasFlag(Security.DataOperation.Create)) throw new ForbiddenException("No tiene permiso para crear dashboards.");
        var limits = await LimitsAsync(ct);
        var all = await store.ListAsync(ct);
        if (limits.Dashboards is { } max && all.Count >= max)
            throw new PlanLimitException(max == PlanLimits.FreeDashboards
                ? $"El plan Gratis permite {max} dashboard. Pase a Pro para tener hasta {PlanLimits.ProDashboards}."
                : $"El plan Pro permite {max} dashboards. Pase a Empresa para tenerlos sin tope.");

        var now = DateTimeOffset.UtcNow;
        var dashboard = new Dashboard
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = Name(input.Name),
            Description = Description(input.Description),
            Owner = access.Username ?? throw new OperationNotAllowedException("La sesión no tiene un nombre de usuario."),
            Widgets = await WidgetsAsync(input.Widgets, limits, ct),
            Shares = Shares(input.Shares),
            CreatedAt = now, UpdatedAt = now, UpdatedBy = access.Username
        };
        await store.SaveAsync(dashboard, ct);
        return View(access, dashboard, null);
    }

    public async Task<DashboardView> UpdateAsync(string id, DashboardInput input, CancellationToken ct)
    {
        var (access, current, view) = await LoadAsync(id, ct);
        if (view.Locked) throw Locked(await LimitsAsync(ct));
        if (!view.CanEdit) throw new ForbiddenException($"No tiene permiso para editar el dashboard '{current.Name}'.");
        var limits = await LimitsAsync(ct);
        var dashboard = current with
        {
            Name = Name(input.Name),
            Description = Description(input.Description),
            Widgets = await WidgetsAsync(input.Widgets, limits, ct),
            // Compartir es del dueño (y del administrador): quien solo puede editar no cambia con quién se comparte.
            Shares = view.CanShare && input.Shares is not null ? Shares(input.Shares) : current.Shares,
            UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = access.Username
        };
        await store.SaveAsync(dashboard, ct);
        return View(access, dashboard, null);
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        var (_, current, view) = await LoadAsync(id, ct);
        if (!view.CanDelete) throw new ForbiddenException($"No tiene permiso para borrar el dashboard '{current.Name}'.");
        await store.DeleteAsync(id, ct);
    }

    /// <summary>Datos de una tarjeta guardada, con el rango de fechas del dashboard y los permisos de quien mira.</summary>
    public async Task<AggregateResult> WidgetDataAsync(string id, string widgetId, string? from, string? to, CancellationToken ct)
    {
        var view = await GetAsync(id, ct);
        var widget = view.Dashboard.Widgets.FirstOrDefault(w => w.Id == widgetId)
                     ?? throw new NotFoundException($"La tarjeta '{widgetId}' no existe en el dashboard.");
        return await DataAsync(widget, from, to, ct);
    }

    /// <summary>Datos de una tarjeta mientras se edita, sin guardarla: solo para quien puede crear o editar dashboards.</summary>
    public async Task<AggregateResult> PreviewAsync(DashboardWidget widget, string? from, string? to, CancellationToken ct)
    {
        var access = await schemas.AccessAsync(ct);
        if ((access.Dashboards & (Security.DataOperation.Create | Security.DataOperation.Update)) == 0)
            throw new ForbiddenException("No tiene permiso para crear ni editar dashboards.");
        Validate(widget);
        return await DataAsync(widget, from, to, ct);
    }

    private Task<AggregateResult> DataAsync(DashboardWidget widget, string? from, string? to, CancellationToken ct)
    {
        var query = widget.Query.DateColumn is null ? widget.Query : widget.Query with { From = from, To = to };
        return crud.AggregateAsync(widget.Connection, widget.Schema, widget.Table, query, ct);
    }

    private async Task<(UserAccess Access, Dashboard Dashboard, DashboardView View)> LoadAsync(string id, CancellationToken ct)
    {
        var access = await schemas.AccessAsync(ct);
        var dashboard = await store.GetAsync(id, ct);
        // Uno que no puede ver es como si no existiera: no se revela su nombre.
        if (dashboard is null || !access.Dashboards.HasFlag(Security.DataOperation.View) || !CanSee(access, dashboard))
            throw new NotFoundException($"El dashboard '{id}' no existe.");
        var unlocked = Unlocked(await store.ListAsync(ct), await LimitsAsync(ct));
        return (access, dashboard, View(access, dashboard, unlocked));
    }

    private async Task<PlanLimits> LimitsAsync(CancellationToken ct) => PlanLimits.For((await plan.CurrentAsync(ct)).Plan);

    /// <summary>Los que el plan deja usar: los más antiguos hasta el tope. Nulo = todos.</summary>
    private static HashSet<string>? Unlocked(IReadOnlyList<Dashboard> all, PlanLimits limits) =>
        limits.Dashboards is { } max && all.Count > max
            ? all.OrderBy(d => d.CreatedAt).ThenBy(d => d.Id, StringComparer.Ordinal).Take(max).Select(d => d.Id).ToHashSet()
            : null;

    private static bool IsOwner(UserAccess a, Dashboard d) => a.Username is { } u && string.Equals(u, d.Owner, StringComparison.OrdinalIgnoreCase);

    private static bool CanSee(UserAccess a, Dashboard d) =>
        a.IsAdmin || IsOwner(a, d) || d.Shares.Any(s => a.ProfileIds.Contains(s.Profile));

    private static DashboardView View(UserAccess a, Dashboard d, HashSet<string>? unlocked)
    {
        var owner = IsOwner(a, d) || a.IsAdmin;
        var sharedForEdit = d.Shares.Any(s => s.CanEdit && a.ProfileIds.Contains(s.Profile));
        var ops = a.Dashboards;
        return new DashboardView(d,
            CanEdit: ops.HasFlag(Security.DataOperation.Update) && (owner || sharedForEdit),
            CanDelete: ops.HasFlag(Security.DataOperation.Delete) && (owner || sharedForEdit),
            CanShare: owner,
            Locked: unlocked is not null && !unlocked.Contains(d.Id));
    }

    private static PlanLimitException Locked(PlanLimits limits) =>
        new(limits.Plan == Plan.Free
            ? $"El plan Gratis permite {limits.Dashboards} dashboard: este quedó guardado y vuelve al pasar a Pro."
            : $"El plan Pro permite {limits.Dashboards} dashboards: este quedó guardado y vuelve al pasar a Empresa.");

    private static string Name(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > MaxNameLength) throw new ValidationException("name", $"es obligatorio (máx. {MaxNameLength})");
        return n;
    }

    private static string? Description(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null
        : text.Trim().Length > 500 ? throw new ValidationException("description", "máx. 500 caracteres") : text.Trim();

    private static IReadOnlyList<DashboardShare> Shares(IReadOnlyList<DashboardShare>? shares) =>
        (shares ?? []).Where(s => !string.IsNullOrWhiteSpace(s.Profile))
            .GroupBy(s => s.Profile.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new DashboardShare(g.Key, g.Any(s => s.CanEdit))).ToList();

    /// <summary>Valida cada tarjeta contra el esquema que ve quien guarda: tabla, columnas y métrica deben calzar.</summary>
    private async Task<IReadOnlyList<DashboardWidget>> WidgetsAsync(IReadOnlyList<DashboardWidget>? widgets, PlanLimits limits, CancellationToken ct)
    {
        var list = widgets ?? [];
        if (list.Count > limits.Widgets)
            throw new PlanLimitException($"Su plan permite {limits.Widgets} tarjetas por dashboard.");
        var result = new List<DashboardWidget>();
        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i];
            try
            {
                Validate(w);
                var (ctx, table) = await schemas.GetTableAsync(w.Connection, w.Schema, w.Table, ct);
                new QueryBuilder(ctx.Provider.Dialect).BuildAggregate(ctx.LabelSchema, table, w.Query);
            }
            catch (ValidationException e)
            {
                throw new ValidationException(e.Errors.ToDictionary(kv => $"widgets[{i}].{kv.Key}", kv => kv.Value));
            }
            result.Add(w with
            {
                Id = string.IsNullOrWhiteSpace(w.Id) ? Guid.NewGuid().ToString("N")[..12] : w.Id.Trim(),
                Title = (w.Title ?? "").Trim(),
                // Los valores del rango son del dashboard, no de la tarjeta.
                Query = w.Query with { From = null, To = null, Limit = Math.Clamp(w.Query.Limit, 1, AggregateRequest.MaxLimit) }
            });
        }
        if (result.Select(w => w.Id).Distinct().Count() != result.Count)
            throw new ValidationException("widgets", "hay tarjetas con el mismo id");
        return result;
    }

    /// <summary>Cuántas agrupaciones lleva cada tipo de tarjeta.</summary>
    private static void Validate(DashboardWidget w)
    {
        if ((w.Title ?? "").Length > MaxNameLength) throw new ValidationException("title", $"máx. {MaxNameLength} caracteres");
        var groups = w.Query.GroupBy.Count;
        var ok = w.Type switch
        {
            WidgetType.Number => groups == 0,
            WidgetType.Pivot => groups == 2,
            WidgetType.Map => throw new ValidationException("type", "el mapa llega en una próxima versión"),
            _ => groups == 1
        };
        if (!ok)
            throw new ValidationException("groupBy", w.Type switch
            {
                WidgetType.Number => "un número no se agrupa",
                WidgetType.Pivot => "la tabla dinámica necesita dos agrupaciones",
                _ => "esta tarjeta necesita una agrupación"
            });
    }
}
