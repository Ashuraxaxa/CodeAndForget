using System.Security.Cryptography.X509Certificates;
using Mantenedores.Core.Connections;
using Mantenedores.Core.Overrides;
using Mantenedores.Core.Schema;
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
            dp.ProtectKeysWithCertificate(new X509Certificate2(protection.CertificatePath, protection.CertificatePassword));

        // --- Almacén de conexiones ---
        if (options.Provider == MetadataProvider.File)
        {
            services.AddSingleton<IConnectionStore>(sp => new FileConnectionStore(
                Path.Combine(dataDirectory, "connections.json"), sp.GetRequiredService<IDataProtectionProvider>()));
            services.AddSingleton<IOverrideStore>(new FileOverrideStore(Path.Combine(dataDirectory, "overrides.json")));
            services.AddSingleton<ISchemaSnapshotStore>(new FileSnapshotStore(Path.Combine(dataDirectory, "snapshots")));
            return;
        }

        services.AddSingleton(new MetadataDatabase(options, dataDirectory));
        services.AddSingleton<SqlConnectionStore>();
        services.AddSingleton<IConnectionStore>(sp => sp.GetRequiredService<SqlConnectionStore>());
        services.AddSingleton<IOverrideStore, SqlOverrideStore>();
        services.AddSingleton<ISchemaSnapshotStore, SqlSnapshotStore>();

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
