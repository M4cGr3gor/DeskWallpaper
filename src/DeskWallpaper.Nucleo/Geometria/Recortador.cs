namespace DeskWallpaper.Nucleo;

/// <summary>
/// Calcula qué parte de la imagen va a cada zona y a cada monitor.
/// </summary>
public static class Recortador
{
    /// <summary>
    /// Región de la imagen (en sus píxeles) que cubre la envolvente física de la zona, con la misma
    /// proporción que esa envolvente. Siempre queda dentro de la imagen: nunca hay bandas vacías.
    /// </summary>
    public static RectD RegionZona(SizePx imagen, RectMm envolvente, Encuadre e)
    {
        var proporcionZona = envolvente.Ancho / envolvente.Alto;
        var proporcionImagen = (double)imagen.Ancho / imagen.Alto;

        // Rectángulo más grande con la proporción de la zona que cabe en la imagen.
        double ancho, alto;
        if (proporcionImagen >= proporcionZona)
        {
            alto = imagen.Alto;
            ancho = alto * proporcionZona;
        }
        else
        {
            ancho = imagen.Ancho;
            alto = ancho / proporcionZona;
        }

        var zoom = Math.Max(1.0, e.Zoom);
        ancho /= zoom;
        alto /= zoom;

        var x = Math.Clamp(e.CentroX * imagen.Ancho - ancho / 2, 0, imagen.Ancho - ancho);
        var y = Math.Clamp(e.CentroY * imagen.Alto - alto / 2, 0, imagen.Alto - alto);
        return new RectD(x, y, ancho, alto);
    }

    /// <summary>Parte de la región de la zona que corresponde físicamente a un monitor.</summary>
    public static RectD RegionMonitor(RectD regionZona, RectMm envolvente, RectMm monitor)
    {
        var escalaX = regionZona.Ancho / envolvente.Ancho;
        var escalaY = regionZona.Alto / envolvente.Alto;
        return new RectD(
            regionZona.X + (monitor.X - envolvente.X) * escalaX,
            regionZona.Y + (monitor.Y - envolvente.Y) * escalaY,
            monitor.Ancho * escalaX,
            monitor.Alto * escalaY);
    }

    /// <summary>Cuánto se amplía la región al llevarla a la salida, en el eje que más se amplía (mayor que 1 = se amplía).</summary>
    public static double FactorAmpliacion(RectD regionMonitor, SizePx salida) =>
        Math.Max(salida.Ancho / regionMonitor.Ancho, salida.Alto / regionMonitor.Alto);
}
