namespace Mantenedores.Metadata;

/// <summary>Dónde guarda el servicio su propia configuración (D-023).</summary>
public enum MetadataProvider
{
    /// <summary>Archivo JSON (Fase 1). Solo para desarrollo o pruebas.</summary>
    File,

    /// <summary>Por defecto: un archivo SQLite en la carpeta de datos, sin instalar nada.</summary>
    Sqlite,

    Postgres,
    SqlServer
}

public sealed class MetadataOptions
{
    public const string Section = "Metadata";

    public MetadataProvider Provider { get; set; } = MetadataProvider.Sqlite;

    /// <summary>Cadena de conexión. Obligatoria en Postgres y SqlServer; en Sqlite, vacío = data/mantenedores.db.</summary>
    public string ConnectionString { get; set; } = "";
}

/// <summary>Dónde se guardan las llaves que cifran las cadenas de conexión (D-025).</summary>
public enum KeyStore
{
    /// <summary>Por defecto: carpeta data/keys.</summary>
    FileSystem,

    /// <summary>En la base de metadata: varias instancias comparten las llaves sin un volumen común.</summary>
    Database
}

public sealed class DataProtectionSettings
{
    public const string Section = "DataProtection";

    public KeyStore KeyStore { get; set; } = KeyStore.FileSystem;

    /// <summary>Certificado (.pfx) para cifrar las llaves en reposo. Opcional.</summary>
    public string CertificatePath { get; set; } = "";

    public string CertificatePassword { get; set; } = "";
}
