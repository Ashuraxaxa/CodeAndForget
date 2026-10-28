// Host de prueba de Duende IdentityServer para P-078: un cliente SPA con PKCE, un ApiResource y dos usuarios.
using System.Net;
using System.Security.Claims;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Test;
using Microsoft.AspNetCore.Authentication;

const string App = "http://localhost:8094";
var b = WebApplication.CreateBuilder(args);
b.Services.AddIdentityServer(o =>
    {
        o.UserInteraction.LoginUrl = "/login";
        o.KeyManagement.Enabled = false;
        o.EmitStaticAudienceClaim = false;
        o.Authentication.CookieSameSiteMode = SameSiteMode.Lax; // http en la prueba: None exige Secure
    })
    .AddDeveloperSigningCredential(persistKey: false)
    .AddInMemoryIdentityResources([new IdentityResources.OpenId(), new IdentityResources.Profile()])
    .AddInMemoryApiScopes([new ApiScope("mantenedores", ["role"])])
    .AddInMemoryApiResources([new ApiResource("mantenedores-api") { Scopes = { "mantenedores" }, UserClaims = { "role", "name" } }])
    .AddInMemoryClients([new Client
    {
        ClientId = "mantenedores-spa",
        RequireClientSecret = false,
        RequirePkce = true,
        AllowedGrantTypes = GrantTypes.Code,
        AllowedScopes = { "openid", "profile", "mantenedores" },
        RedirectUris = { $"{App}/auth/callback", $"{App}/silent-renew.html" },
        PostLogoutRedirectUris = { $"{App}/" },
        AllowedCorsOrigins = { App },
        AccessTokenLifetime = int.Parse(Environment.GetEnvironmentVariable("TOKEN_SECONDS") ?? "3600"),
        AllowOfflineAccess = false
    }])
    .AddTestUsers([
        new TestUser { SubjectId = "1", Username = "ana", Password = "ana",
            Claims = [new Claim("name", "ana"), new Claim("role", "Administradores")] },
        new TestUser { SubjectId = "2", Username = "luis", Password = "luis",
            Claims = [new Claim("name", "luis"), new Claim("role", "Lectores")] }
    ]);

var app = b.Build();
app.UseIdentityServer();

app.MapGet("/login", (string? returnUrl) => Results.Content($"""
    <html><body><h1>Duende de prueba</h1><form method="post" action="/login">
    <input type="hidden" name="returnUrl" value="{WebUtility.HtmlEncode(returnUrl)}">
    <input id="username" name="username"><input id="password" name="password" type="password">
    <button id="login">Entrar</button></form></body></html>
    """, "text/html"));

app.MapPost("/login", async (HttpContext http, TestUserStore users, IIdentityServerInteractionService interaction) =>
{
    var form = await http.Request.ReadFormAsync();
    var returnUrl = form["returnUrl"].ToString();
    if (!users.ValidateCredentials(form["username"], form["password"])) return Results.Content("credenciales inválidas", "text/plain");
    if (!interaction.IsValidReturnUrl(returnUrl)) return Results.BadRequest("returnUrl inválida");
    var user = users.FindByUsername(form["username"]);
    await http.SignInAsync(new IdentityServerUser(user.SubjectId) { DisplayName = user.Username, AdditionalClaims = user.Claims });
    return Results.Redirect(returnUrl);
});

app.Run();
