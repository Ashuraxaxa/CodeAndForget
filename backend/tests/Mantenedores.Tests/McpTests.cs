using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;

namespace Mantenedores.Tests;

/// <summary>Servidor MCP (P-114, D-101): apagado por defecto, con los permisos de quien llama y sin guardar ajustes.</summary>
public sealed class McpTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("mcp-tests-").FullName;
    private readonly List<WebApplicationFactory<Program>> _factories = [];

    private HttpClient Client(bool enabled = true)
    {
        var f = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
            b.UseSetting("RateLimit:Enabled", "false");
            b.UseSetting("Mcp:Enabled", enabled ? "true" : "false");
        });
        _factories.Add(f);
        return f.CreateClient();
    }

    private static async Task<HttpClient> As(HttpClient c, string user, string password)
    {
        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = user, password });
        login.EnsureSuccessStatusCode();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        return c;
    }

    /// <summary>Una llamada JSON-RPC como la hace un cliente MCP por HTTP; devuelve el código y el mensaje de respuesta.</summary>
    private static async Task<(HttpStatusCode Status, JsonElement Body)> Rpc(HttpClient c, string method, object? @params = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params }), Encoding.UTF8, "application/json")
        };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        var res = await c.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        // Respuesta como evento SSE: "event: message\ndata: {...}".
        var json = text.Split('\n').FirstOrDefault(l => l.StartsWith("data: "))?["data: ".Length..] ?? (text.StartsWith('{') ? text : "null");
        return (res.StatusCode, JsonDocument.Parse(json).RootElement.Clone());
    }

    private static Task<(HttpStatusCode, JsonElement)> Call(HttpClient c, string tool, object args) =>
        Rpc(c, "tools/call", new { name = tool, arguments = args });

    [Fact]
    public async Task Apagado_por_defecto_no_hay_ruta()
    {
        var admin = await As(Client(enabled: false), "admin", "admin");
        Assert.Equal(HttpStatusCode.NotFound, (await Rpc(admin, "tools/list")).Status);
    }

    [Fact]
    public async Task Con_un_token_lista_las_herramientas_y_las_conexiones()
    {
        var admin = await As(Client(), "admin", "admin");
        var created = await (await admin.PostAsJsonAsync("/api/tokens", new { name = "Claude" })).Content.ReadFromJsonAsync<JsonElement>();
        var ia = Client();
        ia.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.GetProperty("secret").GetString());

        var (status, init) = await Rpc(ia, "initialize", new
        {
            protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "pruebas", version = "1" }
        });
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("mantenedores", init.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.Contains("propose_settings", init.GetProperty("result").GetProperty("instructions").GetString());

        var tools = (await Rpc(ia, "tools/list")).Body.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).Order().ToList();
        Assert.Equal(["get_table", "list_connections", "list_proposals", "list_tables", "preview_settings", "propose_settings",
            "sample_rows", "suggest_settings"], tools);

        var (_, list) = await Call(ia, "list_connections", new { });
        var result = list.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
        Assert.StartsWith("[", result.GetProperty("content")[0].GetProperty("text").GetString());

        // Un error del servicio llega a la IA con su mensaje, para que corrija.
        var (_, missing) = await Call(ia, "list_proposals", new { connection = "no-existe" });
        Assert.True(missing.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Contains("no-existe", missing.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Funciona_con_el_cliente_oficial_de_MCP()
    {
        var admin = await As(Client(), "admin", "admin");
        var created = await (await admin.PostAsJsonAsync("/api/tokens", new { name = "SDK" })).Content.ReadFromJsonAsync<JsonElement>();
        var http = Client();
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/api/mcp"), TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + created.GetProperty("secret").GetString() }
        }, http);
        await using var mcp = await McpClient.CreateAsync(transport);

        Assert.Equal("mantenedores", mcp.ServerInfo.Name);
        var tools = await mcp.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "propose_settings");
        // Lo que solo lee se anuncia así, para que el cliente no pida confirmación de más.
        Assert.True(tools.Single(t => t.Name == "get_table").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.False(tools.Single(t => t.Name == "propose_settings").ProtocolTool.Annotations?.ReadOnlyHint ?? false);

        var list = await mcp.CallToolAsync("list_connections", new Dictionary<string, object?>());
        Assert.NotEqual(true, list.IsError);
        var error = await mcp.CallToolAsync("get_table", new Dictionary<string, object?> { ["connection"] = "no-existe", ["schema"] = "s", ["table"] = "t" });
        Assert.True(error.IsError);
    }

    [Fact]
    public async Task Sin_sesion_o_sin_permiso_de_configurar_no_entra()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Rpc(Client(), "tools/list")).Status);

        var admin = await As(Client(), "admin", "admin");
        await admin.PutAsJsonAsync("/api/security/users/lucas", new { password = "clave-de-lucas" });
        var lucas = await As(Client(), "lucas", "clave-de-lucas");
        Assert.Equal(HttpStatusCode.Forbidden, (await Rpc(lucas, "tools/list")).Status);
        // Su token tiene el mismo acceso que él: tampoco entra.
        var created = await (await lucas.PostAsJsonAsync("/api/tokens", new { name = "IA de Lucas" })).Content.ReadFromJsonAsync<JsonElement>();
        var ia = Client();
        ia.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.GetProperty("secret").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await Rpc(ia, "tools/list")).Status);
    }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
