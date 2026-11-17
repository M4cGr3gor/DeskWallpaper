namespace DeskWallpaper.Nucleo;

/// <summary>Lee imágenes y genera los recortes a la resolución de cada monitor.</summary>
public interface IRenderizador
{
    /// <summary>Tamaño de la imagen ya orientada según su EXIF. Lanza <see cref="ExcepcionImagen"/> si no se puede leer.</summary>
    SizePx LeerTamano(string ruta);

    /// <summary>Recorta <paramref name="region"/> (en px de la imagen orientada) y la guarda como PNG de tamaño <paramref name="salida"/>.</summary>
    void Renderizar(string rutaOrigen, RectD region, SizePx salida, string rutaDestino) =>
        Renderizar(rutaOrigen, [new Recorte(region, salida, rutaDestino)]);

    /// <summary>Varios recortes de la misma imagen decodificándola una sola vez.</summary>
    void Renderizar(string rutaOrigen, IReadOnlyList<Recorte> recortes);

    /// <summary>
    /// PNG en memoria de la imagen orientada y reducida para que su lado mayor no pase de
    /// <paramref name="ladoMaximo"/> (nunca se amplía), y el tamaño original ya orientado.
    /// </summary>
    (byte[] Png, SizePx Original) Reducida(string ruta, int ladoMaximo);

    /// <summary>Píxeles RGBA (sin premultiplicar) de la imagen orientada, con el lado mayor ≤ <paramref name="lado"/>.</summary>
    byte[] PixelesReducidos(string ruta, int lado);

    /// <summary>JPEG (calidad 85) de la imagen orientada con el lado mayor ≤ <paramref name="lado"/>.</summary>
    byte[] Miniatura(string ruta, int lado);

    /// <summary>
    /// PNG de <paramref name="ancho"/> px con los monitores en su posición física y la imagen de cada
    /// zona recortada como se aplicaría (fondo transparente; monitores sin imagen en gris oscuro).
    /// Las zonas con monitores que no están en <paramref name="d"/> se omiten.
    /// </summary>
    byte[] ComponerMiniatura(Combinacion c, Disposicion d, LienzoFisico l, int ancho);
}

/// <summary>Un recorte de una imagen: región (px de la imagen orientada), tamaño de salida y PNG destino.</summary>
public sealed record Recorte(RectD Region, SizePx Salida, string RutaDestino);

/// <summary>Una imagen no se ha podido leer o no es una imagen válida.</summary>
public sealed class ExcepcionImagen(string mensaje, Exception? interna = null) : Exception(mensaje, interna);
