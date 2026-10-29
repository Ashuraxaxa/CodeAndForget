using System.Security.Cryptography.X509Certificates;
using Mantenedores.Core.Audit;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Schema;
using Mantenedores.Core.Security;
using Mantenedores.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Mantenedores.Metadata;

/// <summary>Registra el almacén de conexiones, el de ajustes (P-011) y las llaves de cifrado según la configuración (D-023, D-025).</summary>
public static class MetadataSetup
{
    public const string ApplicationName = "Mantenedores";

    public static void AddMetadataStore(this IServiceCollection services, IConfiguration configuration, string dataDirectory)
    {
        var options = configuration.GetSection(MetadataOptions.Section).Get<MetadataOptions>() ?? new MetadataOptions();
        var protection = configuration.GetSection(DataProtectionSettings.Section).Get<DataProtectionSettings>() ?? new DataProtectionSettings();

        if (protection.KeyStore == KeyStore.Database && options.Provider == MetadataProvider.File)
            throw new InvalidOperationException(
                "DataProtection:KeyStore=Database requiere una base de metadata: configure Metadata:Provider (Sqlite, Postgres o SqlServer).");

        // --- Llaves de cifrado ---
        var dp = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (protection.KeyStore == KeyStore.FileSystem)
            dp.PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
        if (protection.CertificatePath.Length > 0)
            dp.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(protection.CertificatePath, protection.CertificatePassword));

        // --- Almacén de conexiones ---
        if (options.Provider == MetadataProvider.File)
        {
            services.AddSingleton<IConnectionStore>(sp => new FileConnectionStore(
                Path.Combine(dataDirectory, "connections.json"), sp.GetRequiredService<IDataProtectionProvider>()));
            services.AddSingleton<IOverrideStore>(new FileOverrideStore(Path.Combine(dataDirectory, "overrides.json")));
            services.AddSingleton<ISchemaSnapshotStore>(new FileSnapshotStore(Path.Combine(dataDirectory, "snapshots")));
            services.AddSingleton<ISecurityStore>(new FileSecurityStore(Path.Combine(dataDirectory, "security.json")));
            services.AddSingleton<IAuditStore>(new FileAuditStore(Path.Combine(dataDirectory, "audit.jsonl")));
            return;
        }

        services.AddSingleton(new MetadataDatabase(options, dataDirectory));
        services.AddSingleton<SqlConnectionStore>();
        services.AddSingleton<IConnectionStore>(sp => sp.GetRequiredService<SqlConnectionStore>());
        services.AddSingleton<IOverrideStore, SqlOverrideStore>();
        services.AddSingleton<ISchemaSnapshotStore, SqlSnapshotStore>();
        services.AddSingleton<ISecurityStore, SqlSecurityStore>();
        services.AddSingleton<IAuditStore, SqlAuditStore>();
        // Con metadata compartida, las instancias se avisan los cambios de ajustes, esquema y seguridad (D-077).
        services.AddSingleton<IChangeStampStore, SqlChangeStampStore>();

        if (protection.KeyStore == KeyStore.Database)
            services.AddOptions<KeyManagementOptions>()
                .Configure<MetadataDatabase>((o, db) => o.XmlRepository = new DatabaseXmlRepository(db));
    }

    /// <summary>Al arrancar: aplica las migraciones e importa el connections.json de la Fase 1 si corresponde.</summary>
    public static async Task InitializeMetadataAsync(this IServiceProvider services, string dataDirectory)
    {
        if (services.GetService<MetadataDatabase>() is not { } db) return;
        await new MetadataMigrator(db).MigrateAsync();
        await LegacyConnectionImporter.ImportAsync(dataDirectory, services.GetRequiredService<SqlConnectionStore>(), ApplicationName);
    }
}
