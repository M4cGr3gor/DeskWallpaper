using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

/// <summary>Sustituye a IDesktopWallpaper: nunca toca el escritorio real.</summary>
internal sealed class ServicioFondosFalso(IEnumerable<string> ids) : IServicioFondos
{
    public Dictionary<string, string> Fondos { get; } = ids.ToDictionary(i => i, _ => "", StringComparer.OrdinalIgnoreCase);
    public PosicionFondo Posicion { get; private set; } = PosicionFondo.Centrar;
    public int LlamadasPonerFondo { get; private set; }

    /// <summary>Monitores apagados: Windows conserva su fondo pero no los da como activos.</summary>
    public HashSet<string> Inactivos { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Si no es null, la primera llamada a PonerFondo para este monitor falla (como un error COM).</summary>
    public string? FallarUnaVezEn { get; set; }

    public IReadOnlyList<string> MonitorIds() => Fondos.Keys.Where(k => !Inactivos.Contains(k)).ToList();
    public string? ObtenerFondo(string monitorId) => Fondos.GetValueOrDefault(monitorId);

    public void PonerFondo(string monitorId, string ruta)
    {
        LlamadasPonerFondo++;
        if (FallarUnaVezEn is not null && string.Equals(FallarUnaVezEn, monitorId, StringComparison.OrdinalIgnoreCase))
        {
            FallarUnaVezEn = null;
            throw new InvalidOperationException("Error simulado de IDesktopWallpaper");
        }
        Fondos[monitorId] = ruta;
    }
    public PosicionFondo ObtenerPosicion() => Posicion;
    public void PonerPosicion(PosicionFondo posicion) => Posicion = posicion;

    /// <summary>Modo "color sólido" de Windows: quita la imagen de todos los monitores.</summary>
    public void QuitarImagenes()
    {
        LlamadasQuitarImagenes++;
        foreach (var id in Fondos.Keys.ToList()) Fondos[id] = "";
    }
    public int LlamadasQuitarImagenes { get; private set; }

    /// <summary>Color de fondo (COLORREF) que se ve en los monitores sin imagen.</summary>
    public uint Color { get; set; }
    public uint ObtenerColorFondo() => Color;
    public void PonerColorFondo(uint color) => Color = color;
}

public class AplicadorFondosPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly ServicioFondosFalso _servicio = new([M1.Id, M2.Id, M3.Id]);
    private readonly string _datos;
    private readonly AplicadorFondos _aplicador;
    private readonly LienzoFisico _lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());

    public AplicadorFondosPruebas()
    {
        _datos = Path.Combine(_img.Carpeta, "datos");
        _aplicador = new AplicadorFondos(_servicio, new RenderizadorSkia(), _datos);
    }

    public void Dispose() => _img.Dispose();

    private string CarpetaCache => Path.Combine(_datos, "cache", "fondos");

    private static Combinacion Comb(string rutaPanoramica, string rutaM3) => new(FirmaDisposicion.Calcular(Usuario),
    [
        new AsignacionZona([M2.Id, M1.Id], rutaPanoramica, new Encuadre()),
        new AsignacionZona([M3.Id], rutaM3, new Encuadre()),
    ]);

    [Fact]
    public void Usuario_GeneraTresArchivosDeResolucionNativa()
    {
        var pano = _img.Franjas("pano.jpg", 384, 108, SKColors.Red, SKColors.Blue);
        var m3 = _img.Franjas("m3.jpg", 192, 120, SKColors.Green);

        var r = _aplicador.Aplicar(Comb(pano, m3), Usuario, _lienzo);

        Assert.True(r.Exito, string.Join("; ", r.Errores));
        Assert.Equal(3, _servicio.LlamadasPonerFondo);
        Assert.Equal(PosicionFondo.Rellenar, _servicio.Posicion);
        Assert.Equal((1920, 1080), ImagenesPrueba.Tamano(_servicio.Fondos[M1.Id]));
        Assert.Equal((1920, 1080), ImagenesPrueba.Tamano(_servicio.Fondos[M2.Id]));
        Assert.Equal((1920, 1200), ImagenesPrueba.Tamano(_servicio.Fondos[M3.Id]));
        Assert.All(_servicio.Fondos.Values, ruta => Assert.StartsWith(CarpetaCache, ruta));
    }

    [Fact]
    public void Panoramica_MitadIzquierdaAlMonitor2()
    {
        var pano = _img.Franjas("pano.png", 3840, 1080, SKColors.Red, SKColors.Blue);
        var m3 = _img.Franjas("m3.png", 1920, 1200, SKColors.Green);

        var r = _aplicador.Aplicar(Comb(pano, m3), Usuario, _lienzo);

        Assert.True(r.Exito, string.Join("; ", r.Errores));
        Assert.True(ImagenesPrueba.EsRojo(ImagenesPrueba.Pixel(_servicio.Fondos[M2.Id], 960, 540)));
        Assert.True(ImagenesPrueba.EsAzul(ImagenesPrueba.Pixel(_servicio.Fondos[M1.Id], 960, 540)));
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void ImagenCorrupta_NoCambiaNada()
    {
        var pano = _img.Franjas("pano.png", 3840, 1080, SKColors.Red);
        var corrupta = _img.Texto("corrupta.jpg", "no es una imagen");

        var r = _aplicador.Aplicar(Comb(pano, corrupta), Usuario, _lienzo);

        Assert.False(r.Exito);
        Assert.Contains(r.Errores, e => e.Contains("corrupta.jpg"));
        Assert.Equal(0, _servicio.LlamadasPonerFondo);
        Assert.False(Directory.Exists(CarpetaCache) && Directory.EnumerateFiles(CarpetaCache).Any());
    }

    [Fact]
    public void MonitorDesconectado_ErrorSinAplicar()
    {
        var pano = _img.Franjas("pano.png", 3840, 1080, SKColors.Red);
        var m3 = _img.Franjas("m3.png", 1920, 1200, SKColors.Green);
        var sinM3 = new Disposicion([M1, M2]);

        var r = _aplicador.Aplicar(Comb(pano, m3), sinM3, LienzoFisico.DesdeDisposicion(sinM3, new Dictionary<string, double>()));

        Assert.False(r.Exito);
        Assert.Contains(r.Errores, e => e.Contains("ya no está conectado"));
        Assert.Equal(0, _servicio.LlamadasPonerFondo);
    }

    [Fact]
    public void ImagenPequena_AvisoCalidad()
    {
        var pano = _img.Franjas("pequena.png", 1920, 713, SKColors.Red, SKColors.Blue);
        var m3 = _img.Franjas("m3.png", 1920, 1200, SKColors.Green);

        var r = _aplicador.Aplicar(Comb(pano, m3), Usuario, _lienzo);

        Assert.True(r.Exito);
        Assert.Contains(r.Avisos, a => a.MonitorId == M1.Id && a.Factor > 1.05);
        Assert.Contains(r.Avisos, a => a.MonitorId == M2.Id && a.Factor > 1.05);
        Assert.DoesNotContain(r.Avisos, a => a.MonitorId == M3.Id);
    }

    [Fact]
    public void RutaConAcentosYAmpersand()
    {
        Directory.CreateDirectory(Path.Combine(_img.Carpeta, "Dual monitor"));
        var pano = _img.Franjas(Path.Combine("Dual monitor", "Batman & Joker ñ.jpg"), 3840, 1080, SKColors.Red, SKColors.Blue);
        var m3 = _img.Franjas("m3 á.jpg", 1920, 1200, SKColors.Green);

        var r = _aplicador.Aplicar(Comb(pano, m3), Usuario, _lienzo);

        Assert.True(r.Exito, string.Join("; ", r.Errores));
        Assert.Equal(3, _servicio.LlamadasPonerFondo);
    }

    [Fact]
    public void Limpieza_ConservaEnUsoYProtegidas()
    {
        var pano = _img.Franjas("pano.png", 384, 108, SKColors.Red);
        var m3 = _img.Franjas("m3.png", 192, 120, SKColors.Green);

        _aplicador.Aplicar(Comb(pano, m3), Usuario, _lienzo);
        var protegida = _servicio.Fondos[M3.Id];
        _aplicador.Aplicar(Comb(pano, m3), Usuario, _lienzo, [protegida]);

        var esperados = _servicio.Fondos.Values.Append(protegida).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var presentes = Directory.GetFiles(CarpetaCache).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(4, presentes.Count);
        Assert.True(esperados.SetEquals(presentes));
    }

    [Fact]
    public void AplicarSoloArriba_Monitor3Apagado_ConservaSuFondo()
    {
        // Tras aplicar solo la zona 2+1, el archivo del fondo del monitor 3 no existía.
        // Un monitor apagado no se puede poner en negro: conserva su fondo y el archivo no se borra.
        var pano = _img.Franjas("pano.png", 384, 108, SKColors.Red);
        var m3 = _img.Franjas("m3.png", 192, 120, SKColors.Green);
        _aplicador.Aplicar(Comb(pano, m3), Usuario, _lienzo);
        var fondoM3 = _servicio.Fondos[M3.Id];
        _servicio.Inactivos.Add(M3.Id);

        var soloArriba = new Combinacion("x", [new AsignacionZona([M2.Id, M1.Id], pano, new Encuadre())]);
        Assert.True(_aplicador.Aplicar(soloArriba, Usuario, _lienzo).Exito);

        Assert.Equal(fondoM3, _servicio.Fondos[M3.Id]);
        Assert.True(File.Exists(fondoM3));
    }

    [Fact]
    public void MonitorSinImagen_QuedaEnNegro()
    {
        // Un monitor que se deja vacío en el lienzo se pone en negro al aplicar.
        var pano = _img.Franjas("pano.png", 384, 108, SKColors.Red);
        var m3 = _img.Franjas("m3.png", 192, 120, SKColors.Green);
        _aplicador.Aplicar(Comb(pano, m3), Usuario, _lienzo);
        var c = new Combinacion("x", [new AsignacionZona([M2.Id, M1.Id], pano, new Encuadre())]);

        var r = _aplicador.Aplicar(c, Usuario, _lienzo);

        Assert.True(r.Exito);
        var negro = _servicio.Fondos[M3.Id];
        Assert.Equal(negro, r.FondosPorMonitor[M3.Id]);
        using var bmp = SKBitmap.Decode(negro);
        Assert.Equal((1920, 1200), (bmp.Width, bmp.Height));
        foreach (var (x, y) in new[] { (0, 0), (960, 600), (1919, 1199) })
            Assert.Equal(SKColors.Black, bmp.GetPixel(x, y));
    }
}
