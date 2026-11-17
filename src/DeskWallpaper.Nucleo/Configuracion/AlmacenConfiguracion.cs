using System.Text.Encodings.Web;
using System.Text.Json;

namespace DeskWallpaper.Nucleo;

/// <summary>Lee y guarda <c>configuracion.json</c> en la carpeta de datos.</summary>
public static class AlmacenConfiguracion
{
    public const string NombreArchivo = "configuracion.json";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // acentos legibles; el archivo no se incrusta en HTML
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }, // "Azul", no 5
    };

    /// <summary>
    /// Carga la configuración. Si no existe, devuelve la de primer arranque. Si está corrupta,
    /// la conserva como <c>configuracion.json.bak-&lt;fecha&gt;</c> y devuelve la de primer arranque.
    /// </summary>
    public static Configuracion Cargar(string carpetaDatos)
    {
        var ruta = Path.Combine(carpetaDatos, NombreArchivo);
        if (!File.Exists(ruta)) return Configuracion.PorDefecto();

        string texto;
        try
        {
            texto = File.ReadAllText(ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Configuracion.PorDefecto(); // ilegible (bloqueado, sin permisos): no se toca el archivo
        }

        try
        {
            var c = JsonSerializer.Deserialize<Configuracion>(texto, Opciones)
                    ?? throw new JsonException("Configuración vacía");
            return Normalizar(c);
        }
        catch (JsonException)
        {
            try
            {
                File.Move(ruta, $"{ruta}.bak-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Si no se puede apartar, la próxima vez se volverá a intentar.
            }
            return Configuracion.PorDefecto();
        }
    }

    /// <summary>Guarda de forma atómica (archivo temporal y renombrado).</summary>
    public static void Guardar(string carpetaDatos, Configuracion c)
    {
        Directory.CreateDirectory(carpetaDatos);
        var ruta = Path.Combine(carpetaDatos, NombreArchivo);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(c, Opciones));
        File.Move(temporal, ruta, overwrite: true);
    }

    // El deserializador crea diccionarios sensibles a mayúsculas y puede dejar nulos si faltan propiedades.
    /// <summary>Los campos que falten en el JSON llegan como null: se dejan vacíos.</summary>
    private static FuenteBusqueda Completar(FuenteBusqueda f) => f with
    {
        PlantillaExacta = f.PlantillaExacta ?? "",
        PlantillaMayor = f.PlantillaMayor ?? "",
        PlantillaSinResolucion = f.PlantillaSinResolucion ?? "",
        Cabeceras = f.Cabeceras ?? new Dictionary<string, string>(),
        RutaResultados = f.RutaResultados ?? "",
        RutaMiniatura = f.RutaMiniatura ?? "",
        RutaImagen = f.RutaImagen ?? "",
        RutaAncho = f.RutaAncho ?? "",
        RutaAlto = f.RutaAlto ?? "",
        RutaFicha = f.RutaFicha ?? "",
        RutaUltimaPagina = f.RutaUltimaPagina ?? "",
        Clave = f.Clave ?? "",
        RutaArchivos = f.RutaArchivos ?? "",
        RutaArchivoUrl = f.RutaArchivoUrl ?? "",
        RutaArchivoAncho = f.RutaArchivoAncho ?? "",
        RutaArchivoAlto = f.RutaArchivoAlto ?? "",
        RutaDuracion = f.RutaDuracion ?? "",
    };

    private static EstadoBiblioteca NormalizarBiblioteca(EstadoBiblioteca? b)
    {
        b ??= new EstadoBiblioteca();
        return b with
        {
            Seccion = b.Seccion ?? "Todas",
            Texto = b.Texto ?? "",
            TamanoMiniatura = Math.Clamp(b.TamanoMiniatura, 120, 240),
        };
    }

    private static Configuracion Normalizar(Configuracion c)
    {
        c.CarpetasVigiladas ??= [];
        c.TamanosCorregidos = new Dictionary<string, SizeMm>(c.TamanosCorregidos ?? [], StringComparer.OrdinalIgnoreCase);
        c.MarcosMm = new Dictionary<string, double>(c.MarcosMm ?? [], StringComparer.OrdinalIgnoreCase);
        c.Biblioteca = NormalizarBiblioteca(c.Biblioteca);
        // Fondos animados: un archivo anterior no trae su pestaña, su biblioteca ni las opciones de reproducción.
        c.BibliotecaAnimados = NormalizarBiblioteca(c.BibliotecaAnimados);
        c.PestanaActiva = c.PestanaActiva == nameof(Pestana.Animados) ? nameof(Pestana.Animados) : nameof(Pestana.Imagenes);
        c.Reproduccion ??= new OpcionesReproduccion();
        if (c.Reproduccion.TopeFps is not (0 or 30)) c.Reproduccion = c.Reproduccion with { TopeFps = 30 };
        c.TopeMiniaturasMb = c.TopeMiniaturasMb <= 0 ? 200 : Math.Clamp(c.TopeMiniaturasMb, 50, 2000);
        // Anchos de los paneles: 0 es el de siempre; lo demás, dentro de sus límites.
        c.AnchoNavegacion = c.AnchoNavegacion <= 0 || double.IsNaN(c.AnchoNavegacion) ? 0
            : Math.Clamp(c.AnchoNavegacion, Configuracion.AnchoNavegacionMinimo, Configuracion.AnchoNavegacionMaximo);
        c.AnchoPanel = c.AnchoPanel <= 0 || double.IsNaN(c.AnchoPanel) ? 0
            : Math.Clamp(c.AnchoPanel, Configuracion.AnchoPanelMinimo, Configuracion.AnchoPanelMaximo);
        // Búsqueda online: un archivo anterior no trae fuentes; a una fuente editada a mano le pueden faltar campos.
        c.FuentesBusqueda = c.FuentesBusqueda is null
            ? [.. FuentesPredeterminadas.Todas()]
            : [.. c.FuentesBusqueda.Where(f => f is not null && !string.IsNullOrWhiteSpace(f.Nombre)).Select(Completar)];
        // Búsqueda de vídeos: a una configuración anterior se le añaden una vez las fuentes de vídeo de serie.
        if (!c.FuentesVideoAnadidas && !c.FuentesBusqueda.Any(f => f.Contenido == ContenidoFuente.Videos))
            c.FuentesBusqueda.AddRange(FuentesPredeterminadas.Videos());
        c.FuentesVideoAnadidas = true;
        if (string.IsNullOrWhiteSpace(c.CarpetaDescargas)) c.CarpetaDescargas = Configuracion.CarpetaDescargasPorDefecto;
        c.Busqueda ??= new EstadoBusqueda();
        c.Busqueda = c.Busqueda with { Texto = c.Busqueda.Texto ?? "", Desmarcadas = c.Busqueda.Desmarcadas ?? [] };
        c.ZonasPorFirma = new Dictionary<string, List<List<string>>>(c.ZonasPorFirma ?? [], StringComparer.OrdinalIgnoreCase);

        // Un JSON editado a mano puede traer zonas incompletas: se descartan en vez de fallar después.
        if (c.LienzoPendiente is { } pendiente)
        {
            var validas = (pendiente.Zonas ?? [])
                .Where(z => z is not null && z.MonitorIds is { Count: > 0 } && !string.IsNullOrEmpty(z.RutaImagen))
                .Select(z => z with { Encuadre = z.Encuadre ?? new Encuadre() })
                .ToList();
            c.LienzoPendiente = validas.Count == 0
                ? null
                : new Combinacion(pendiente.FirmaDisposicion ?? "", validas);
        }
        return c;
    }
}
