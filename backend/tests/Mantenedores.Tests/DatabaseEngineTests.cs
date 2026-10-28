using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mantenedores.Core.Providers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>El motor como nombre abierto (P-064, D-092): compatible con lo guardado y validado contra lo instalado.</summary>
public sealed class DatabaseEngineTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("engine-tests-").FullName;

    private sealed record Holder(DatabaseEngine Engine);

    [Fact]
    public void Se_compara_sin_mayusculas_y_viaja_como_texto()
    {
        Assert.Equal(DatabaseEngine.Postgres, new DatabaseEngine("postgres"));
        Assert.Equal(DatabaseEngine.Postgres.GetHashCode(), new DatabaseEngine("POSTGRES").GetHashCode());
        Assert.Equal("{\"Engine\":\"MySql\"}", JsonSerializer.Serialize(new Holder(DatabaseEngine.MySql)));
        Assert.Equal(new DatabaseEngine("Sqlite"), JsonSerializer.Deserialize<Holder>("{\"Engine\":\"Sqlite\"}")!.Engine);
        Assert.Throws<ArgumentException>(() => new DatabaseEngine("no vale"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("{\"Engine\":\"a;b\"}"));
    }

    [Theory]
    [InlineData(0, "SqlServer")]
    [InlineData(1, "Postgres")]
    [InlineData(2, "MySql")]
    public void Lee_el_numero_que_guardaba_el_enum(int legacy, string name) =>
        Assert.Equal(new DatabaseEngine(name), JsonSerializer.Deserialize<Holder>($"{{\"Engine\":{legacy}}}")!.Engine);

    private async Task<HttpClient> AdminAsync()
    {
        var c = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
        }).CreateClient();
        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    [Fact]
    public async Task La_api_lista_los_motores_instalados_y_rechaza_uno_que_no_esta()
    {
        var c = await AdminAsync();
        var engines = await c.GetFromJsonAsync<JsonElement>("/api/engines");
        Assert.Equal(["SqlServer", "Postgres", "MySql"], engines.EnumerateArray().Select(e => e.GetProperty("name").GetString()));
        Assert.Equal("MySQL / MariaDB", engines[2].GetProperty("label").GetString());

        var res = await c.PutAsJsonAsync("/api/connections/x", new { id = "x", name = "X", engine = "Oracle", connectionString = "Data Source=o" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("el motor Oracle no está instalado", await res.Content.ReadAsStringAsync());

        var ok = await c.PutAsJsonAsync("/api/connections/x", new { id = "x", name = "X", engine = "postgres", connectionString = "Host=h" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Postgres", (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("engine").GetString());
    }

    public void Dispose()
    {
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
