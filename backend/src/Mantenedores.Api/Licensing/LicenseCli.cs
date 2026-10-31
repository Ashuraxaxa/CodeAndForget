using System.Globalization;
using System.Security.Cryptography;
using Mantenedores.Core.Licensing;

namespace Mantenedores.Api.Licensing;

/// <summary>
/// Herramientas del dueño para las licencias (P-116, D-106), sin levantar el servidor:
/// <code>
/// dotnet Mantenedores.Api.dll license keygen &lt;carpeta&gt;
/// dotnet Mantenedores.Api.dll license issue --key privada.pem --org "Empresa SAC" --plan pro|business [--users 25] [--days 365] [--email a@b.com]
/// dotnet Mantenedores.Api.dll license show &lt;clave&gt;
/// </code>
/// La llave privada nunca va al repositorio: la pública se copia a <see cref="TrustedLicenseKeys"/>.
/// </summary>
public static class LicenseCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            return args switch
            {
                ["keygen", var dir] => KeyGen(dir, output),
                ["issue", .. var rest] => Issue(rest, output),
                ["show", var key] => Show(key, output),
                _ => Usage(error)
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or CryptographicException or Core.Errors.ValidationException)
        {
            error.WriteLine(ex is Core.Errors.ValidationException v ? string.Join(" ", v.Errors.Values) : ex.Message);
            return 1;
        }
    }

    private static int Usage(TextWriter error)
    {
        error.WriteLine("Uso: license keygen <carpeta> | license issue --key <privada.pem> --org <nombre> --plan pro|business [--users N] [--days N] [--email correo] | license show <clave>");
        return 2;
    }

    private static int KeyGen(string dir, TextWriter output)
    {
        Directory.CreateDirectory(dir);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePath = Path.Combine(dir, "celdo-licencias-privada.pem");
        if (File.Exists(privatePath)) throw new IOException($"{privatePath} ya existe: no se sobrescribe una llave.");
        File.WriteAllText(privatePath, key.ExportPkcs8PrivateKeyPem());
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(privatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var publicPem = key.ExportSubjectPublicKeyInfoPem();
        File.WriteAllText(Path.Combine(dir, "celdo-licencias-publica.pem"), publicPem);
        output.WriteLine($"Llave privada: {privatePath} (guárdela fuera del repositorio; quien la tenga emite licencias).");
        output.WriteLine("Llave pública, para TrustedLicenseKeys:");
        output.WriteLine(publicPem);
        return 0;
    }

    private static int Issue(string[] args, TextWriter output)
    {
        var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Opción inesperada: {args[i]}");
            o[args[i][2..]] = args[i + 1];
        }
        string Need(string name) => o.TryGetValue(name, out var v) && v.Trim().Length > 0 ? v.Trim() : throw new ArgumentException($"Falta --{name}.");
        var plan = Need("plan").ToLowerInvariant() switch
        {
            "pro" => Plan.Pro,
            "business" or "empresa" => Plan.Business,
            var p => throw new ArgumentException($"Plan inválido: {p} (use pro o business).")
        };
        int? Int(string name) => o.TryGetValue(name, out var v)
            ? int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : throw new ArgumentException($"--{name} debe ser un número mayor que 0.")
            : null;

        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(Need("key")));
        var now = DateTimeOffset.UtcNow;
        var days = Int("days");
        var info = new LicenseInfo
        {
            Id = Guid.NewGuid().ToString("N")[..12], Organization = Need("org"), Plan = plan, Users = Int("users"),
            Email = o.GetValueOrDefault("email"), IssuedAt = now, ExpiresAt = days is { } d ? now.AddDays(d) : null
        };
        output.WriteLine(LicenseKey.Issue(info, key));
        return 0;
    }

    private static int Show(string key, TextWriter output)
    {
        var info = LicenseKey.Verify(key, TrustedLicenseKeys.All);
        output.WriteLine($"Organización: {info.Organization}");
        output.WriteLine($"Plan: {LicenseService.PlanName(info.Plan)}");
        output.WriteLine($"Usuarios: {(info.Users?.ToString(CultureInfo.InvariantCulture) ?? "los del plan")}");
        output.WriteLine($"Emitida: {info.IssuedAt:yyyy-MM-dd} · Vence: {(info.ExpiresAt is { } e ? e.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "no vence")}");
        output.WriteLine($"Id: {info.Id}");
        return 0;
    }
}
