namespace DeskWallpaper.Nucleo;

/// <summary>
/// Imagen en memoria para la edición con IA: RGBA de 8 bits sin premultiplicar, filas seguidas
/// de arriba abajo. Nunca representa un archivo: el original se queda en el disco sin tocar.
/// </summary>
public sealed class ImagenRgba
{
    public ImagenRgba(int ancho, int alto, byte[] pixeles)
    {
        if (ancho <= 0 || alto <= 0) throw new ArgumentException("El tamaño de la imagen debe ser positivo");
        if (pixeles.Length != (long)ancho * alto * 4) throw new ArgumentException("Los píxeles no coinciden con el tamaño");
        Ancho = ancho;
        Alto = alto;
        Pixeles = pixeles;
    }

    public int Ancho { get; }
    public int Alto { get; }
    public byte[] Pixeles { get; }
    public SizePx Tamano => new(Ancho, Alto);
    public long Bytes => Pixeles.LongLength;

    public ImagenRgba Copia() => new(Ancho, Alto, (byte[])Pixeles.Clone());

    /// <summary>Copia de la región <paramref name="r"/>, que debe quedar dentro de la imagen.</summary>
    public ImagenRgba Recorte(RectPx r)
    {
        if (r.X < 0 || r.Y < 0 || r.Ancho <= 0 || r.Alto <= 0 || r.X + r.Ancho > Ancho || r.Y + r.Alto > Alto)
            throw new ArgumentOutOfRangeException(nameof(r), "La región se sale de la imagen");
        var datos = new byte[r.Ancho * r.Alto * 4];
        for (var y = 0; y < r.Alto; y++)
            Buffer.BlockCopy(Pixeles, ((r.Y + y) * Ancho + r.X) * 4, datos, y * r.Ancho * 4, r.Ancho * 4);
        return new ImagenRgba(r.Ancho, r.Alto, datos);
    }
}

/// <summary>Fallo al editar o guardar una imagen, con el mensaje en español para el usuario.</summary>
public sealed class ExcepcionEdicion(string mensaje, Exception? causa = null) : Exception(mensaje, causa)
{
    /// <summary>Se ha elegido la ruta del original al guardar: hay que pedir otra (el original nunca se toca).</summary>
    public bool EsElOriginal { get; init; }
}
