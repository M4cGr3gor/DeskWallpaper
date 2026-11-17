using DeskWallpaper.Nucleo;
using SkiaSharp;

namespace DeskWallpaper.Pruebas;

public class PaletaColorPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    public void Dispose() => _img.Dispose();

    [Theory]
    [InlineData(255, 0, 0, ColorNombre.Rojo)]
    [InlineData(0, 0, 255, ColorNombre.Azul)]
    [InlineData(20, 20, 20, ColorNombre.Negro)]
    [InlineData(250, 250, 250, ColorNombre.Blanco)]
    [InlineData(128, 128, 128, ColorNombre.Gris)]
    [InlineData(120, 70, 20, ColorNombre.Marron)]
    [InlineData(255, 140, 0, ColorNombre.Naranja)]
    [InlineData(255, 230, 0, ColorNombre.Amarillo)]
    [InlineData(0, 200, 0, ColorNombre.Verde)]
    [InlineData(0, 220, 230, ColorNombre.Cian)]
    [InlineData(140, 0, 220, ColorNombre.Morado)]
    [InlineData(240, 80, 180, ColorNombre.Rosa)]
    public void Nombrar(byte r, byte g, byte b, ColorNombre esperado) => Assert.Equal(esperado, PaletaColor.Nombrar(r, g, b));

    [Fact]
    public void Calcular_MitadRojaMitadAzul()
    {
        var ruta = _img.Franjas("ra.png", 400, 200, SKColors.Red, SKColors.Blue);
        var colores = PaletaColor.Calcular(new RenderizadorSkia().PixelesReducidos(ruta, 64));
        Assert.Equal(2, colores.Count);
        Assert.Contains(ColorNombre.Rojo, colores);
        Assert.Contains(ColorNombre.Azul, colores);
    }

    [Fact]
    public void Calcular_ColorMinoritario_NoCuenta()
    {
        // 95 % verde, 5 % rojo: el rojo no llega al 12 %.
        var ruta = _img.Franjas("verde.png", 400, 200, Enumerable.Repeat(SKColors.Lime, 19).Append(SKColors.Red).ToArray());
        Assert.Equal([ColorNombre.Verde], PaletaColor.Calcular(new RenderizadorSkia().PixelesReducidos(ruta, 64)));
    }

    [Fact]
    public void PixelesReducidos_LadoMayorLimitado()
    {
        var ruta = _img.Franjas("g.png", 400, 100, SKColors.Red);
        Assert.Equal(64 * 16 * 4, new RenderizadorSkia().PixelesReducidos(ruta, 64).Length);
    }
}
