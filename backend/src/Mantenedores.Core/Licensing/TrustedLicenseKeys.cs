using System.Security.Cryptography;

namespace Mantenedores.Core.Licensing;

/// <summary>
/// Llaves públicas con que se validan las licencias (D-106). La privada la guarda el dueño fuera del repositorio:
/// con ella se emiten licencias (<c>dotnet Mantenedores.Api.dll license issue …</c>). Para cambiarla, se agrega la
/// nueva pública aquí (las licencias firmadas con la anterior siguen sirviendo mientras su llave siga en la lista).
/// </summary>
public static class TrustedLicenseKeys
{
    private const string Celdo2026 = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEPA/N9cAd92m4ysxZlnU5YJVluF0x
        mTtz3clQPCJT2rWAzwZI7TvhubReqzugQ8Mah1GAYYWsytz9Q24cn7QCBg==
        -----END PUBLIC KEY-----
        """;

    private static readonly List<ECDsa> Keys = [Load(Celdo2026)];

    public static IReadOnlyList<ECDsa> All
    {
        get { lock (Keys) return [.. Keys]; }
    }

    /// <summary>Solo para pruebas: agrega una llave de prueba (no hay forma de hacerlo desde la configuración).</summary>
    internal static void AddForTests(ECDsa key)
    {
        lock (Keys) Keys.Add(key);
    }

    private static ECDsa Load(string pem)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(pem);
        return key;
    }
}
