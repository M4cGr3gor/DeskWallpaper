namespace DeskWallpaper.Nucleo;

/// <summary>Cómo encaja un resultado con la resolución pedida. Recorta: un vídeo de otra forma que la cubre sin ampliarse.</summary>
public enum Encaje { Exacta, Mayor, Recorta }

public static class EncajeResolucion
{
    /// <summary>Diferencia de forma admitida para "mayor con la misma forma".</summary>
    public const double ToleranciaForma = 0.02;

    /// <summary>Exacta si es igual; Mayor si es ≥ en ancho y alto y la forma difiere como mucho un 2 %; null si no encaja.</summary>
    public static Encaje? De(SizePx resultado, SizePx pedida)
    {
        if (resultado == pedida) return Encaje.Exacta;
        if (resultado.Ancho < pedida.Ancho || resultado.Alto < pedida.Alto || pedida.Alto == 0 || resultado.Alto == 0) return null;
        var formaResultado = (double)resultado.Ancho / resultado.Alto;
        var formaPedida = (double)pedida.Ancho / pedida.Alto;
        return Math.Abs(formaResultado / formaPedida - 1) <= ToleranciaForma ? Encaje.Mayor : null;
    }

    /// <summary>
    /// Un vídeo se recorta para cubrir su zona al aplicarlo, así que vale si la cubre sin ampliarse (2 % de margen):
    /// un 3840×2160 sirve para la zona 2+1 (3840×1080). Exacta y Mayor como en <see cref="De"/>; si no, Recorta.
    /// </summary>
    public static Encaje? DeVideo(SizePx video, SizePx pedida)
    {
        if (video == pedida) return Encaje.Exacta;
        if (video.Ancho <= 0 || video.Alto <= 0 || pedida.Ancho <= 0 || pedida.Alto <= 0) return null;
        var ampliacion = Math.Max((double)pedida.Ancho / video.Ancho, (double)pedida.Alto / video.Alto);
        if (ampliacion > 1 + ToleranciaForma) return null;
        return De(video, pedida) ?? Encaje.Recorta;
    }
}

public static class ResolucionesBusqueda
{
    /// <summary>
    /// Una por zona, sin repetir tamaños: el rectángulo que envuelve a sus monitores, en píxeles físicos.
    /// Etiqueta con el Id de la zona: "3840×1080 (2+1)".
    /// </summary>
    public static IReadOnlyList<(string Etiqueta, SizePx Tamano)> DeZonas(IReadOnlyList<Zona> zonas, Disposicion d)
    {
        var lista = new List<(string, SizePx)>();
        foreach (var z in zonas)
        {
            var monitores = z.MonitorIds.Select(d.Buscar).OfType<DatosMonitor>().ToList();
            if (monitores.Count == 0) continue;
            var x = monitores.Min(m => m.Escritorio.X);
            var y = monitores.Min(m => m.Escritorio.Y);
            var tamano = new SizePx(monitores.Max(m => m.Escritorio.X + m.Escritorio.Ancho) - x,
                                    monitores.Max(m => m.Escritorio.Y + m.Escritorio.Alto) - y);
            if (lista.Any(e => e.Item2 == tamano)) continue;
            lista.Add(($"{tamano.Ancho}×{tamano.Alto} ({z.Id})", tamano));
        }
        return lista;
    }
}
