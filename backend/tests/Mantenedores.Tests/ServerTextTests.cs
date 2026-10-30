using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mantenedores.Api.Infrastructure;
using Mantenedores.Core.Audit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mantenedores.Tests;

/// <summary>Textos que arma el servidor en el idioma de la interfaz (D-102).</summary>
public sealed class ServerTextTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("servertext-tests-").FullName;
    private WebApplicationFactory<Program>? _factory;

    [Theory]
    [InlineData("en-US,en;q=0.9,es;q=0.8", "en")]
    [InlineData("ru", "ru")]
    [InlineData("es-CL", "es")]
    [InlineData("pt-BR", "es")]
    [InlineData("", "es")]
    [InlineData(null, "es")]
    public void Toma_el_primer_idioma_conocido_o_espanol(string? header, string expected) =>
        Assert.Equal(expected, ServerText.Language(header));

    [Fact]
    public void Las_operaciones_se_llaman_como_en_la_pantalla_de_auditoria()
    {
        Assert.Equal("Edición", ServerText.Operation(AuditOperation.Update, "es"));
        Assert.Equal("N:M links", ServerText.Operation(AuditOperation.Links, "en"));
        Assert.Equal("Удаление", ServerText.Operation(AuditOperation.Delete, "ru"));
    }

    [Theory]
    [InlineData("en", "Date (UTC),User,Operation", "audit-")]
    [InlineData("ru", "Дата (UTC),Пользователь,Операция", "audit-")]
    [InlineData("es", "Fecha (UTC),Usuario,Operación", "auditoria-")]
    public async Task La_exportacion_de_la_auditoria_usa_el_idioma_de_la_interfaz(string language, string header, string file)
    {
        _factory ??= new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("Storage:DataDirectory", _data);
            b.UseSetting("RateLimit:Enabled", "false");
        });
        var c = _factory.CreateClient();
        var login = await c.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "admin" });
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);

        var res = await c.GetAsync("/api/audit/export?format=csv");
        res.EnsureSuccessStatusCode();
        Assert.StartsWith(header, (await res.Content.ReadAsStringAsync()).TrimStart('﻿'));
        Assert.StartsWith(file, res.Content.Headers.ContentDisposition?.FileNameStar);
    }

    public void Dispose()
    {
        _factory?.Dispose();
        try { Directory.Delete(_data, recursive: true); } catch (IOException) { }
    }
}
