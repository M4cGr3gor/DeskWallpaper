namespace DeskWallpaper.Nucleo;

/// <summary>Hace una copia reducida de un vídeo (en la app, el modo <c>--reducir</c> del ayudante en C++).</summary>
public interface IReductorVideo
{
    /// <param name="duracion">La del vídeo, para el progreso.</param>
    /// <param name="progreso">De 0 a 1.</param>
    /// <exception cref="ExcepcionImagen">Si no se ha podido (no deja la salida a medias).</exception>
    /// <exception cref="OperationCanceledException">Si se cancela.</exception>
    void Reducir(string entrada, string salida, SizePx tamano, TimeSpan duracion, Action<double>? progreso, CancellationToken ct);
}

/// <summary>Por dónde va la reducción: vídeo <see cref="Indice"/> de <see cref="Total"/>, y cuánto lleva de él (0–1).</summary>
public sealed record ProgresoReduccion(int Indice, int Total, string Nombre, double Fraccion);

/// <summary>
/// Opción «Reducir los vídeos más grandes que sus monitores». La memoria del ayudante
/// depende de la resolución del vídeo, no de lo comprimido que esté (medido: un 4K en un monitor de 1080p gastaba
/// ~190 MB de RAM y ~340 MB de la tarjeta; uno de 1080p, ~100 y ~100). Al aplicar, cada vídeo más de un 10 % mayor de
/// lo que su zona muestra se cambia por una copia de ese tamaño, guardada en <c>cache\videos</c> con tope. El original
/// no se toca: la biblioteca, el lienzo y el fondo de reserva siguen usándolo; solo <c>animado.json</c> apunta a la copia.
/// </summary>
public sealed class VideosReducidos(string carpetaDatos, FotogramasVideo fotogramas, IReductorVideo reductor)
{
    /// <summary>Tope de la caché de copias reducidas.</summary>
    public const long TopeBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>Solo se reduce si el vídeo es más de un 10 % mayor de lo necesario: por menos no compensa la espera.</summary>
    public const double Margen = 1.1;

    public string Carpeta => Path.Combine(carpetaDatos, "cache", "videos");

    /// <summary>
    /// El tamaño al que reducir el vídeo de una zona para que cada monitor siga viendo su trozo a resolución nativa,
    /// con lados pares (lo pide H.264); null si no hace falta reducirlo o no se puede saber.
    /// </summary>
    public static SizePx? TamanoReducido(SizePx video, ZonaAnimada zona, Disposicion d)
    {
        if (video.Ancho <= 0 || video.Alto <= 0 || zona.Pantallas.Count == 0) return null;
        double necesarioAncho = 0, necesarioAlto = 0;
        foreach (var p in zona.Pantallas)
        {
            if (d.Buscar(p.MonitorId) is not { } m) return null;
            double du = p.U1 - p.U0, dv = p.V1 - p.V0;
            if (du <= 0 || dv <= 0) return null;
            necesarioAncho = Math.Max(necesarioAncho, m.Escritorio.Ancho / du);
            necesarioAlto = Math.Max(necesarioAlto, m.Escritorio.Alto / dv);
        }
        var factor = Math.Max(necesarioAncho / video.Ancho, necesarioAlto / video.Alto);
        if (factor * Margen >= 1) return null;
        return new SizePx(Par(video.Ancho * factor), Par(video.Alto * factor));
    }

    /// <summary>Cuántos vídeos de la configuración hay que reducir ahora (los que no están ya en la caché).</summary>
    public int Pendientes(ConfigAnimado c, Disposicion d) =>
        c.Zonas.Count(z => Plan(z, d) is { } plan && !File.Exists(plan.Destino));

