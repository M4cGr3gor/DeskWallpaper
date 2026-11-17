using System.Net;
using System.Text;
using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.Online;

/// <summary>Búsqueda online de fondos animados.</summary>
public class BusquedaVideosPruebas : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("dw-busqueda-videos-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    private static FuenteBusqueda Video(string nombre) =>
        FuentesPredeterminadas.Todas().Single(f => f.Nombre == nombre && f.Tipo == TipoFuente.Api);

    /// <summary>Como responde la API de vídeos de Pexels (recortado): el archivo HLS no trae tamaño.</summary>
    private const string JsonPexels = """
        { "page": 1, "videos": [
          { "id": 1, "url": "https://www.pexels.com/video/olas-1/", "image": "https://images.pexels.com/videos/1/p.jpg", "duration": 15,
            "video_files": [
              { "link": "https://videos.pexels.com/1/hd_1280_720.mp4", "width": 1280, "height": 720 },
              { "link": "https://videos.pexels.com/1/hls.m3u8", "width": null, "height": null },
              { "link": "https://videos.pexels.com/1/uhd_3840_2160.mp4", "width": 3840, "height": 2160 },
              { "link": "https://videos.pexels.com/1/hd_1920_1080.mp4", "width": 1920, "height": 1080 } ] } ] }
        """;

    /// <summary>Como responde la API de vídeos de Pixabay (recortado): sin versión 4K, «large» viene vacío.</summary>
    private const string JsonPixabay = """
        { "total": 1, "totalHits": 1, "hits": [
          { "id": 7, "pageURL": "https://pixabay.com/videos/nubes-7/", "duration": 21,
            "videos": {
              "large": { "url": "", "width": 0, "height": 0, "thumbnail": "" },
              "medium": { "url": "https://cdn.pixabay.com/video/7/medium.mp4", "width": 1920, "height": 1080, "thumbnail": "https://cdn.pixabay.com/video/7/medium.jpg" },
              "small": { "url": "https://cdn.pixabay.com/video/7/small.mp4", "width": 1280, "height": 720, "thumbnail": "https://cdn.pixabay.com/video/7/small.jpg" } } } ] }
        """;

    [Fact]
    public void Plantilla_SustituyeLaClaveCodificada()
    {
        var u = PlantillaUrl.Rellenar("https://pixabay.com/api/videos/?key={clave}&q={texto}", "mar", null, 1, "ab&c");
        Assert.Equal("https://pixabay.com/api/videos/?key=ab%26c&q=mar", u.AbsoluteUri);
    }

    [Theory]
    [InlineData(3840, 1080, 3840, 1080, Encaje.Exacta)]
    [InlineData(5120, 1440, 3840, 1080, Encaje.Mayor)]
    [InlineData(3840, 2160, 3840, 1080, Encaje.Recorta)]   // un 16:9 cubre la zona 2+1 recortándolo
    [InlineData(3840, 2160, 1920, 1200, Encaje.Recorta)]
    [InlineData(1920, 1080, 3840, 1080, null)]             // habría que ampliarlo al doble
    [InlineData(1920, 1080, 1920, 1200, null)]             // le faltan 120 filas
    public void Encaje_DeVideo_CubreLaZonaSinAmpliar(int va, int vh, int za, int zh, Encaje? esperado)
    {
        Assert.Equal(esperado, EncajeResolucion.DeVideo(new SizePx(va, vh), new SizePx(za, zh)));
    }

    [Fact]
    public void Lector_Pexels_ArchivosConTamanoYDuracion()
    {
        var p = LectorRespuestaJson.Leer(JsonPexels, Video("Pexels (vídeos)"));

        var r = Assert.Single(p.Resultados);
        Assert.Equal(3, r.Archivos!.Count);   // el HLS sin tamaño se salta
        Assert.Equal(TimeSpan.FromSeconds(15), r.Duracion);
        Assert.Equal(new SizePx(3840, 2160), r.Tamano);   // el mayor
        Assert.Equal("https://images.pexels.com/videos/1/p.jpg", r.Miniatura.AbsoluteUri);
        Assert.Equal("https://www.pexels.com/video/olas-1/", r.Ficha!.AbsoluteUri);
    }

    [Fact]
    public void Lector_Pixabay_ObjetoDeArchivosSinLosVacios()
    {
        var p = LectorRespuestaJson.Leer(JsonPixabay, Video("Pixabay (vídeos)"));

        var r = Assert.Single(p.Resultados);
        Assert.Equal([new SizePx(1920, 1080), new SizePx(1280, 720)], r.Archivos!.Select(a => a.Tamano).ToList());
        Assert.Equal(TimeSpan.FromSeconds(21), r.Duracion);
        Assert.Equal("https://pixabay.com/videos/nubes-7/", r.Ficha!.AbsoluteUri);
    }

    [Fact]
    public async Task Buscador_Video_EligeElArchivoMasPequenoQueCubreYNoRepite()
    {
        var http = new ManejadorFalso { PorDefecto = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonPexels, Encoding.UTF8, "application/json") } };
        var buscador = new BuscadorOnline(http, (_, _) => Task.CompletedTask);
        var f = Video("Pexels (vídeos)") with { Clave = "abc" };

        var solo1080 = await buscador.BuscarAsync(f, "olas", [new SizePx(1920, 1080)], 1, CancellationToken.None);
        var dos = await buscador.BuscarAsync(f, "olas", [new SizePx(1920, 1200), new SizePx(1920, 1080)], 1, CancellationToken.None);

        var a = Assert.Single(solo1080.Resultados);
        Assert.Equal("https://videos.pexels.com/1/hd_1920_1080.mp4", a.Resultado.Imagen.AbsoluteUri);
        Assert.Equal(Encaje.Exacta, a.Encaje);
        var b = Assert.Single(dos.Resultados);   // el mismo vídeo encaja en las dos casillas: sale una vez
        Assert.Equal("https://videos.pexels.com/1/uhd_3840_2160.mp4", b.Resultado.Imagen.AbsoluteUri);   // 1080 no cubre 1200
        Assert.Equal(new SizePx(3840, 2160), b.Resultado.Tamano);
        Assert.Equal(Encaje.Recorta, b.Encaje);
    }

    [Fact]
    public async Task Buscador_ClaveEnLaCabecera()
    {
        var http = new ManejadorFalso().Responder(HttpStatusCode.OK, JsonPexels);
        var buscador = new BuscadorOnline(http, (_, _) => Task.CompletedTask);

        await buscador.BuscarAsync(Video("Pexels (vídeos)") with { Clave = "mi-clave" }, "olas", [], 1, CancellationToken.None);

        Assert.Equal("mi-clave", Assert.Single(Assert.Single(http.Peticiones).Headers.GetValues("Authorization")));
    }

    [Fact]
    public async Task Buscador_SinClave_NoPideNadaYLoDice()
    {
        var http = new ManejadorFalso();
        var f = Video("Pixabay (vídeos)");

        Assert.True(BuscadorOnline.FaltaClave(f));
        Assert.False(BuscadorOnline.FaltaClave(f with { Clave = "x" }));
        Assert.False(BuscadorOnline.FaltaClave(FuentesPredeterminadas.Todas()[0]));   // Wallhaven no la usa
        var r = await new BuscadorOnline(http, (_, _) => Task.CompletedTask).BuscarAsync(f, "mar", [], 1, CancellationToken.None);

        Assert.Empty(http.Peticiones);
        Assert.Contains("clave", r.Error);
    }

    [Fact]
    public void Predeterminadas_DosDeCadaTipoParaVideos()
    {
        var videos = FuentesPredeterminadas.Todas().Where(f => f.Contenido == ContenidoFuente.Videos).ToList();
        Assert.Equal(2, videos.Count(f => f.Tipo == TipoFuente.Web));
        Assert.Equal(2, videos.Count(f => f.Tipo == TipoFuente.Api));
        Assert.All(FuentesPredeterminadas.Todas().Where(f => f.Contenido == ContenidoFuente.Imagenes), f => Assert.Equal("", f.Clave));
    }

    [Fact]
    public async Task Descargar_Video_GuardaConSuExtensionSinSobrescribir()
    {
        var mp4 = new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'m', (byte)'p', (byte)'4', (byte)'2', 1, 2, 3 };
        var webm = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0x82, 0x84, (byte)'w', (byte)'e', (byte)'b', (byte)'m' };
        var http = new ManejadorFalso().Responder(mp4, "video/mp4").Responder(mp4, "video/mp4").Responder(webm, "video/webm");
        var d = new DescargadorImagenes(http, new RenderizadorSkia());

        var a = await d.DescargarVideoAsync(new Uri("https://videos.pexels.com/1/olas.mp4?x=1"), _carpeta, null, CancellationToken.None);
        var b = await d.DescargarVideoAsync(new Uri("https://videos.pexels.com/1/olas.mp4"), _carpeta, null, CancellationToken.None);
        var c = await d.DescargarVideoAsync(new Uri("https://cdn.example/v/nubes"), _carpeta, null, CancellationToken.None);

        Assert.Equal("olas.mp4", Path.GetFileName(a));
        Assert.Equal("olas (2).mp4", Path.GetFileName(b));
        Assert.Equal(".webm", Path.GetExtension(c));
        Assert.Equal(mp4, File.ReadAllBytes(a));
    }

    [Fact]
    public async Task Descargar_Video_QueNoLoEs_FallaSinDejarNada()
    {
        var http = new ManejadorFalso().Responder(HttpStatusCode.OK, "<html>no</html>", "text/html");
        var d = new DescargadorImagenes(http, new RenderizadorSkia());

        await Assert.ThrowsAsync<ExcepcionDescarga>(() =>
            d.DescargarVideoAsync(new Uri("https://x.example/v.mp4"), _carpeta, null, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_carpeta));
    }

    [Fact]
    public void Configuracion_SinFuentesDeVideo_LasRecibeUnaVez()
    {
        // Una configuración de la fase 5: solo Wallhaven (editada) y Google; sin campo «Contenido».
        File.WriteAllText(Path.Combine(_carpeta, "configuracion.json"), """
            { "Version": 1, "FuentesBusqueda": [ { "Nombre": "Wallhaven", "Tipo": "Api", "Activa": false, "PlantillaExacta": "https://wallhaven.cc/x" } ] }
            """);

        var c = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal(ContenidoFuente.Imagenes, c.FuentesBusqueda![0].Contenido);
        Assert.False(c.FuentesBusqueda[0].Activa);   // lo que el usuario cambió no se toca
        Assert.Equal(4, c.FuentesBusqueda.Count(f => f.Contenido == ContenidoFuente.Videos));
        AlmacenConfiguracion.Guardar(_carpeta, c);
        Assert.Equal(5, AlmacenConfiguracion.Cargar(_carpeta).FuentesBusqueda!.Count);   // no se añaden otra vez
    }
}
