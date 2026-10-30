using Mantenedores.Core.Audit;

namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// Los pocos textos que arma el servidor sin pasar por la pantalla (D-102): encabezados y operaciones de la exportación de
/// la auditoría, y el correo de avisos. Los mensajes de error, en cambio, los traduce el frontend (D-080).
/// Idiomas: es, en y ru, como la interfaz; cualquier otro, español.
/// </summary>
public static class ServerText
{
    public static readonly IReadOnlyList<string> Languages = ["es", "en", "ru"];

    /// <summary>"en-US,en;q=0.9" → "en". Un código suelto ("ru") también sirve. Sin idioma conocido, "es".</summary>
    public static string Language(string? acceptLanguageOrCode)
    {
        var first = (acceptLanguageOrCode ?? "").Split(',')[0].Split(';')[0].Trim();
        var code = first.Split('-')[0].ToLowerInvariant();
        return Languages.Contains(code) ? code : "es";
    }

    public static string Pick(string language, string es, string en, string ru) => language switch
    {
        "en" => en,
        "ru" => ru,
        _ => es
    };

    /// <summary>Nombre de la operación como lo muestra la pantalla de auditoría.</summary>
    public static string Operation(AuditOperation op, string language) => op switch
    {
        AuditOperation.Create => Pick(language, "Alta", "Create", "Создание"),
        AuditOperation.Update => Pick(language, "Edición", "Update", "Изменение"),
        AuditOperation.Delete => Pick(language, "Baja", "Delete", "Удаление"),
        AuditOperation.Import => Pick(language, "Importación", "Import", "Импорт"),
        AuditOperation.Links => Pick(language, "Vínculos N:M", "N:M links", "Связи N:M"),
        _ => Pick(language, "Acción", "Action", "Действие")
    };
}
