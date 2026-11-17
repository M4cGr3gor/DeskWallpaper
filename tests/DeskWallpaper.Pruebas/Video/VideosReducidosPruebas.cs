using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas.Video;

/// <summary>Reductor de prueba: escribe unos bytes en lugar de codificar; puede fallar o cancelarse.</summary>
internal sealed class ReductorFalso : IReductorVideo
{
    public List<(string Entrada, SizePx Tamano, TimeSpan Duracion)> Llamadas { get; } = [];
    public bool Fallar { get; set; }
    public bool Cancelar { get; set; }

    public void Reducir(string entrada, string salida, SizePx tamano, TimeSpan duracion, Action<double>? progreso,
                        CancellationToken ct)
    {
        Llamadas.Add((entrada, tamano, duracion));
        File.WriteAllText(salida, "a medias");
        progreso?.Invoke(0.5);
        if (Cancelar) throw new OperationCanceledException(ct);
        if (Fallar) throw new ExcepcionImagen("El codificador ha fallado");
        File.WriteAllText(salida, $"reducido {tamano.Ancho}x{tamano.Alto}");
        progreso?.Invoke(1);
    }
}

/// <summary>Reducir los vídeos más grandes que sus monitores.</summary>
public class VideosReducidosPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly ReductorFalso _reductor = new();
    private readonly string _datos;

    public VideosReducidosPruebas() => _datos = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "datos")).FullName;

    public void Dispose() => _img.Dispose();

    private VideosReducidos Servicio(int ancho = 3840, int alto = 2160) =>
        new(_datos, new FotogramasVideo(_datos, new LectorVideoFalso(ancho, alto)), _reductor);

    private string Video(string nombre)
    {
        var ruta = Path.Combine(_img.Carpeta, nombre);
        File.WriteAllText(ruta, "vídeo original " + nombre);
        return ruta;
    }

    private static ZonaAnimada EnM1(string video) => new(video, [new PantallaAnimada(M1.Id, 0, 0, 1, 1)]);

    private static ConfigAnimado Config(params ZonaAnimada[] zonas) => new(30, true, false, zonas);

    [Fact]
    public void TamanoReducido_4KEnUnMonitorDe1080_SeReduceA1080()
    {
        Assert.Equal(new SizePx(1920, 1080), VideosReducidos.TamanoReducido(new SizePx(3840, 2160), EnM1("v"), Usuario));
    }

    [Fact]
    public void TamanoReducido_4KRecortadoEnDosMonitores_NoSeReduce()
    {
        // Un 16:9 en la zona 2+1 (32:9): cada monitor ve media anchura y media altura del vídeo, a resolución completa.
        var zona = new ZonaAnimada("v", [new PantallaAnimada(M2.Id, 0, 0.25, 0.5, 0.75), new PantallaAnimada(M1.Id, 0.5, 0.25, 1, 0.75)]);
        Assert.Null(VideosReducidos.TamanoReducido(new SizePx(3840, 2160), zona, Usuario));
    }

    [Fact]
    public void TamanoReducido_PocoMasGrandeQueElMonitor_NoSeReduce()
    {
        Assert.Null(VideosReducidos.TamanoReducido(new SizePx(2048, 1152), EnM1("v"), Usuario));   // un 7 % más
        Assert.Null(VideosReducidos.TamanoReducido(new SizePx(1920, 1080), EnM1("v"), Usuario));
    }

    [Fact]
    public void TamanoReducido_LadosImpares_SalenParesYNoMenoresQueElMonitor()
    {
        var t = VideosReducidos.TamanoReducido(new SizePx(3841, 2161), EnM1("v"), Usuario)!.Value;
        Assert.True(t.Ancho % 2 == 0 && t.Alto % 2 == 0);
        Assert.True(t.Ancho >= 1920 && t.Alto >= 1080);
        Assert.True(t.Ancho <= 1922 && t.Alto <= 1082);
    }

    [Fact]
    public void TamanoReducido_MonitorQueNoEstaEnLaDisposicion_NoSeReduce()
    {
        var zona = new ZonaAnimada("v", [new PantallaAnimada(@"\\?\DISPLAY#OTRO", 0, 0, 1, 1)]);
        Assert.Null(VideosReducidos.TamanoReducido(new SizePx(3840, 2160), zona, Usuario));
    }

    [Fact]
    public void Preparar_VideoGrande_UsaLaCopiaReducidaConElMismoRecorte()
    {
        var video = Video("grande.mp4");
        var s = Servicio();
        var avisos = new List<string>();

        Assert.Equal(1, s.Pendientes(Config(EnM1(video)), Usuario));
        var c = s.Preparar(Config(EnM1(video)), Usuario, avisos, null, CancellationToken.None);

        var zona = Assert.Single(c.Zonas);
        Assert.StartsWith(s.Carpeta, zona.Video, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("reducido 1920x1080", File.ReadAllText(zona.Video));
        Assert.Equal(EnM1(video).Pantallas, zona.Pantallas);
        Assert.Equal((video, new SizePx(1920, 1080), TimeSpan.FromSeconds(20)), Assert.Single(_reductor.Llamadas));
        Assert.Equal("vídeo original grande.mp4", File.ReadAllText(video));   // el original no se toca
        Assert.Empty(avisos);
    }

    [Fact]
    public void Preparar_SegundaVez_UsaLaCacheSinReducirOtraVez()
    {
        var video = Video("grande.mp4");
        var s = Servicio();
        var primera = s.Preparar(Config(EnM1(video)), Usuario, [], null, CancellationToken.None);

        Assert.Equal(0, s.Pendientes(Config(EnM1(video)), Usuario));
        var segunda = s.Preparar(Config(EnM1(video)), Usuario, [], null, CancellationToken.None);

        Assert.Equal(primera.Zonas[0].Video, segunda.Zonas[0].Video);
        Assert.Single(_reductor.Llamadas);
    }

    [Fact]
    public void Preparar_VideoQueYaCabe_NoSeToca()
    {
        var video = Video("pequeno.mp4");
        var c = Servicio(1920, 1080).Preparar(Config(EnM1(video)), Usuario, [], null, CancellationToken.None);

        Assert.Equal(video, c.Zonas[0].Video);
        Assert.Empty(_reductor.Llamadas);
    }

    [Fact]
    public void Preparar_FallaLaReduccion_UsaElOriginalYAvisaSinDejarArchivos()
    {
        var video = Video("grande.mp4");
        var s = Servicio();
        _reductor.Fallar = true;
        var avisos = new List<string>();

        var c = s.Preparar(Config(EnM1(video)), Usuario, avisos, null, CancellationToken.None);

        Assert.Equal(video, c.Zonas[0].Video);
        Assert.Contains("grande.mp4", Assert.Single(avisos));
        Assert.Empty(Directory.EnumerateFiles(s.Carpeta));
    }

    [Fact]
    public void Preparar_Cancelado_LanzaYNoDejaArchivos()
    {
        var video = Video("grande.mp4");
        var s = Servicio();
        _reductor.Cancelar = true;

        Assert.Throws<OperationCanceledException>(() => s.Preparar(Config(EnM1(video)), Usuario, [], null, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(s.Carpeta));
    }

    [Fact]
    public void Preparar_InformaDelProgresoDeCadaVideo()
    {
        var a = Video("a.mp4");
        var b = Video("b.mp4");
        var progreso = new List<ProgresoReduccion>();

        Servicio().Preparar(Config(EnM1(a), EnM1(b)), Usuario, [], progreso.Add, CancellationToken.None);

        Assert.Contains(new ProgresoReduccion(1, 2, "a.mp4", 0.5), progreso);
        Assert.Contains(new ProgresoReduccion(2, 2, "b.mp4", 1), progreso);
    }

    [Fact]
    public void Recortar_BorraLosMasAntiguosPeroNuncaLosQueEstanEnUso()
    {
        var s = Servicio();
        Directory.CreateDirectory(s.Carpeta);
        string Copia(string nombre, int diasAtras)
        {
            var ruta = Path.Combine(s.Carpeta, nombre);
            File.WriteAllBytes(ruta, new byte[1000]);
            File.SetLastWriteTimeUtc(ruta, DateTime.UtcNow.AddDays(-diasAtras));
            return ruta;
        }
        var enUsoViejo = Copia("a_1920x1080.mp4", 30);
        var viejo = Copia("b_1920x1080.mp4", 20);
        var nuevo = Copia("c_1920x1080.mp4", 1);

        s.Recortar([enUsoViejo], topeBytes: 2000);

        Assert.True(File.Exists(enUsoViejo));
        Assert.False(File.Exists(viejo));
        Assert.True(File.Exists(nuevo));
    }

    [Fact]
    public void Opciones_ReducirVideos_ActivadaPorDefecto()
    {
        Assert.True(new OpcionesReproduccion().ReducirVideos);
    }
}
