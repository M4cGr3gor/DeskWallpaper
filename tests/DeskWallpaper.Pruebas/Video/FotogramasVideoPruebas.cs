using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas.Video;

/// <summary>Fotograma de referencia de cada vídeo.</summary>
public class FotogramasVideoPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly LectorVideoFalso _lector = new();
    private readonly string _datos;

    public FotogramasVideoPruebas() => _datos = Path.Combine(_img.Carpeta, "datos");

    public void Dispose() => _img.Dispose();

    /// <summary>Un «vídeo» de prueba: bytes distintos para que cada uno tenga su huella.</summary>
    private string Video(string nombre)
    {
        var ruta = Path.Combine(_img.Carpeta, nombre);
        File.WriteAllBytes(ruta, System.Text.Encoding.UTF8.GetBytes("vídeo de prueba " + nombre));
        return ruta;
    }

    private FotogramasVideo Nuevos() => new(_datos, _lector);

    [Fact]
    public void EsVideo_PorExtension()
    {
        Assert.True(FormatosVideo.EsVideo("a.MP4"));
        Assert.True(FormatosVideo.EsVideo("b.webm"));
        Assert.False(FormatosVideo.EsVideo("c.jpg"));
    }

    [Fact]
    public void TextoDuracion_Formatos()
    {
        Assert.Equal("0:20", FormatosVideo.TextoDuracion(TimeSpan.FromSeconds(20)));
        Assert.Equal("1:05", FormatosVideo.TextoDuracion(TimeSpan.FromSeconds(65)));
        Assert.Equal("1:02:03", FormatosVideo.TextoDuracion(TimeSpan.FromSeconds(3723)));
    }

    [Fact]
    public void Preparar_LeeUnaVezYCachea()
    {
        var video = Video("a.mp4");
        var f = Nuevos();

        var (ruta1, info1) = f.Preparar(video);
        var (ruta2, info2) = Nuevos().Preparar(video);

        Assert.Equal(1, _lector.Lecturas);
        Assert.Equal(ruta1, ruta2);
        Assert.Equal(info1, info2);
        Assert.Equal(new SizePx(384, 108), info1.Tamano);
        Assert.Equal(TimeSpan.FromSeconds(20), info1.Duracion);
        var huella = Huella.Calcular(video);
        Assert.Equal(Path.Combine(f.Carpeta, huella + ".jpg"), ruta1);
        Assert.True(File.Exists(Path.Combine(f.Carpeta, huella + ".json")));
    }

    [Fact]
    public void Preparar_VideoIlegible_LanzaExcepcionImagen()
    {
        var video = Video("roto.mp4");
        var f = Nuevos();

        Assert.Throws<ExcepcionImagen>(() => f.Preparar(video));

        Assert.True(!Directory.Exists(f.Carpeta) || Directory.GetFiles(f.Carpeta).Length == 0);
    }

    [Fact]
    public void Recortar_BorraLosMasAntiguosHastaElTope()
    {
        var f = Nuevos();
        var videos = new[] { "1.mp4", "2.mp4", "3.mp4", "4.mp4" }.Select(Video).ToList();
        var fotogramas = new List<string>();
        for (var i = 0; i < videos.Count; i++)
        {
            var (ruta, _) = f.Preparar(videos[i]);
            fotogramas.Add(ruta);
            var cuando = DateTime.UtcNow.AddMinutes(-10 + i);   // del más antiguo al más reciente
            File.SetLastWriteTimeUtc(ruta, cuando);
            File.SetLastWriteTimeUtc(Path.ChangeExtension(ruta, ".json"), cuando);
        }
        var unPar = new FileInfo(fotogramas[0]).Length + new FileInfo(Path.ChangeExtension(fotogramas[0], ".json")).Length;

        f.Recortar((long)(unPar * 2.5));

        Assert.False(File.Exists(fotogramas[0]));
        Assert.False(File.Exists(fotogramas[1]));
        Assert.True(File.Exists(fotogramas[2]));
        Assert.True(File.Exists(fotogramas[3]));
        var antes = _lector.Lecturas;
        f.Preparar(videos[0]);
        Assert.Equal(antes + 1, _lector.Lecturas);
    }

    [Fact]
    public void Renderizador_LeerTamanoDeUnVideoEsElDeSuFotograma()
    {
        var r = new RenderizadorConVideos(new RenderizadorSkia(), Nuevos());
        var imagen = _img.Franjas("imagen.png", 200, 100, SKColors.Blue);

        Assert.Equal(new SizePx(384, 108), r.LeerTamano(Video("x.mp4")));
        var lecturas = _lector.Lecturas;
        Assert.Equal(new SizePx(200, 100), r.LeerTamano(imagen));
        Assert.Equal(lecturas, _lector.Lecturas);
    }

    [Fact]
    public void Renderizador_ComponerMiniatura_ConZonaDeVideo()
    {
        var r = new RenderizadorConVideos(new RenderizadorSkia(), Nuevos());
        var c = new Combinacion("x",
        [
            new AsignacionZona([M2.Id, M1.Id], Video("pano.mp4"), new Encuadre()),
            new AsignacionZona([M3.Id], _img.Franjas("m3.png", 192, 120, SKColors.Green), new Encuadre()),
        ]);
        var l = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());

        var png = r.ComponerMiniatura(c, Usuario, l, 320);

        Assert.NotEmpty(png);
        Assert.Equal(1, _lector.Lecturas);
    }
}
