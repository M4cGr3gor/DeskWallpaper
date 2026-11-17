using DeskWallpaper.Nucleo;
using SkiaSharp;

namespace DeskWallpaper.Pruebas;

public class CacheMiniaturasPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    public void Dispose() => _img.Dispose();

    private CacheMiniaturas Nueva() => new(Path.Combine(_img.Carpeta, "datos"), new RenderizadorSkia());

    [Fact]
    public void Obtener_Genera_JPEG_256_YNoRegenera()
    {
        var cache = Nueva();
        var origen = _img.Franjas("a.png", 1000, 500, SKColors.Red);
        var ruta = cache.Obtener("h1", origen);

        Assert.EndsWith(".jpg", ruta);
        Assert.Equal((256, 128), ImagenesPrueba.Tamano(ruta));
        var creada = File.GetCreationTimeUtc(ruta);
        var longitud = new FileInfo(ruta).Length;
        File.Delete(origen); // si regenerase, fallaría al no encontrar el original
        Assert.Equal(ruta, cache.Obtener("h1", origen));
        Assert.Equal(creada, File.GetCreationTimeUtc(ruta));
        Assert.Equal(longitud, new FileInfo(ruta).Length);
    }

    [Fact]
    public void Recortar_BorraLasDeUsoMasAntiguo()
    {
        var cache = Nueva();
        var origen = _img.Franjas("a.png", 1000, 500, SKColors.Red, SKColors.Blue);
        var rutas = Enumerable.Range(0, 5).Select(i => cache.Obtener($"h{i}", origen)).ToList();
        for (var i = 0; i < 5; i++) File.SetLastWriteTimeUtc(rutas[i], DateTime.UtcNow.AddMinutes(i - 10)); // h0 la más antigua
        var unaMiniatura = new FileInfo(rutas[0]).Length;
        var tope = unaMiniatura * 3;

        cache.Recortar(tope);

        Assert.True(cache.BytesOcupados() <= tope * 0.9 + 1);
        Assert.False(File.Exists(rutas[0]));
        Assert.True(File.Exists(rutas[4]));
    }

    [Fact]
    public void Obtener_TrasVaciar_Regenera()
    {
        var cache = Nueva();
        var origen = _img.Franjas("a.png", 1000, 500, SKColors.Red);
        cache.Obtener("h1", origen);
        cache.Vaciar();
        Assert.Equal(0, cache.BytesOcupados());
        Assert.True(File.Exists(cache.Obtener("h1", origen)));
    }

    [Fact]
    public void ConfiguracionPorDefecto_Tope200() => Assert.Equal(200, Configuracion.PorDefecto().TopeMiniaturasMb);
}
