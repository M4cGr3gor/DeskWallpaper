using System.Text;
using System.Text.Json;

namespace DeskWallpaper.Nucleo;

/// <summary>
/// Opciones de «Reproducción…». <see cref="TopeFps"/> = 0: como el vídeo. <see cref="ReducirVideos"/>: usar copias del
/// tamaño de los monitores de los vídeos más grandes (<see cref="VideosReducidos"/>).
/// </summary>
public sealed record OpcionesReproduccion(bool PausarMaximizadas = true, int TopeFps = 30, bool ReducirVideos = true);

/// <summary>Un monitor de una zona con vídeo: su Id (el de IDesktopWallpaper) y el trozo del vídeo (0–1) que se ve en él.</summary>
public sealed record PantallaAnimada(string MonitorId, double U0, double V0, double U1, double V1);

/// <summary>Una zona con vídeo: se decodifica una vez y cada monitor copia su trozo.</summary>
public sealed record ZonaAnimada(string Video, IReadOnlyList<PantallaAnimada> Pantallas);

/// <summary>
/// Lo que reproduce el ayudante en C++ (<c>animado.json</c>). Lo escribe la app al aplicar o probar; el ayudante solo
/// lo modifica para guardar la pausa manual. Todos los cálculos se hacen aquí: el ayudante no sabe nada de encuadres.
/// </summary>
public sealed record ConfigAnimado(int TopeFps, bool PausarMaximizadas, bool PausaManual, IReadOnlyList<ZonaAnimada> Zonas)
{
    public static ConfigAnimado Vacia(OpcionesReproduccion o) => new(o.TopeFps, o.PausarMaximizadas, false, []);
}

/// <summary>De una combinación aplicada a la configuración del ayudante: solo las zonas cuyo archivo es un vídeo.</summary>
public static class GeneradorAnimado
{
    /// <param name="tamanoVideo">Tamaño nativo de un vídeo (el de su fotograma de referencia).</param>
    public static ConfigAnimado Desde(Combinacion c, Disposicion d, LienzoFisico l, Func<string, SizePx> tamanoVideo,
                                      OpcionesReproduccion o)
    {
        var zonas = new List<ZonaAnimada>();
        foreach (var z in c.Zonas.Where(z => FormatosVideo.EsVideo(z.RutaImagen)))
        {
            var tamano = tamanoVideo(z.RutaImagen);
            var pantallas = PlanRecorte.Calcular(tamano, z.MonitorIds, d, l, z.Encuadre)
                .Select(p => new PantallaAnimada(p.MonitorId,
                    A01(p.Region.X / tamano.Ancho), A01(p.Region.Y / tamano.Alto),
                    A01((p.Region.X + p.Region.Ancho) / tamano.Ancho), A01((p.Region.Y + p.Region.Alto) / tamano.Alto)))
                .ToList();
            zonas.Add(new ZonaAnimada(z.RutaImagen, pantallas));
        }
        return new ConfigAnimado(o.TopeFps, o.PausarMaximizadas, false, zonas);
    }

    private static double A01(double v) => Math.Clamp(v, 0, 1);
}

/// <summary><c>animado.json</c> en la carpeta de datos: UTF-8 sin BOM y nombres en camelCase (lo lee el ayudante en C++).</summary>
public static class AlmacenAnimado
{
    public const string Archivo = "animado.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Guardar(string carpetaDatos, ConfigAnimado c)
    {
        Directory.CreateDirectory(carpetaDatos);
        var ruta = Path.Combine(carpetaDatos, Archivo);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(c, Json), new UTF8Encoding(false));
        File.Move(temporal, ruta, overwrite: true);
    }

    /// <returns>Null si no existe o está dañado.</returns>
    public static ConfigAnimado? Leer(string carpetaDatos)
    {
        try
        {
            var c = JsonSerializer.Deserialize<ConfigAnimado>(File.ReadAllText(Path.Combine(carpetaDatos, Archivo)), Json);
            return c is null ? null : c with { Zonas = c.Zonas ?? [] };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public static void Borrar(string carpetaDatos)
    {
        try { File.Delete(Path.Combine(carpetaDatos, Archivo)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* se sobrescribirá */ }
    }
}
