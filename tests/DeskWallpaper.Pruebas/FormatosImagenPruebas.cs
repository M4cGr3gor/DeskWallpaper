using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas;

public class FormatosImagenPruebas
{
    [Theory]
    [InlineData(@"D:\Imagenes\FOTO.JPG")]
    [InlineData(@"C:\a\b.jpeg")]
    [InlineData("x.Png")]
    [InlineData("x.webp")]
    [InlineData("x.bmp")]
    public void EsImagenAdmitida_SiSinDistinguirMayusculas(string ruta) => Assert.True(FormatosImagen.EsImagenAdmitida(ruta));

    [Theory]
    [InlineData("doc.pdf")]
    [InlineData("sin extension")]
    [InlineData("animado.gif")]
    [InlineData("")]
    public void EsImagenAdmitida_No(string ruta) => Assert.False(FormatosImagen.EsImagenAdmitida(ruta));
}
