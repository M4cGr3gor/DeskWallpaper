namespace DeskWallpaper.Nucleo;

/// <summary>Imagen asignada a una zona (identificada por los Id de sus monitores, no por su posición).</summary>
public sealed record AsignacionZona(IReadOnlyList<string> MonitorIds, string RutaImagen, Encuadre Encuadre);

/// <summary>Conjunto de asignaciones para una disposición concreta.</summary>
public sealed record Combinacion(string FirmaDisposicion, IReadOnlyList<AsignacionZona> Zonas)
{
    /// <summary>
    /// Lienzo pendiente que hay que guardar: lo que hay ahora en el lienzo más las zonas guardadas de
    /// monitores que ahora no están conectados (para no perderlas si vuelven). Null si no queda nada.
    /// </summary>
    public static Combinacion? FusionarPendiente(Combinacion? guardada, Combinacion actual, Disposicion d)
    {
        var enUso = actual.Zonas.SelectMany(z => z.MonitorIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ausentes = (guardada?.Zonas ?? [])
            .Where(z => z.MonitorIds.Any(id => d.Buscar(id) is null) && !z.MonitorIds.Any(enUso.Contains));
        var zonas = actual.Zonas.Concat(ausentes).ToList();
        return zonas.Count == 0 ? null : new Combinacion(actual.FirmaDisposicion, zonas);
    }
}
