namespace DeskWallpaper.Nucleo;

/// <summary>Decide dónde guarda la aplicación sus datos.</summary>
public static class CarpetaDatos
{
    private const string Prefijo = "--data=";

    /// <summary>
    /// <c>--data=&lt;carpeta&gt;</c> si se pasa; si no, <c>%LOCALAPPDATA%\DeskWallpaper</c>.
    /// Crea la carpeta si no existe.
    /// </summary>
    public static string Resolver(string[] args, Func<string> localAppData)
    {
        var parametro = args.LastOrDefault(a => a.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase));
        var carpeta = parametro is not null
            ? Path.GetFullPath(parametro[Prefijo.Length..].Trim('"'))
            : Path.Combine(localAppData(), "DeskWallpaper");
        Directory.CreateDirectory(carpeta);
        return carpeta;
    }
}