    /// <summary>
    /// La configuración con cada vídeo grande cambiado por su copia reducida (haciéndola si no está en la caché). Si una
    /// copia falla, esa zona sigue con el original y se añade un aviso.
    /// </summary>
    /// <exception cref="OperationCanceledException">Si se cancela (no deja archivos a medias).</exception>
    public ConfigAnimado Preparar(ConfigAnimado c, Disposicion d, List<string> avisos, Action<ProgresoReduccion>? progreso,
                                  CancellationToken ct)
    {
        var total = Pendientes(c, d);
        var indice = 0;
        var zonas = new List<ZonaAnimada>();
        foreach (var zona in c.Zonas)
        {
            ct.ThrowIfCancellationRequested();
            if (Plan(zona, d) is not { } plan)
            {
                zonas.Add(zona);
                continue;
            }
            if (File.Exists(plan.Destino))
            {
                Tocar(plan.Destino);
                zonas.Add(zona with { Video = plan.Destino });
                continue;
            }

            indice++;
            var nombre = Path.GetFileName(zona.Video);
            var actual = indice;
            Directory.CreateDirectory(Carpeta);
            var temporal = Path.Combine(Carpeta, $"{Path.GetFileNameWithoutExtension(plan.Destino)}.{Guid.NewGuid():N}.tmp.mp4");
            try
            {
                reductor.Reducir(zona.Video, temporal, plan.Tamano, plan.Duracion,
                    f => progreso?.Invoke(new ProgresoReduccion(actual, total, nombre, f)), ct);
                File.Move(temporal, plan.Destino, overwrite: true);
                zonas.Add(zona with { Video = plan.Destino });
            }
            catch (Exception ex) when (ex is ExcepcionImagen or IOException or UnauthorizedAccessException)
            {
                avisos.Add($"No se ha podido reducir «{nombre}»: se usa el original. {ex.Message}");
                zonas.Add(zona);
            }
            finally
            {
                Borrar(temporal);
            }
        }
        return c with { Zonas = zonas };
    }

    /// <summary>Borra las copias usadas hace más tiempo hasta quedar por debajo del tope, salvo las que están en uso.</summary>
    public void Recortar(IEnumerable<string> enUso, long topeBytes = TopeBytes)
    {
        if (!Directory.Exists(Carpeta)) return;
        var protegidas = enUso.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var archivos = new DirectoryInfo(Carpeta).EnumerateFiles("*.mp4").ToList();
        // Temporales de una reducción que se cortó (la app se cerró a medias): fuera si tienen más de un día.
        foreach (var f in archivos.Where(f => f.Name.Contains(".tmp.", StringComparison.Ordinal) &&
                                              f.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)))
            Borrar(f.FullName);

        var copias = archivos.Where(f => !f.Name.Contains(".tmp.", StringComparison.Ordinal)).OrderBy(f => f.LastWriteTimeUtc).ToList();
        var ocupado = copias.Sum(f => f.Length);
        foreach (var f in copias)
        {
            if (ocupado <= topeBytes) break;
            if (protegidas.Contains(f.FullName)) continue;
            ocupado -= f.Length;
            Borrar(f.FullName);
        }
    }

    private sealed record PlanReduccion(SizePx Tamano, TimeSpan Duracion, string Destino);

    private PlanReduccion? Plan(ZonaAnimada zona, Disposicion d)
    {
        try
        {
            var (_, info) = fotogramas.Preparar(zona.Video);
            if (TamanoReducido(info.Tamano, zona, d) is not { } tamano) return null;
            var huella = Huella.Calcular(zona.Video);
            return new PlanReduccion(tamano, info.Duracion, Path.Combine(Carpeta, $"{huella}_{tamano.Ancho}x{tamano.Alto}.mp4"));
        }
        catch (Exception ex) when (ex is ExcepcionImagen or IOException or UnauthorizedAccessException)
        {
            return null;   // un vídeo que no se puede leer se queda como está (el ayudante lo omitirá)
        }
    }

    /// <summary>Hacia arriba al siguiente par (sin pasarse por errores de redondeo: 1920,0000001 es 1920).</summary>
    private static int Par(double lado) => Math.Max(2, (int)Math.Ceiling(Math.Round(lado, 3) / 2) * 2);

    private static void Tocar(string ruta)
    {
        try { File.SetLastWriteTimeUtc(ruta, DateTime.UtcNow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* da igual */ }
    }

    private static void Borrar(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* en uso: se queda */ }
    }
}
