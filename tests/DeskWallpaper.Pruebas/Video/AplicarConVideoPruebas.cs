using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas.Video;

/// <summary>Aplicar y probar con vídeos.</summary>
public class AplicarConVideoPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly ServicioFondosFalso _servicio = new([M1.Id, M2.Id, M3.Id]);
    private readonly LectorVideoFalso _lector = new(1920, 1080) { Color = SKColors.OrangeRed };
    private readonly ControlFalso _control = new();
    private readonly RegistroFalso _registro = new();
    private readonly string _datos;
    private readonly AplicadorFondos _aplicador;
    private readonly ServicioAnimado _animado;
    private readonly LienzoFisico _lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());

    public AplicarConVideoPruebas()
    {
        _datos = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "datos")).FullName;
        var renderizador = new RenderizadorConVideos(new RenderizadorSkia(), new FotogramasVideo(_datos, _lector));
        _aplicador = new AplicadorFondos(_servicio, renderizador, _datos);
        _animado = new ServicioAnimado(_datos, true, @"C:\x\DeskWallpaper.Animado.exe", _control, _registro);
    }

    public void Dispose() => _img.Dispose();

    private string Video(string nombre)
    {
        var ruta = Path.Combine(_img.Carpeta, nombre);
        File.WriteAllBytes(ruta, System.Text.Encoding.UTF8.GetBytes("vídeo " + nombre));
        return ruta;
    }

    private static ConfigAnimado ConVideo(string video) =>
        new(30, true, false, [new ZonaAnimada(video, [new PantallaAnimada(M1.Id, 0, 0, 1, 1)])]);

    private Combinacion SoloImagenes() => new("x",
        [new AsignacionZona([M3.Id], _img.Franjas("m3.png", 192, 120, SKColors.Green), new Encuadre())]);

    [Fact]
    public void Probar_ConVideo_VuelveAlEstadoAnteriorDelAyudante()
    {
        var video = Video("antes.mp4");
        _animado.Activar(ConVideo(video));
        var prueba = new PruebaFondos(_servicio, _aplicador, _animado);

        Assert.True(prueba.Empezar(SoloImagenes(), Usuario, _lienzo, ConfigAnimado.Vacia(new OpcionesReproduccion())).Exito);
        Assert.False(_control.Encendido);
        prueba.Volver();

        Assert.True(_control.Encendido);
        Assert.Equal(video, AlmacenAnimado.Leer(_datos)!.Zonas.Single().Video);
        Assert.Null(prueba.AnteriorAnimado);
    }

    [Fact]
    public void Probar_Mantener_DejaLoNuevo()
    {
        var nuevo = Video("nuevo.mp4");
        var prueba = new PruebaFondos(_servicio, _aplicador, _animado);
        var c = new Combinacion("x", [new AsignacionZona([M1.Id], nuevo, new Encuadre())]);

        prueba.Empezar(c, Usuario, _lienzo, ConVideo(nuevo));
        prueba.Mantener();

        Assert.True(_control.Encendido);
        Assert.Equal(nuevo, AlmacenAnimado.Leer(_datos)!.Zonas.Single().Video);
        Assert.Null(prueba.AnteriorAnimado);
        Assert.False(prueba.Volver());
    }

    [Fact]
    public void Aplicar_VideoQueNoExiste_NoCambiaNada()
    {
        var c = new Combinacion("x", [new AsignacionZona([M1.Id], Path.Combine(_img.Carpeta, "no-existe.mp4"), new Encuadre())]);

        var r = _aplicador.Aplicar(c, Usuario, _lienzo);

        Assert.False(r.Exito);
        Assert.Equal(0, _servicio.LlamadasPonerFondo);
    }

    [Fact]
    public void Aplicar_ConVideo_PoneElFotogramaComoFondoDeReserva()
    {
        var c = new Combinacion("x", [new AsignacionZona([M1.Id], Video("fondo.mp4"), new Encuadre())]);

        var r = _aplicador.Aplicar(c, Usuario, _lienzo);

        Assert.True(r.Exito);
        using var puesto = SKBitmap.Decode(_servicio.Fondos[M1.Id]);
        var centro = puesto.GetPixel(puesto.Width / 2, puesto.Height / 2);
        Assert.InRange(centro.Red, 230, 255);
        Assert.InRange(centro.Green, 50, 90);
        Assert.InRange(centro.Blue, 0, 30);
    }
}
