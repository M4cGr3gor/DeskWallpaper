namespace DeskWallpaper.Nucleo;

/// <summary>Formatos de imagen que se pueden usar como fondo.</summary>
public static class FormatosImagen
{
    public static readonly IReadOnlyList<string> Extensiones = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    /// <summary>Por extensión, sin distinguir mayúsculas.</summary>
    public static bool EsImagenAdmitida(string ruta) =>
        !string.IsNullOrEmpty(ruta) && Extensiones.Contains(Path.GetExtension(ruta), StringComparer.OrdinalIgnoreCase);
}
