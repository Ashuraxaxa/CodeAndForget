using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mantenedores.Core.Providers;
using Mantenedores.Providers.MySql;
using Mantenedores.Providers.Postgres;
using Mantenedores.Providers.SqlServer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Tope del pool por conexión y límites de uso (P-051, P-091, D-070).</summary>
public sealed class PoolAndRateLimitTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("limits-tests-").FullName;

    private static DbConnectionStringBuilder Parse(string cs) => new() { ConnectionString = cs };

    [Fact]
    public void Cada_motor_agrega_el_tope_con_sus_nombres_y_respeta_lo_que_trae_la_cadena()
    {
        var pool = new PoolOptions { MaxSize = 7, IdleSeconds = 60 };

        var pg = Parse(new PostgresProvider().ApplyPool("Host=h;Password=\"a;b\"", pool));
        Assert.Equal("7", pg["Maximum Pool Size"].ToString());
        Assert.Equal("60", pg["Connection Idle Lifetime"].ToString());
        Assert.Equal("a;b", pg["Password"].ToString()); // las comillas de valores con ; se conservan

        var sql = Parse(new SqlServerProvider().ApplyPool("Server=s;Max Pool Size=50", pool));
        Assert.Equal("50", sql["Max Pool Size"].ToString()); // el del usuario manda
        Assert.False(sql.ContainsKey("Connection Idle Lifetime"));

        var my = Parse(new MySqlProvider().ApplyPool("Server=m;MaxPoolSize=3", pool));
        Assert.Equal("3", my["MaxPoolSize"].ToString());
        Assert.False(my.ContainsKey("Maximum Pool Size"));
        Assert.Equal("60", my["Connection Idle Timeout"].ToString());

        var off = Parse(new PostgresProvider().ApplyPool("Host=h", new PoolOptions { MaxSize = 0, IdleSeconds = 0 }));
        Assert.False(off.ContainsKey("Maximum Pool Size"));
    }

    [Fact]
    public void Los_proveedores_aceptan_la_cadena_resultante()
    {
        var pool = new PoolOptions();
        _ = new Npgsql.NpgsqlConnectionStringBuilder(new PostgresProvider().ApplyPool("Host=h;Username=u", pool));
        _ = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(new SqlServerProvider().ApplyPool("Server=s", pool));
        _ = new MySqlConnector.MySqlConnectionStringBuilder(new MySqlProvider().ApplyPool("Server=m", pool));
    }

    private HttpClient Client(params (string Key, string Value)[] settings)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
            foreach (var (k, v) in settings) b.UseSetting(k, v);
        });
        return factory.CreateClient();
    }

    [Fact]
    public async Task El_login_se_limita_por_ip()
    {
        var c = Client(("RateLimit:LoginPerMinute", "3"));
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codes.Add((await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "mala" })).StatusCode);
        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], codes);

        var res = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode); // también la clave buena: el límite es por IP
        Assert.True(res.Headers.Contains("Retry-After"));
        Assert.Contains("Límite de uso", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task La_api_se_limita_por_usuario_y_los_health_checks_no_cuentan()
    {
        var c = Client(("RateLimit:PermitsPerMinute", "3"));
        var token = (await (await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" }))
            .Content.ReadFromJsonAsync<LoginDto>())!.Token; // 1
        async Task<HttpStatusCode> Me()
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return (await c.SendAsync(req)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.OK, await Me());
        Assert.Equal(HttpStatusCode.OK, await Me());
        Assert.Equal(HttpStatusCode.OK, await Me()); // el login fue con la IP: el usuario tiene su propia cuota
        Assert.Equal(HttpStatusCode.TooManyRequests, await Me());
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/health/live")).StatusCode);
    }

    [Fact]
    public async Task Se_puede_apagar()
    {
        var c = Client(("RateLimit:Enabled", "false"), ("RateLimit:LoginPerMinute", "1"));
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "mala" })).StatusCode);
    }

    private sealed record LoginDto(string Token);

    public void Dispose()
    {
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
