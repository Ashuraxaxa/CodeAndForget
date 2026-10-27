using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Mantenedores.Metadata;

/// <summary>
/// Llaves de Data Protection en la base de metadata (DataProtection:KeyStore = Database, D-025):
/// varias instancias de la app comparten las llaves y pueden leer las mismas conexiones.
/// </summary>
public sealed class DatabaseXmlRepository(MetadataDatabase db) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var conn = db.CreateConnection();
        conn.Open();
        return conn.Query<string>("SELECT xml FROM mnt_dataprotection_key").Select(XElement.Parse).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var conn = db.CreateConnection();
        conn.Open();
        conn.Execute("INSERT INTO mnt_dataprotection_key (friendly_name, xml) VALUES (@name, @xml)",
            new { name = string.IsNullOrEmpty(friendlyName) ? Guid.NewGuid().ToString() : friendlyName, xml = element.ToString(SaveOptions.DisableFormatting) });
    }
}
