namespace DeskWallpaper.Nucleo;

/// <summary>Correcciones del tamaño físico que informa el monitor.</summary>
public static class TamanoPantalla
{
    /// <summary>Tamaño a partir de la diagonal en pulgadas (lo que el usuario conoce), con píxeles cuadrados.</summary>
    public static SizeMm DesdeDiagonal(double pulgadas, int anchoPx, int altoPx)
    {
        var diagonalMm = pulgadas * 25.4;
        var proporcion = (double)anchoPx / altoPx;
        var alto = diagonalMm / Math.Sqrt(proporcion * proporcion + 1);
        return new SizeMm(alto * proporcion, alto);
    }

    /// <summary>Diagonal en pulgadas de un tamaño físico.</summary>
    public static double DiagonalPulgadas(SizeMm tamano) =>
        Math.Sqrt(tamano.Ancho * tamano.Ancho + tamano.Alto * tamano.Alto) / 25.4;

    /// <summary>
    /// Ajusta el tamaño del EDID para que tenga la proporción de los píxeles (píxeles cuadrados),
    /// conservando la diagonal. El EDID suele venir redondeado a cm y su proporción no coincide
    /// con la del panel (ANMITE: 350×210 mm frente a 1920×1200 px); si no se corrige, la imagen
    /// nativa del monitor se recortaría y se estiraría.
    /// </summary>
    public static SizeMm PixelesCuadrados(SizeMm medido, int anchoPx, int altoPx)
    {
        if (anchoPx <= 0 || altoPx <= 0 || medido.Ancho <= 0 || medido.Alto <= 0) return medido;
        var diagonal = Math.Sqrt(medido.Ancho * medido.Ancho + medido.Alto * medido.Alto);
        var proporcion = (double)anchoPx / altoPx;
        var alto = diagonal / Math.Sqrt(proporcion * proporcion + 1);
        return new SizeMm(alto * proporcion, alto);
    }
}
