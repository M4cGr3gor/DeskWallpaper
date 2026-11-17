namespace DeskWallpaper.Nucleo;

/// <summary>Carpeta de imágenes que la biblioteca vigila.</summary>
public sealed record CarpetaVigilada(string Ruta, bool IncluirSubcarpetas);

/// <summary>
/// Lo que el usuario estaba viendo en la biblioteca. La sección se guarda por su nombre
/// (<see cref="SeccionBiblioteca"/>) para que el JSON sea legible.
/// </summary>
public sealed record EstadoBiblioteca(string Seccion = "Todas", string? Etiqueta = null, ColorNombre? Color = null,
                                      string Texto = "", int TamanoMiniatura = 160)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public SeccionBiblioteca SeccionActual =>
        Enum.TryParse<SeccionBiblioteca>(Seccion, out var s) && Enum.IsDefined(s) ? s : SeccionBiblioteca.Todas;
}

/// <summary>
/// Búsqueda online: último texto y casillas de resolución desmarcadas ("3840x1080"). Se guardan las
/// desmarcadas, no las marcadas, para que la resolución de una zona nueva salga marcada por defecto.
/// </summary>
public sealed record EstadoBusqueda(string Texto = "", IReadOnlyList<string>? Desmarcadas = null);

/// <summary>Tamaño y posición de la ventana principal (en píxeles físicos).</summary>
public sealed record EstadoVentana(int X, int Y, int Ancho, int Alto, bool Maximizada)
{
    /// <summary>
    /// Estado a guardar al cerrar. Minimizada: se conserva el anterior (Windows da -32000,-32000).
    /// Maximizada: se conserva el tamaño normal anterior para cuando deje de estarlo.
    /// </summary>
    public static EstadoVentana? AlCerrar(EstadoVentana? anterior, int x, int y, int ancho, int alto, bool maximizada, bool minimizada)
    {
        if (minimizada) return anterior;
        if (maximizada) return (anterior ?? new EstadoVentana(x, y, ancho, alto, false)) with { Maximizada = true };
        return new EstadoVentana(x, y, ancho, alto, false);
    }
}

/// <summary>Todo lo que el usuario configura y la aplicación debe recordar entre sesiones.</summary>
public sealed class Configuracion
{
    public int Version { get; set; } = 1;

    /// <summary>Carpetas de la biblioteca. Manda siempre la lista guardada, aunque esté vacía.</summary>
    public List<CarpetaVigilada> CarpetasVigiladas { get; set; } = [];

    /// <summary>Tamaño físico corregido por el usuario, por Id de monitor.</summary>
    public Dictionary<string, SizeMm> TamanosCorregidos { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hueco de los marcos en mm, con clave <see cref="LienzoFisico.ClaveMarco"/>.</summary>
    public Dictionary<string, double> MarcosMm { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zonas unidas o separadas por el usuario, por firma de disposición (listas de Id de monitor).</summary>
    public Dictionary<string, List<List<string>>> ZonasPorFirma { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Firma de la disposición en la que se aplicó el último fondo.</summary>
    public string? UltimaFirma { get; set; }

    public EstadoVentana? Ventana { get; set; }

    /// <summary>Tope de la caché de miniaturas de la biblioteca, en MB.</summary>
    public int TopeMiniaturasMb { get; set; } = 200;

    /// <summary>Sección, filtros, búsqueda y tamaño de miniaturas de la biblioteca.</summary>
    public EstadoBiblioteca Biblioteca { get; set; } = new();

    /// <summary>Lo que se estaba viendo en la pestaña «Fondos animados»: cada pestaña recuerda lo suyo.</summary>
    public EstadoBiblioteca BibliotecaAnimados { get; set; } = new();

    /// <summary>Pestaña abierta: «Imagenes» o «Animados» (nombres de <see cref="Pestana"/>).</summary>
    public string PestanaActiva { get; set; } = nameof(Pestana.Imagenes);

    /// <summary>Fuentes de la búsqueda online. null = las predeterminadas; una lista vacía se respeta.</summary>
    public List<FuenteBusqueda>? FuentesBusqueda { get; set; }

    /// <summary>
    /// Las fuentes de vídeo de serie ya se añadieron: se añaden una sola vez a
    /// una configuración anterior, para que borrarlas después se respete.
    /// </summary>
    public bool FuentesVideoAnadidas { get; set; }

    /// <summary>Dónde se guardan las imágenes descargadas. "" = <see cref="CarpetaDescargasPorDefecto"/>.</summary>
    public string CarpetaDescargas { get; set; } = "";

    /// <summary>Último texto buscado y casillas de resolución desmarcadas.</summary>
    public EstadoBusqueda Busqueda { get; set; } = new();

    /// <summary>Si el panel de información del visor estaba abierto.</summary>
    public bool VisorConInformacion { get; set; }

    /// <summary>Ancho de la navegación de la izquierda, arrastrado por el usuario (0 = el de siempre).</summary>
    public double AnchoNavegacion { get; set; }

    /// <summary>Ancho del panel de la zona, a la derecha (0 = el de siempre).</summary>
    public double AnchoPanel { get; set; }

    public const double AnchoNavegacionPorDefecto = 190, AnchoNavegacionMinimo = 150, AnchoNavegacionMaximo = 400;
    public const double AnchoPanelPorDefecto = 236, AnchoPanelMinimo = 200, AnchoPanelMaximo = 520;

    /// <summary>Opciones de «Reproducción…» de los fondos animados.</summary>
    public OpcionesReproduccion Reproduccion { get; set; } = new();

    public static string CarpetaDescargasPorDefecto => Path.Combine(CarpetaDescargasWindows(), "DeskWallpaper");

    /// <summary>
    /// La carpeta Descargas del usuario tal como la tiene Windows (puede haberla movido a otra unidad);
    /// si no se puede consultar, %USERPROFILE%\Downloads.
    /// </summary>
    public static string CarpetaDescargasWindows()
    {
        var descargas = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
        if (OperatingSystem.IsWindows() && Nativo.SHGetKnownFolderPath(ref descargas, 0, IntPtr.Zero, out var ruta) == 0)
        {
            try
            {
                if (System.Runtime.InteropServices.Marshal.PtrToStringUni(ruta) is { Length: > 0 } texto) return texto;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(ruta);
            }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private static class Nativo
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        public static extern int SHGetKnownFolderPath(ref Guid id, uint opciones, IntPtr usuario, out IntPtr ruta);
    }

    /// <summary>Lo que hay en el lienzo sin aplicar, para seguir donde se dejó.</summary>
    public Combinacion? LienzoPendiente { get; set; }

    /// <summary>Configuración del primer arranque.</summary>
    public static Configuracion PorDefecto() => new()
    {
        CarpetasVigiladas =
        [
            new CarpetaVigilada(@"D:\Imagenes\Dual monitor", false),
            new CarpetaVigilada(@"D:\Imagenes\1920x1200", false),
        ],
        FuentesVideoAnadidas = true,   // al cargarla recibe todas las de serie, las de vídeo incluidas
    };
}
