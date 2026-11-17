using DeskWallpaper.Nucleo;
using DeskWallpaper.Windows;

namespace DeskWallpaper.Pruebas.Video;

/// <summary>Salida del modo <c>--info</c> del ayudante.</summary>
public class LectorVideoAyudantePruebas
{
    [Fact]
    public void Analizar_LineaValida()
    {
        var info = LectorVideoAyudante.Analizar("{\"ancho\":3840,\"alto\":1080,\"duracionMs\":20000}\r\n");

        Assert.Equal(new SizePx(3840, 1080), info.Tamano);
        Assert.Equal(TimeSpan.FromSeconds(20), info.Duracion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hola")]
    [InlineData("{\"ancho\":0,\"alto\":1080,\"duracionMs\":20000}")]
    public void Analizar_Basura_LanzaExcepcionImagen(string salida) =>
        Assert.Throws<ExcepcionImagen>(() => LectorVideoAyudante.Analizar(salida));
}
