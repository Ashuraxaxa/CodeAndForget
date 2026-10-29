using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Mantenedores.Api.Infrastructure;

public static class HostingSetup
{
    public static void ConfigureForwardedHeaders(this IServiceCollection services, HostingOptions hosting)
    {
        var f = hosting.ForwardedHeaders;
        if (!f.Enabled) return;

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            if (f.TrustAllProxies)
            {
                o.KnownIPNetworks.Clear();
                o.KnownProxies.Clear();
                return;
            }
            foreach (var ip in f.KnownProxies)
                o.KnownProxies.Add(IPAddress.Parse(ip));
            foreach (var cidr in f.KnownNetworks)
                o.KnownIPNetworks.Add(ParseNetwork(cidr));
        });
    }

    /// <summary>
    /// "10.0.0.0/8" → red. También acepta una IP con bits de host ("192.168.1.5/24" → 192.168.1.0/24), como antes de
    /// .NET 10: <see cref="System.Net.IPNetwork.Parse(string)"/> los rechaza.
    /// </summary>
    public static System.Net.IPNetwork ParseNetwork(string cidr)
    {
        var parts = cidr.Trim().Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || !int.TryParse(parts[1], out var prefix))
            throw new FormatException($"Red inválida en Hosting:ForwardedHeaders:KnownNetworks: '{cidr}' (use p. ej. 10.0.0.0/8).");
        var bytes = address.GetAddressBytes();
        if (prefix < 0 || prefix > bytes.Length * 8)
            throw new FormatException($"Prefijo inválido en Hosting:ForwardedHeaders:KnownNetworks: '{cidr}'.");
        for (var i = 0; i < bytes.Length; i++)
        {
            var keep = Math.Clamp(prefix - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - keep));
        }
        return new System.Net.IPNetwork(new IPAddress(bytes), prefix);
    }

    /// <summary>Va al principio del pipeline: primero los encabezados del proxy, luego la subruta.</summary>
    public static void UseHosting(this WebApplication app, HostingOptions hosting)
    {
        if (hosting.ForwardedHeaders.Enabled) app.UseForwardedHeaders();
        if (hosting.NormalizedPathBase.Length > 0) app.UsePathBase(hosting.NormalizedPathBase);

        // Obligatorio después de UsePathBase: si no, el enrutamiento implícito de WebApplication corre antes,
        // compara la ruta completa ("/mantenedores/api/...") y todo cae en el index.html.
        app.UseRouting();
    }
}
