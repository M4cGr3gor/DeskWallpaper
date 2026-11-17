namespace DeskWallpaper.Nucleo;

/// <summary>
/// Un monitor tal como lo ve Windows.
/// </summary>
/// <param name="Id">Ruta de dispositivo del monitor (la misma que usa IDesktopWallpaper). Estable entre sesiones.</param>
/// <param name="Numero">Número que Windows muestra en Configuración (\\.\DISPLAYn).</param>
/// <param name="Nombre">Nombre comercial (EDID).</param>
/// <param name="Escritorio">Posición y resolución en el escritorio virtual, en píxeles físicos.</param>
/// <param name="Escala">Escala de Windows (1,0 = 100 %).</param>
/// <param name="TamanoFisico">Tamaño del área visible en milímetros.</param>
/// <param name="Principal">Si es el monitor principal de Windows.</param>
public sealed record DatosMonitor(string Id, int Numero, string Nombre, RectPx Escritorio,
                                  double Escala, SizeMm TamanoFisico, bool Principal)
{
    public double MmPorPxX => TamanoFisico.Ancho / Escritorio.Ancho;
    public double MmPorPxY => TamanoFisico.Alto / Escritorio.Alto;
}

/// <summary>Conjunto de monitores activos y su colocación.</summary>
public sealed record Disposicion(IReadOnlyList<DatosMonitor> Monitores)
{
    /// <summary>Busca un monitor por Id sin distinguir mayúsculas.</summary>
    public DatosMonitor? Buscar(string id) =>
        Monitores.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Sustituye el tamaño físico de los monitores indicados (correcciones del usuario).</summary>
    public Disposicion ConCorrecciones(IReadOnlyDictionary<string, SizeMm> tamanos)
    {
        var comparador = StringComparer.OrdinalIgnoreCase;
        var corregidos = Monitores.Select(m =>
        {
            var clave = tamanos.Keys.FirstOrDefault(k => comparador.Equals(k, m.Id));
            return clave is null ? m : m with { TamanoFisico = tamanos[clave] };
        }).ToList();
        return new Disposicion(corregidos);
    }
}
