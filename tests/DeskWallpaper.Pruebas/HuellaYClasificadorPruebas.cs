using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class HuellaYClasificadorPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    public void Dispose() => _img.Dispose();

    [Fact]
    public void Huella_IgualParaCopias()
    {
        var a = _img.Franjas("a.png", 400, 300, SKColors.Red, SKColors.Blue);
        var b = Path.Combine(_img.Carpeta, "copia con otro nombre.png");
        File.Copy(a, b);
        Assert.Equal(Huella.Calcular(a), Huella.Calcular(b));
        Assert.Matches("^[0-9a-f]{16}$", Huella.Calcular(a));
    }

    [Fact]
    public void Huella_CambiaSiCambiaElContenido()
    {
        var a = _img.Franjas("a.png", 400, 300, SKColors.Red);
        var b = _img.Franjas("b.png", 400, 300, SKColors.Green);
        Assert.NotEqual(Huella.Calcular(a), Huella.Calcular(b));
    }

    [Fact]
    public void Huella_BmpSinComprimir_DistingueCambiosEnMedio()
    {
        // Dos BMP del mismo tamaño con las franjas de arriba y abajo iguales (p. ej. negras).
        var a = Path.Combine(_img.Carpeta, "uno.bmp");
        var b = Path.Combine(_img.Carpeta, "dos.BMP");
        var datos = new byte[300_000];
        File.WriteAllBytes(a, datos);
        datos[150_000] = 1;
        File.WriteAllBytes(b, datos);
        Assert.NotEqual(Huella.Calcular(a), Huella.Calcular(b));
    }

    [Fact]
    public void Huella_ArchivoGrande_DistingueCambiosAlFinal()
    {
        var a = Path.Combine(_img.Carpeta, "grande1.bin");
        var b = Path.Combine(_img.Carpeta, "grande2.bin");
        var datos = new byte[300_000];
        new Random(1).NextBytes(datos);
        File.WriteAllBytes(a, datos);
        datos[^1] ^= 0xFF;
        File.WriteAllBytes(b, datos);
        Assert.NotEqual(Huella.Calcular(a), Huella.Calcular(b));
    }

    [Fact]
    public void Huella_ArchivoPequeno() =>
        Assert.Matches("^[0-9a-f]{16}$", Huella.Calcular(_img.Texto("p.txt", "hola")));

    [Fact]
    public void Huella_ArchivoInexistente_Lanza() =>
        Assert.ThrowsAny<IOException>(() => Huella.Calcular(Path.Combine(_img.Carpeta, "no.png")));

    private static readonly LienzoFisico Lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());
    private static readonly IReadOnlyList<Zona> Zonas = ZonasPorDefecto.Calcular(Usuario);

    [Theory]
    [InlineData(3840, 1080, Categoria.Panoramica)]
    [InlineData(2560, 1024, Categoria.Panoramica)]
    [InlineData(1920, 713, Categoria.Panoramica)]
    [InlineData(1920, 1200, Categoria.UnMonitor)]
    [InlineData(1920, 1080, Categoria.UnMonitor)]
    [InlineData(1080, 1920, Categoria.Otras)]
    [InlineData(1000, 1000, Categoria.Otras)]
    public void Clasificar_Usuario(int ancho, int alto, Categoria esperada) =>
        Assert.Equal(esperada, ClasificadorProporcion.Clasificar(new SizePx(ancho, alto), Zonas, Lienzo));

    [Fact]
    public void Clasificar_TodosSeparados_PanoramicaSigueSiendolo()
    {
        var sueltas = EditorZonas.Separar(Usuario, Zonas, "2+1")!;
        Assert.Equal(Categoria.Panoramica, ClasificadorProporcion.Clasificar(new SizePx(3840, 1080), sueltas, Lienzo));
        Assert.Equal(Categoria.UnMonitor, ClasificadorProporcion.Clasificar(new SizePx(1920, 1080), sueltas, Lienzo));
    }
}
