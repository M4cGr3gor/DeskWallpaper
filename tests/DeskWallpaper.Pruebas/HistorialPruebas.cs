using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class HistorialPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly byte[] _mini = [1, 2, 3];
    private static readonly DateTime T0 = new(2026, 11, 11, 10, 0, 0, DateTimeKind.Local);

    public void Dispose() => _img.Dispose();

    private string Datos => Path.Combine(_img.Carpeta, "datos");

    private static Combinacion Comb(string ruta, double zoom = 1) =>
        new("firma", [new AsignacionZona([M2.Id, M1.Id], ruta, new Encuadre(Zoom: zoom))]);

    [Fact]
    public void Registrar_Nueva_Y_Listar()
    {
        var h = new Historial(Datos);
        var e = h.Registrar(Comb(@"D:\a.jpg"), _mini, T0);

        var lista = h.Listar();
        var unica = Assert.Single(lista);
        Assert.Equal(e.Id, unica.Id);
        Assert.Equal(@"D:\a.jpg", unica.Combinacion.Zonas[0].RutaImagen);
        Assert.Equal([M2.Id, M1.Id], unica.Combinacion.Zonas[0].MonitorIds);
        Assert.Equal(_mini, unica.Miniatura);
        Assert.Equal(1, unica.Usos);
        Assert.True(File.Exists(Path.Combine(Datos, "biblioteca.db")));
    }

    [Fact]
    public void Registrar_Repetida_ActualizaUsos()
    {
        var h = new Historial(Datos);
        h.Registrar(Comb(@"D:\a.jpg"), _mini, T0);
        h.Registrar(Comb(@"D:\A.JPG"), _mini, T0.AddDays(3)); // misma ruta con otras mayúsculas

        var unica = Assert.Single(h.Listar());
        Assert.Equal(2, unica.Usos);
        Assert.Equal(T0.AddDays(3), unica.UltimoUso);
        Assert.Equal(T0, unica.Creada);
    }

    [Fact]
    public void Registrar_EncuadreDistinto_EsOtraEntrada()
    {
        var h = new Historial(Datos);
        h.Registrar(Comb(@"D:\a.jpg"), _mini, T0);
        h.Registrar(Comb(@"D:\a.jpg", zoom: 2), _mini, T0.AddMinutes(1));
        Assert.Equal(2, h.Listar().Count);
    }

    [Fact]
    public void Listar_MasRecientePrimero()
    {
        var h = new Historial(Datos);
        h.Registrar(Comb(@"D:\vieja.jpg"), _mini, T0);
        h.Registrar(Comb(@"D:\nueva.jpg"), _mini, T0.AddDays(1));
        Assert.Equal(@"D:\nueva.jpg", h.Listar()[0].Combinacion.Zonas[0].RutaImagen);
    }

    [Fact]
    public void Tope_BorraLaMasAntigua()
    {
        var h = new Historial(Datos, maximo: 3);
        for (var i = 0; i < 4; i++) h.Registrar(Comb($@"D:\{i}.jpg"), _mini, T0.AddDays(i));

        var rutas = h.Listar().Select(e => e.Combinacion.Zonas[0].RutaImagen).ToList();
        Assert.Equal([@"D:\3.jpg", @"D:\2.jpg", @"D:\1.jpg"], rutas);
    }

    [Fact]
    public void Borrar_QuitaLaEntrada()
    {
        var h = new Historial(Datos);
        var e = h.Registrar(Comb(@"D:\a.jpg"), _mini, T0);
        h.Borrar(e.Id);
        Assert.Empty(h.Listar());
    }

    [Fact]
    public void ImagenesQueFaltan_DetectaBorradas()
    {
        var existe = _img.Franjas("existe.png", 10, 10, SKColors.Red);
        var c = new Combinacion("f",
        [
            new AsignacionZona([M2.Id, M1.Id], existe, new Encuadre()),
            new AsignacionZona([M3.Id], Path.Combine(_img.Carpeta, "borrada.jpg"), new Encuadre()),
        ]);
        Assert.Equal([Path.Combine(_img.Carpeta, "borrada.jpg")], Historial.ImagenesQueFaltan(c));
    }

    [Fact]
    public void ComponerMiniatura_AnchoFijo()
    {
        var pano = _img.Franjas("pano.png", 3840, 1080, SKColors.Red, SKColors.Blue);
        var m3 = _img.Franjas("m3.png", 1920, 1200, SKColors.Green);
        var l = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());
        var c = new Combinacion("f",
        [
            new AsignacionZona([M2.Id, M1.Id], pano, new Encuadre()),
            new AsignacionZona([M3.Id], m3, new Encuadre()),
        ]);

        var png = new RenderizadorSkia().ComponerMiniatura(c, Usuario, l, 320);

        using var bmp = SKBitmap.Decode(png);
        Assert.Equal(320, bmp.Width);
        var escala = 320 / 1200.0; // ancho físico total sin marcos: 600 + 600 (M3 no sobresale)
        SKColor En(RectMm r) => bmp.GetPixel((int)((r.X + r.Ancho / 2) * escala), (int)((r.Y + r.Alto / 2) * escala));
        Assert.True(ImagenesPrueba.EsRojo(En(l.Monitor(M2.Id))));
        Assert.True(ImagenesPrueba.EsAzul(En(l.Monitor(M1.Id))));
    }
}
