using System.Reflection;
using System.Runtime.Loader;
using Mantenedores.Core.Providers;

namespace Mantenedores.Api.Infrastructure;

/// <summary>
/// Proveedores de motor cargados desde una carpeta, sin recompilar la app (P-064, D-092). Apagado por defecto:
/// <c>Providers:PluginsDirectory</c> vacío. Cada subcarpeta es un plugin: <c>&lt;carpeta&gt;/&lt;Nombre&gt;/&lt;Nombre&gt;.dll</c>
/// con sus dependencias (la salida de <c>dotnet publish</c> del proyecto del plugin). Guía en <c>docs/motores.md</c>.
/// </summary>
/// <remarks>
/// Cargar un plugin es ejecutar su código con los permisos de la app: la carpeta solo debe poder escribirla quien
/// administra el servidor. Un plugin que no carga detiene el arranque con el motivo, en vez de dejar un motor a medias.
/// </remarks>
public static class ProviderPlugins
{
    public const string Section = "Providers";

    public sealed record LoadedPlugin(string Name, string Path, IReadOnlyList<IDatabaseProvider> Providers);

    public static IReadOnlyList<LoadedPlugin> Load(string directory)
    {
        if (!Directory.Exists(directory))
            throw new InvalidOperationException($"Providers:PluginsDirectory apunta a '{directory}', que no existe.");

        var loaded = new List<LoadedPlugin>();
        foreach (var folder in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
        {
            var name = System.IO.Path.GetFileName(folder);
            var main = System.IO.Path.Combine(folder, name + ".dll");
            if (!File.Exists(main))
                throw new InvalidOperationException($"El plugin '{name}' no tiene {name}.dll en su carpeta ({folder}).");
            loaded.Add(new LoadedPlugin(name, main, LoadOne(name, main)));
        }
        return loaded;
    }

    private static IReadOnlyList<IDatabaseProvider> LoadOne(string name, string main)
    {
        try
        {
            var assembly = new PluginLoadContext(main).LoadFromAssemblyPath(main);
            var types = assembly.GetExportedTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDatabaseProvider).IsAssignableFrom(t))
                .ToList();
            if (types.Count == 0)
                throw new InvalidOperationException($"El plugin '{name}' no tiene ninguna clase pública que implemente IDatabaseProvider.");
            return types.Select(t => t.GetConstructor(Type.EmptyTypes) is { } ctor
                    ? (IDatabaseProvider)ctor.Invoke(null)
                    : throw new InvalidOperationException($"{t.FullName} (plugin '{name}') necesita un constructor público sin parámetros."))
                .ToList();
        }
        catch (Exception ex) when (ex is TypeLoadException or MissingMethodException or FileLoadException or BadImageFormatException
                                       or ReflectionTypeLoadException)
        {
            throw new InvalidOperationException(
                $"No se pudo cargar el plugin '{name}' ({main}): ¿está compilado para esta versión de Mantenedores.Core? {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Contexto propio por plugin: sus dependencias (el driver del motor, por ejemplo) se cargan desde su carpeta y no
    /// chocan con las de la app ni con las de otro plugin. Mantenedores.Core y lo que Core expone se comparten con la app,
    /// para que <see cref="IDatabaseProvider"/> sea el mismo tipo en los dos lados.
    /// </summary>
    private sealed class PluginLoadContext(string mainPath) : AssemblyLoadContext(System.IO.Path.GetFileNameWithoutExtension(mainPath))
    {
        private static readonly HashSet<string> Shared = SharedNames();

        private readonly AssemblyDependencyResolver _resolver = new(mainPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is { } n && Shared.Contains(n)) return null; // el de la app
            return _resolver.ResolveAssemblyToPath(assemblyName) is { } path ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;

        private static HashSet<string> SharedNames()
        {
            var core = typeof(IDatabaseProvider).Assembly;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { core.GetName().Name! };
            foreach (var r in core.GetReferencedAssemblies()) names.Add(r.Name!);
            return names;
        }
    }
}
