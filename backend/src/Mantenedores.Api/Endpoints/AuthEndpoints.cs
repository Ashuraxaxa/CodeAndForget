using Mantenedores.Api.Auth;

namespace Mantenedores.Api.Endpoints;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string Username, string Password);
    public sealed record LoginResponse(string Token, DateTime ExpiresAt, string Username);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", (LoginRequest req, TokenService tokens) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || !tokens.Validate(req.Username, req.Password ?? ""))
                return Results.Problem("Usuario o contraseña incorrectos.", statusCode: 401, title: "No autorizado");

            var (token, expires) = tokens.Issue(req.Username);
            return Results.Ok(new LoginResponse(token, expires, req.Username));
        }).AllowAnonymous();
    }
}
