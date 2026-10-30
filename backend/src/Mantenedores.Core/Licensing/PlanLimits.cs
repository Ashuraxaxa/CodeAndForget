namespace Mantenedores.Core.Licensing;

/// <summary>Funciones que dependen del plan (D-104). Las de Gratis no se nombran: están siempre.</summary>
public enum Feature
{
    /// <summary>Perfiles y permisos por tabla, columna y acción (crear y editar perfiles).</summary>
    Profiles,
    /// <summary>Exportar e importar en Excel, JSON y XML (CSV está en Gratis).</summary>
    AllFormats,
    /// <summary>Importar actualizando por clave.</summary>
    ImportUpsert,
    /// <summary>Avisos por correo de tablas críticas.</summary>
    Notifications,
    /// <summary>Botones que ejecutan procedimientos almacenados.</summary>
    Actions,
    /// <summary>Copiar ajustes entre conexiones.</summary>
    CopySettings,
    /// <summary>IA por MCP y tokens de acceso.</summary>
    Ai,
    /// <summary>Inicio de sesión con proveedor de identidad (modo Jwt / OIDC).</summary>
    Sso,
    /// <summary>Varias instancias, metadata en PostgreSQL o SQL Server, Redis, plugins y OpenTelemetry.</summary>
    Infrastructure
}

/// <summary>Topes de un plan. Nulo = sin tope.</summary>
public sealed record PlanLimits(Plan Plan, int? Users, int? Connections, int? Tables, int? AuditDays)
{
    public const int FreeUsers = 2;
    public const int FreeConnections = 1;
    public const int FreeTables = 15;
    public const int FreeAuditDays = 7;
    public const int ProUsers = 25;
    public const int ProAuditDays = 365;

    public static PlanLimits For(Plan plan, int? licensedUsers = null) => plan switch
    {
        Plan.Free => new(plan, FreeUsers, FreeConnections, FreeTables, FreeAuditDays),
        Plan.Pro => new(plan, licensedUsers is > 0 ? licensedUsers : ProUsers, null, null, ProAuditDays),
        _ => new(plan, licensedUsers is > 0 ? licensedUsers : null, null, null, null)
    };

    /// <summary>Plan mínimo de cada función.</summary>
    public static Plan Requires(Feature feature) => feature switch
    {
        Feature.Sso or Feature.Infrastructure => Plan.Business,
        _ => Plan.Pro
    };

    public bool Has(Feature feature) => Plan >= Requires(feature);
}
