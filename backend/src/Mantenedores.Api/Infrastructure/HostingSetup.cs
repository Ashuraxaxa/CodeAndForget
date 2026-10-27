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
                o.KnownNetworks.Clear();
                o.KnownProxies.Clear();
                return;
            }
            foreach (var ip in f.KnownProxies)
                o.KnownProxies.Add(IPAddress.Parse(ip));
            foreach (var cidr in f.KnownNetworks)
            {
                var parts = cidr.Split('/');
                o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse(parts[0]), int.Parse(parts[1])));
            }
        });
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
