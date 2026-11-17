using System.Globalization;

namespace DeskWallpaper.Nucleo;

/// <summary>Formatos de vídeo que se pueden usar como fondo animado (los que reproduce Windows con sus extensiones).</summary>
public static class FormatosVideo
{
    public static readonly IReadOnlyList<string> Extensiones = [".mp4", ".webm", ".mkv", ".mov", ".m4v"];

    /// <summary>Por extensión, sin distinguir mayúsculas.</summary>
    public static bool EsVideo(string ruta) =>
        !string.IsNullOrEmpty(ruta) && Extensiones.Contains(Path.GetExtension(ruta), StringComparer.OrdinalIgnoreCase);

    /// <summary>«0:20», «1:05», «1:02:03».</summary>
    public static string TextoDuracion(TimeSpan d)
    {
        var total = (long)Math.Round(d.TotalSeconds);
        var (h, m, s) = (total / 3600, total / 60 % 60, total % 60);
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}:{m:00}:{s:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{m}:{s:00}");
    }
}

/// <summary>Tamaño nativo y duración de un vídeo.</summary>
public sealed record InfoVideo(SizePx Tamano, TimeSpan Duracion);

/// <summary>Lee un vídeo: escribe un fotograma en JPEG y devuelve su tamaño y su duración (en la app, el ayudante en C++).</summary>
public interface ILectorVideo
{
    /// <exception cref="ExcepcionImagen">Si el vídeo no se puede leer.</exception>
    InfoVideo Leer(string rutaVideo, string rutaFotogramaJpg);
}
