using DeskWallpaper.Nucleo;
using SkiaSharp;

namespace DeskWallpaper.Pruebas.Video;

/// <summary>
/// Lector de vídeo de prueba: escribe un JPEG de color sólido del tamaño configurado, sin Media Foundation ni el
/// ayudante. Una ruta que contiene «roto» no se puede leer.
/// </summary>
internal sealed class LectorVideoFalso(int ancho = 384, int alto = 108, double segundos = 20) : ILectorVideo
{
    public int Lecturas;
    public SKColor Color { get; set; } = SKColors.OrangeRed;

    public InfoVideo Leer(string rutaVideo, string rutaFotogramaJpg)
    {
        Interlocked.Increment(ref Lecturas);
        if (rutaVideo.Contains("roto", StringComparison.OrdinalIgnoreCase))
            throw new ExcepcionImagen($"No se puede leer el vídeo «{Path.GetFileName(rutaVideo)}»");
        using var bmp = new SKBitmap(ancho, alto);
        bmp.Erase(Color);
        using var datos = bmp.Encode(SKEncodedImageFormat.Jpeg, 90);
        File.WriteAllBytes(rutaFotogramaJpg, datos.ToArray());
        return new InfoVideo(new SizePx(ancho, alto), TimeSpan.FromSeconds(segundos));
    }
}
