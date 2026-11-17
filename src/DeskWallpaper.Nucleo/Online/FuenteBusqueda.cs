namespace DeskWallpaper.Nucleo;

/// <summary>Web: se muestra tal cual en un navegador integrado. Api: sus resultados se leen y se juntan.</summary>
public enum TipoFuente { Web, Api }

/// <summary>Qué busca la fuente: cada pestaña de la biblioteca usa las suyas.</summary>
public enum ContenidoFuente { Imagenes, Videos }

/// <summary>
/// Un servicio de búsqueda de fondos, configurable por el usuario sin recompilar.
/// Las plantillas admiten {texto}, {ancho}, {alto}, {proporcion} y {pagina}; las rutas señalan, con puntos,
/// dónde está cada dato en la respuesta JSON (solo fuentes Api).
/// <para>
/// Búsqueda de vídeos: <see cref="Clave"/> sustituye a
/// {clave} en las direcciones y las cabeceras (la clave personal del usuario en ese servicio). Si una fuente da varios
/// archivos por resultado, <see cref="RutaArchivos"/> señala su lista (o un objeto cuyos valores son los archivos) y las
/// rutas <c>RutaArchivo…</c> son relativas a cada archivo; entonces <see cref="RutaImagen"/>, <see cref="RutaAncho"/> y
/// <see cref="RutaAlto"/> no se usan. <see cref="RutaDuracion"/> va en segundos.
/// </para>
/// </summary>
public sealed record FuenteBusqueda(
    string Nombre,
    TipoFuente Tipo,
    bool Activa,
    string PlantillaExacta,
    string PlantillaMayor,
    string PlantillaSinResolucion,
    IReadOnlyDictionary<string, string> Cabeceras,
    string RutaResultados,
    string RutaMiniatura,
    string RutaImagen,
    string RutaAncho,
    string RutaAlto,
    string RutaFicha,
    string RutaUltimaPagina,
    ContenidoFuente Contenido = ContenidoFuente.Imagenes,
    string Clave = "",
    string RutaArchivos = "",
    string RutaArchivoUrl = "",
    string RutaArchivoAncho = "",
    string RutaArchivoAlto = "",
    string RutaDuracion = "");

/// <summary>Las fuentes que vienen de serie.</summary>
public static class FuentesPredeterminadas
{
    private const string BaseWallhaven = "https://wallhaven.cc/api/v1/search?q={texto}&purity=100&sorting=relevance&page={pagina}";

    public static IReadOnlyList<FuenteBusqueda> Todas() =>
    [
        // Sin clave: solo contenido apto para todos (purity=100). Unas 45 peticiones por minuto como máximo.
        new FuenteBusqueda("Wallhaven", TipoFuente.Api, true,
            PlantillaExacta: BaseWallhaven + "&resolutions={ancho}x{alto}",
            PlantillaMayor: BaseWallhaven + "&atleast={ancho}x{alto}&ratios={proporcion}",
            PlantillaSinResolucion: "",
            Cabeceras: new Dictionary<string, string>(),
            RutaResultados: "data", RutaMiniatura: "thumbs.large", RutaImagen: "path",
            RutaAncho: "dimension_x", RutaAlto: "dimension_y", RutaFicha: "url", RutaUltimaPagina: "meta.last_page"),
        new FuenteBusqueda("Google Imágenes", TipoFuente.Web, true,
            PlantillaExacta: "https://www.google.com/search?udm=2&q={texto}+imagesize:{ancho}x{alto}",
            PlantillaMayor: "", PlantillaSinResolucion: "",
            Cabeceras: new Dictionary<string, string>(),
            RutaResultados: "", RutaMiniatura: "", RutaImagen: "", RutaAncho: "", RutaAlto: "", RutaFicha: "", RutaUltimaPagina: ""),
        .. Videos(),
    ];

    private const string BasePixabay =
        "https://pixabay.com/api/videos/?key={clave}&q={texto}&safesearch=true&per_page=50&page={pagina}";

    /// <summary>
    /// Fondos animados: dos webs sin clave y las APIs de las mismas, que piden una clave personal gratuita. Pixabay da
    /// sus archivos en un objeto (large, medium…; «large» vacío si no hay 4K) y filtra por tamaño mínimo; Pexels, en
    /// una lista (con uno HLS sin tamaño) y la clave va en la cabecera Authorization.
    /// </summary>
    public static IReadOnlyList<FuenteBusqueda> Videos() =>
    [
        Web("Pexels (vídeos)", "https://www.pexels.com/search/videos/{texto}/?orientation=landscape"),
        Web("Pixabay (vídeos)", "https://pixabay.com/videos/search/{texto}/"),
        new FuenteBusqueda("Pixabay (vídeos)", TipoFuente.Api, true,
            PlantillaExacta: BasePixabay + "&min_width={ancho}&min_height={alto}",
            PlantillaMayor: "", PlantillaSinResolucion: BasePixabay,
            Cabeceras: new Dictionary<string, string>(),
            RutaResultados: "hits", RutaMiniatura: "videos.medium.thumbnail", RutaImagen: "", RutaAncho: "", RutaAlto: "",
            RutaFicha: "pageURL", RutaUltimaPagina: "", Contenido: ContenidoFuente.Videos,
            RutaArchivos: "videos", RutaArchivoUrl: "url", RutaArchivoAncho: "width", RutaArchivoAlto: "height", RutaDuracion: "duration"),
        new FuenteBusqueda("Pexels (vídeos)", TipoFuente.Api, true,
            PlantillaExacta: "https://api.pexels.com/videos/search?query={texto}&orientation=landscape&per_page=40&page={pagina}",
            PlantillaMayor: "", PlantillaSinResolucion: "",
            Cabeceras: new Dictionary<string, string> { ["Authorization"] = "{clave}" },
            RutaResultados: "videos", RutaMiniatura: "image", RutaImagen: "", RutaAncho: "", RutaAlto: "",
            RutaFicha: "url", RutaUltimaPagina: "", Contenido: ContenidoFuente.Videos,
            RutaArchivos: "video_files", RutaArchivoUrl: "link", RutaArchivoAncho: "width", RutaArchivoAlto: "height", RutaDuracion: "duration"),
    ];

    private static FuenteBusqueda Web(string nombre, string direccion) =>
        new(nombre, TipoFuente.Web, true, PlantillaExacta: direccion, PlantillaMayor: "", PlantillaSinResolucion: "",
            Cabeceras: new Dictionary<string, string>(),
            RutaResultados: "", RutaMiniatura: "", RutaImagen: "", RutaAncho: "", RutaAlto: "", RutaFicha: "", RutaUltimaPagina: "",
            Contenido: ContenidoFuente.Videos);
}
