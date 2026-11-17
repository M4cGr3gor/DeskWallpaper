namespace DeskWallpaper.Nucleo;

public enum Categoria { Panoramica, UnMonitor, Otras }

/// <summary>Clasifica una imagen según su proporción, relativa a las pantallas actuales.</summary>
public static class ClasificadorProporcion
{
    /// <summary>Cuánto más ancha que la pantalla suelta más ancha tiene que ser una panorámica.</summary>
    public const double MargenPanoramica = 1.25;

    /// <summary>Por debajo de esta fracción de la pantalla suelta más estrecha, la imagen es "Otras".</summary>
    public const double MargenEstrecha = 0.8;

    /// <summary>
    /// Panorámica: proporción ≥ 1,25 × la de la pantalla suelta más ancha (así entran las 2560×1024
    /// aunque sean más estrechas que 32:9). Un monitor: desde 0,8 × la más estrecha hasta ese umbral.
    /// Otras: retratos, cuadradas… Si no hay zonas de un monitor, se usan los monitores.
    /// </summary>
    public static Categoria Clasificar(SizePx imagen, IReadOnlyList<Zona> zonas, LienzoFisico l)
    {
        var sueltas = zonas.Where(z => z.MonitorIds.Count == 1).Select(z => l.Envolvente(z)).ToList();
        if (sueltas.Count == 0) sueltas = zonas.SelectMany(z => z.MonitorIds).Select(l.Monitor).ToList();
        if (sueltas.Count == 0) return Categoria.Otras;

        var proporciones = sueltas.Select(r => r.Ancho / r.Alto).ToList();
        var proporcion = (double)imagen.Ancho / imagen.Alto;
        if (proporcion >= MargenPanoramica * proporciones.Max()) return Categoria.Panoramica;
        if (proporcion >= MargenEstrecha * proporciones.Min()) return Categoria.UnMonitor;
        return Categoria.Otras;
    }
}
