namespace Mantenedores.Core.Providers;

/// <summary>Versión y ajustes del servidor, para verificar la versión mínima (D-035) y diagnosticar (P-028).</summary>
public sealed record ServerInfo(
    string Product,
    Version Version,
    Version MinimumVersion,
    IReadOnlyDictionary<string, string> Settings)
{
    public bool IsSupported => Version >= MinimumVersion;

    public string Description => $"{Product} {Version}";
}
