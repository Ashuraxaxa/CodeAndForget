using Mantenedores.Core.Audit;

namespace Mantenedores.Core.Notifications;

/// <summary>Avisos por correo de una tabla (P-099, D-073), ya validados. Null en la tabla = no avisa.</summary>
public sealed record TableNotify
{
    public required IReadOnlyList<string> Emails { get; init; }
    public required IReadOnlySet<AuditOperation> On { get; init; }

    /// <summary>Incluir los valores antes y después en el correo. Sin ellos, solo quién, cuándo y qué registro.</summary>
    public bool IncludeValues { get; init; } = true;
}

/// <summary>Cambios ya confirmados de una tabla que tiene avisos, para enviar en segundo plano.</summary>
public sealed record ChangeNotice(
    string Connection, string Schema, string Table, string? TableLabel, TableNotify Notify, IReadOnlyList<AuditEntry> Entries);

/// <summary>
/// Envía los avisos. Se llama después de confirmar el cambio y no debe demorar la respuesta: la implementación encola.
/// </summary>
public interface IChangeNotifier
{
    /// <summary>¿Hay un canal configurado (SMTP)? Sin él los avisos de las tablas no se envían.</summary>
    bool Enabled { get; }

    void Enqueue(ChangeNotice notice);
}

public sealed class NoChangeNotifier : IChangeNotifier
{
    public bool Enabled => false;
    public void Enqueue(ChangeNotice notice) { }
}
