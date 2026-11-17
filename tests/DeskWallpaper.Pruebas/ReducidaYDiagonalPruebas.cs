using DeskWallpaper.Nucleo;
using SkiaSharp;

namespace DeskWallpaper.Pruebas;

public class ReducidaYDiagonalPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly RenderizadorSkia _r = new();

    public void Dispose() => _img.Dispose();

    private static (int Ancho, int Alto) TamanoPng(byte[] png)
    {
        using var bmp = SKBitmap.Decode(png);
        return (bmp.Width, bmp.Height);
    }

    [Fact]
    public void Reducida_LadoMayorLimitado()
    {
        var ruta = _img.Franjas("grande.png", 4000, 1000, SKColors.Red, SKColors.Blue);
        var (png, original) = _r.Reducida(ruta, 1600);
        Assert.Equal((1600, 400), TamanoPng(png));
        Assert.Equal(new SizePx(4000, 1000), original);
    }

    [Fact]
    public void Reducida_PequenaNoSeAmplia()
    {
        var ruta = _img.Franjas("pequena.png", 400, 100, SKColors.Red);
        Assert.Equal((400, 100), TamanoPng(_r.Reducida(ruta, 1600).Png));
    }

    [Fact]
    public void Reducida_RespetaExif()
    {
        var jpg = _img.Franjas("base.jpg", 400, 100, SKColors.Red, SKColors.Blue);
        var girada = _img.ConOrientacion(jpg, "girada.jpg", 6);
        var (png, original) = _r.Reducida(girada, 1600);
        var (ancho, alto) = TamanoPng(png);
        Assert.True(alto > ancho);
        Assert.Equal(new SizePx(100, 400), original);
    }

    [Fact]
    public void Reducida_NoImagen_LanzaExcepcionImagen()
    {
        Assert.Throws<ExcepcionImagen>(() => _r.Reducida(_img.Texto("x.jpg", "texto"), 1600));
    }

    [Fact]
    public void DesdeDiagonal_14Pulgadas1920x1200()
    {
        var t = TamanoPantalla.DesdeDiagonal(14, 1920, 1200);
        Assert.Equal(1.6, t.Ancho / t.Alto, 9);
        Assert.Equal(355.6, Math.Sqrt(t.Ancho * t.Ancho + t.Alto * t.Alto), 2);
    }

    [Fact]
    public void DiagonalPulgadas_IdaYVuelta()
    {
        Assert.Equal(27.0, TamanoPantalla.DiagonalPulgadas(TamanoPantalla.DesdeDiagonal(27, 1920, 1080)), 9);
    }
}
