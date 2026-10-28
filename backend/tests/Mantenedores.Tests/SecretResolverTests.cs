using Mantenedores.Core.Connections;
using Mantenedores.Core.Errors;

namespace Mantenedores.Tests;

/// <summary>Referencias a secretos en las cadenas de conexión (P-045): solo lo permitido, y sin filtrar el valor.</summary>
public sealed class SecretResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("secretos-").FullName;
    private SecretResolver Resolver(string prefix = "MNT_") => new(new SecretOptions { EnvPrefix = prefix, FilesDirectory = _dir });

    [Fact]
    public void Reemplaza_variables_con_el_prefijo_y_archivos_de_la_carpeta()
    {
        Environment.SetEnvironmentVariable("MNT_TEST_PASS", "s3cr3t");
        File.WriteAllText(Path.Combine(_dir, "usuario"), "app\n");

        var cs = Resolver().Resolve("Host=db;Username=${file:usuario};Password=${env:MNT_TEST_PASS}");
        Assert.Equal("Host=db;Username=app;Password=s3cr3t", cs);
        Assert.Equal("Host=db;Password=x", Resolver().Resolve("Host=db;Password=x")); // sin referencias, igual
    }

    [Fact]
    public void Rechaza_lo_que_no_esta_permitido_sin_mostrar_el_valor()
    {
        Environment.SetEnvironmentVariable("AUTH_JWT_KEY_TEST", "no-debe-salir");
        var ex = Assert.Throws<ValidationException>(() => Resolver().Resolve("Password=${env:AUTH_JWT_KEY_TEST}"));
        Assert.Contains("MNT_", ex.Errors["connectionString"]);
        Assert.DoesNotContain("no-debe-salir", ex.Errors["connectionString"]);

        Assert.Contains("no existe", Assert.Throws<ValidationException>(() =>
            Resolver().Resolve("Password=${env:MNT_NO_EXISTE_123}")).Errors["connectionString"]);
        Assert.Contains("fuera de", Assert.Throws<ValidationException>(() =>
            Resolver().Resolve("Password=${file:../../etc/passwd}")).Errors["connectionString"]);
        Assert.Contains("fuera de", Assert.Throws<ValidationException>(() =>
            Resolver().Resolve("Password=${file:/etc/passwd}")).Errors["connectionString"]);
        Assert.Contains("desactivadas", Assert.Throws<ValidationException>(() =>
            new SecretResolver(new SecretOptions { FilesDirectory = "" }).Resolve("Password=${file:x}")).Errors["connectionString"]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
