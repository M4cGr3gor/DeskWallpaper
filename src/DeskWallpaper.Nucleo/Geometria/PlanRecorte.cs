namespace DeskWallpaper.Nucleo;

/// <summary>Qué parte de la imagen va a un monitor, a qué resolución y cuánto se amplía.</summary>
public sealed record RecorteMonitor(string MonitorId, RectD Region, SizePx Nativa, double Factor);

/// <summary>
/// Cálculo único de los recortes de una zona: lo usan el aplicador (archivos de fondo) y el lienzo
/// (vista en vivo), así lo que se ve es exactamente lo que se aplica.
/// </summary>
public static class PlanRecorte
{
    /// <exception cref="KeyNotFoundException">Algún monitor no está en la disposición.</exception>
    public static IReadOnlyList<RecorteMonitor> Calcular(SizePx imagen, IReadOnlyList<string> monitorIds,
                                                         Disposicion d, LienzoFisico l, Encuadre e)
    {
        var monitores = monitorIds
            .Select(id => d.Buscar(id) ?? throw new KeyNotFoundException($"Monitor desconocido: {id}"))
            .ToList();
        var envolvente = l.Envolvente(new Zona("", monitorIds));
        var regionZona = Recortador.RegionZona(imagen, envolvente, e);

        return monitores.Select(m =>
        {
            var region = Recortador.RegionMonitor(regionZona, envolvente, l.Monitor(m.Id));
            var nativa = new SizePx(m.Escritorio.Ancho, m.Escritorio.Alto);
            return new RecorteMonitor(m.Id, region, nativa, Recortador.FactorAmpliacion(region, nativa));
        }).ToList();
    }
}
