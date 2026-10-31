using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Mantenedores.Core.Licensing;

namespace Mantenedores.IntegrationTests;

/// <summary>
/// Licencia de las pruebas (P-116): una llave que solo existe aquí y una licencia Empresa por la variable
/// <c>License__Key</c>, así las pruebas de siempre corren sin los topes del plan Gratis. Las pruebas de planes la
/// cambian con <c>UseSetting("License:Key", …)</c>.
/// </summary>
internal static class TestLicense
{
    public static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string Issue(Plan plan, int? users = null, DateTimeOffset? expires = null, string org = "Pruebas SAC") =>
        LicenseKey.Issue(new LicenseInfo
        {
            Id = Guid.NewGuid().ToString("N")[..12], Organization = org, Plan = plan, Users = users,
            IssuedAt = DateTimeOffset.UtcNow.AddDays(-1), ExpiresAt = expires
        }, Key);

    [ModuleInitializer]
    internal static void Init()
    {
        TrustedLicenseKeys.AddForTests(Key);
        Environment.SetEnvironmentVariable("License__Key", Issue(Plan.Business));
    }
}
