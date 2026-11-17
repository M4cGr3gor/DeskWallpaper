using DeskWallpaper.Nucleo;
using SkiaSharp;

namespace DeskWallpaper.Pruebas;

public class RenderizadorSkiaPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly RenderizadorSkia _r = new();

    public void Dispose() => _img.Dispose();

    [Fact]
    public void Renderiza_MitadDerecha()
    {
        var origen = _img.Franjas("rojo azul.png", 400, 100, SKColors.Red, SKColors.Blue);
        var destino = Path.Combine(_img.Carpeta, "salida.png");
        _r.Renderizar(origen, new RectD(200, 0, 200, 100), new SizePx(50, 25), destino);
        Assert.Equal((50, 25), ImagenesPrueba.Tamano(destino));
        Assert.True(ImagenesPrueba.EsAzul(ImagenesPrueba.Pixel(destino, 25, 12)));
    }

    [Fact]
    public void Renderiza_TamanoExacto()
    {
        var origen = _img.Franjas("Batman & Joker ñ.jpg", 640, 400, SKColors.Green);
        var destino = Path.Combine(_img.Carpeta, "salida grande.png");
        _r.Renderizar(origen, new RectD(0, 0, 640, 400), new SizePx(1920, 1200), destino);
        Assert.Equal((1920, 1200), ImagenesPrueba.Tamano(destino));
    }

    [Fact]
    public void Renderiza_ReduccionGrande_ConservaColores()
    {
        var origen = _img.Franjas("grande.png", 4000, 1000, SKColors.Red, SKColors.Blue);
        var destino = Path.Combine(_img.Carpeta, "pequena.png");
        _r.Renderizar(origen, new RectD(0, 0, 4000, 1000), new SizePx(100, 25), destino);
        Assert.True(ImagenesPrueba.EsRojo(ImagenesPrueba.Pixel(destino, 10, 12)));
        Assert.True(ImagenesPrueba.EsAzul(ImagenesPrueba.Pixel(destino, 90, 12)));
    }

    [Fact]
    public void LeerTamano_Correcto()
    {
        var origen = _img.Franjas("tam.png", 400, 100, SKColors.Red);
        Assert.Equal(new SizePx(400, 100), _r.LeerTamano(origen));
    }

    [Fact]
    public void OrientacionExif_SeRespeta()
    {
        // Orientación 6: hay que girar 90° a la derecha para verla derecha → la franja izquierda (roja) queda arriba.
        var jpg = _img.Franjas("base.jpg", 400, 100, SKColors.Red, SKColors.Blue);
        var girada = _img.ConOrientacion(jpg, "girada.jpg", 6);
        Assert.Equal(new SizePx(100, 400), _r.LeerTamano(girada));

        var destino = Path.Combine(_img.Carpeta, "girada.png");
        _r.Renderizar(girada, new RectD(0, 0, 100, 400), new SizePx(100, 400), destino);
        Assert.True(ImagenesPrueba.EsRojo(ImagenesPrueba.Pixel(destino, 50, 50)));
        Assert.True(ImagenesPrueba.EsAzul(ImagenesPrueba.Pixel(destino, 50, 350)));
    }

    [Fact]
    public void ArchivoNoImagen_LanzaExcepcionImagen()
    {
        var falsa = _img.Texto("no soy imagen.jpg", "hola, esto es texto");
        var ex = Assert.Throws<ExcepcionImagen>(() => _r.LeerTamano(falsa));
        Assert.Contains("no soy imagen.jpg", ex.Message);
        Assert.Throws<ExcepcionImagen>(() =>
            _r.Renderizar(falsa, new RectD(0, 0, 10, 10), new SizePx(10, 10), Path.Combine(_img.Carpeta, "x.png")));
    }

    [Fact]
    public void ArchivoInexistente_LanzaExcepcionImagen()
    {
        var ruta = Path.Combine(_img.Carpeta, "no existe.jpg");
        Assert.Throws<ExcepcionImagen>(() => _r.LeerTamano(ruta));
    }
}
