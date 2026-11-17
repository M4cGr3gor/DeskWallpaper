using DeskWallpaper.Nucleo;
using SkiaSharp;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>Lectura orientada y guardado como archivo nuevo.</summary>
public class ArchivoImagenPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();

    public void Dispose() => _img.Dispose();

    /// <summary>Imagen de prueba: degradado con un píxel transparente en (0,0).</summary>
    internal static ImagenRgba Degradado(int ancho, int alto, bool conTransparencia = false)
    {
        var p = new byte[ancho * alto * 4];
        for (var y = 0; y < alto; y++)
            for (var x = 0; x < ancho; x++)
            {
                var i = (y * ancho + x) * 4;
                p[i] = (byte)(x * 255 / Math.Max(1, ancho - 1));
                p[i + 1] = (byte)(y * 255 / Math.Max(1, alto - 1));
                p[i + 2] = 90;
                p[i + 3] = 255;
            }
        if (conTransparencia) { p[0] = p[1] = p[2] = 0; p[3] = 0; }
        return new ImagenRgba(ancho, alto, p);
    }

    [Fact]
    public void Constructor_TamanoIncoherente_Lanza() =>
        Assert.Throws<ArgumentException>(() => new ImagenRgba(2, 2, new byte[15]));

    [Fact]
    public void Leer_JpegGiradoPorExif_SaleGirado()
    {
        var jpeg = _img.Franjas("a.jpg", 40, 20, SKColors.Red, SKColors.Blue);

        var img = ArchivoImagen.Leer(_img.ConOrientacion(jpeg, "g.jpg", 6));

        Assert.Equal(new SizePx(20, 40), img.Tamano);
        Assert.Equal(20L * 40 * 4, img.Bytes);
    }

    [Fact]
    public void Leer_NoEsImagen_ExcepcionImagen() =>
        Assert.Throws<ExcepcionImagen>(() => ArchivoImagen.Leer(_img.Texto("x.jpg", "hola")));

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    [InlineData(".webp")]
    [InlineData(".bmp")]
    public void Guardar_MismoFormatoQueLaExtension(string ext)
    {
        var original = _img.Franjas("orig.png", 8, 8, SKColors.Gray);
        var img = Degradado(64, 32);
        var destino = Path.Combine(_img.Carpeta, "salida" + ext);

        ArchivoImagen.Guardar(img, destino, original);

        using (var codec = SKCodec.Create(destino))
        {
            var esperado = ext switch
            {
                ".jpg" => SKEncodedImageFormat.Jpeg,
                ".png" => SKEncodedImageFormat.Png,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Bmp,
            };
            Assert.Equal(esperado, codec.EncodedFormat);
        }
        var leida = ArchivoImagen.Leer(destino);
        Assert.Equal(img.Tamano, leida.Tamano);
        if (ext is ".png" or ".bmp") Assert.Equal(img.Pixeles, leida.Pixeles);
    }

    [Fact]
    public void Guardar_PngConTransparencia_LaConserva()
    {
        var original = _img.Franjas("orig.png", 8, 8, SKColors.Gray);
        var destino = Path.Combine(_img.Carpeta, "t.png");

        ArchivoImagen.Guardar(Degradado(10, 10, conTransparencia: true), destino, original);

        var leida = ArchivoImagen.Leer(destino);
        Assert.Equal(0, leida.Pixeles[3]);
        Assert.Equal(255, leida.Pixeles[7]);
    }

    [Fact]
    public void Guardar_SobreElOriginalPorUnaUnion_SeRechaza()
    {
        Directory.CreateDirectory(Path.Combine(_img.Carpeta, "real"));
        var original = _img.Franjas(Path.Combine("real", "orig.png"), 8, 8, SKColors.Gray);
        var bytes = File.ReadAllBytes(original);
        var enlace = Path.Combine(_img.Carpeta, "enlace");
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                   $"/c mklink /J \"{enlace}\" \"{Path.GetDirectoryName(original)}\"") { CreateNoWindow = true, UseShellExecute = false })!)
            p.WaitForExit();
        Assert.True(File.Exists(Path.Combine(enlace, "orig.png")));

        var ex = Assert.Throws<ExcepcionEdicion>(() => ArchivoImagen.Guardar(Degradado(8, 8), Path.Combine(enlace, "orig.png"), original));

        Assert.True(ex.EsElOriginal);
        Assert.Equal(bytes, File.ReadAllBytes(original));
        Directory.Delete(enlace);
    }

    [Fact]
    public void Guardar_SobreElOriginal_SeRechaza()
    {
        var original = _img.Franjas("orig.png", 8, 8, SKColors.Gray);
        var bytes = File.ReadAllBytes(original);

        var ex = Assert.Throws<ExcepcionEdicion>(() => ArchivoImagen.Guardar(Degradado(8, 8), original.ToUpperInvariant(), original));

        Assert.Equal("No se puede guardar encima del original", ex.Message);
        Assert.True(ex.EsElOriginal);
        Assert.Equal(bytes, File.ReadAllBytes(original));
    }

    [Fact]
    public void Guardar_NoDejaTemporales()
    {
        var original = _img.Franjas("orig.png", 8, 8, SKColors.Gray);
        var carpeta = Path.Combine(_img.Carpeta, "sal");
        Directory.CreateDirectory(carpeta);

        ArchivoImagen.Guardar(Degradado(8, 8), Path.Combine(carpeta, "r.jpg"), original);

        Assert.Equal(["r.jpg"], Directory.GetFiles(carpeta).Select(Path.GetFileName));
    }

    [Fact]
    public void Guardar_SobreOtroArchivoExistente_LoReemplaza()
    {
        var original = _img.Franjas("orig.png", 8, 8, SKColors.Gray);
        var destino = _img.Texto("otro.png", "viejo");

        ArchivoImagen.Guardar(Degradado(12, 6), destino, original);

        Assert.Equal(new SizePx(12, 6), ArchivoImagen.Leer(destino).Tamano);
    }

    [Fact]
    public void NombrePropuesto()
    {
        var original = Path.Combine(_img.Carpeta, "paisaje.jpg");
        string Nombre(bool borrado, double factor) => Path.GetFileName(ArchivoImagen.NombrePropuesto(original, borrado, factor));

        Assert.Equal("paisaje (editada).jpg", Nombre(true, 1));
        Assert.Equal("paisaje (×2).jpg", Nombre(false, 2));
        Assert.Equal("paisaje (×4).jpg", Nombre(false, 4));
        Assert.Equal("paisaje (editada).jpg", Nombre(true, 4));
        Assert.Equal("paisaje (×1,5).jpg", Nombre(false, 1.5));
        Assert.Equal(_img.Carpeta, Path.GetDirectoryName(ArchivoImagen.NombrePropuesto(original, true, 1)));

        File.WriteAllText(Path.Combine(_img.Carpeta, "paisaje (editada).jpg"), "x");
        Assert.Equal("paisaje (editada) (2).jpg", Nombre(true, 1));
    }

    [Fact]
    public void Escalar_CambiaTamanoYConservaColorLiso()
    {
        var p = new byte[10 * 10 * 4];
        for (var i = 0; i < p.Length; i += 4) { p[i] = 10; p[i + 1] = 200; p[i + 2] = 30; p[i + 3] = 255; }

        var r = ArchivoImagen.Escalar(new ImagenRgba(10, 10, p), new SizePx(25, 5));

        Assert.Equal(new SizePx(25, 5), r.Tamano);
        Assert.InRange(r.Pixeles[(2 * 25 + 12) * 4 + 1], 198, 202);
    }

    [Fact]
    public void Recorte_DevuelveLaRegion()
    {
        var img = Degradado(20, 10);

        var r = img.Recorte(new RectPx(5, 2, 4, 3));

        Assert.Equal(new SizePx(4, 3), r.Tamano);
        Assert.Equal(img.Pixeles.AsSpan((2 * 20 + 5) * 4, 4).ToArray(), r.Pixeles.AsSpan(0, 4).ToArray());
    }
}
