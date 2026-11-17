using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class ZonasPorDefectoPruebas
{
    private static DatosMonitor Mon(int n, int x, int y, int ancho = 1920, int alto = 1080) =>
        new($"ID{n}", n, $"M{n}", new RectPx(x, y, ancho, alto), 1.0, new SizeMm(600, 340), n == 1);

    [Fact]
    public void Usuario_DaDosZonas()
    {
        var zonas = ZonasPorDefecto.Calcular(Usuario);
        Assert.Equal(2, zonas.Count);
        Assert.Equal("2+1", zonas[0].Id);
        Assert.Equal([M2.Id, M1.Id], zonas[0].MonitorIds);
        Assert.Equal("3", zonas[1].Id);
        Assert.Equal([M3.Id], zonas[1].MonitorIds);
    }

    [Fact]
    public void TresEnFila_UnaZona()
    {
        var zonas = ZonasPorDefecto.Calcular(new Disposicion([Mon(2, 1920, 0), Mon(1, 0, 0), Mon(3, 3840, 0)]));
        Assert.Single(zonas);
        Assert.Equal("1+2+3", zonas[0].Id);
    }

    [Fact]
    public void DistintoAlto_NoSeUnen()
    {
        var zonas = ZonasPorDefecto.Calcular(new Disposicion([Mon(1, 0, 0), Mon(2, 1920, 0, 1920, 1200)]));
        Assert.Equal(2, zonas.Count);
    }

    [Fact]
    public void NoContiguos_NoSeUnen()
    {
        var zonas = ZonasPorDefecto.Calcular(new Disposicion([Mon(1, 0, 0), Mon(2, 1921, 0)]));
        Assert.Equal(2, zonas.Count);
    }

    [Fact]
    public void DistintaY_NoSeUnen()
    {
        var zonas = ZonasPorDefecto.Calcular(new Disposicion([Mon(1, 0, 0), Mon(2, 1920, 100)]));
        Assert.Equal(2, zonas.Count);
    }

    [Fact]
    public void UnSoloMonitor_UnaZona()
    {
        var zonas = ZonasPorDefecto.Calcular(new Disposicion([Mon(1, 0, 0)]));
        Assert.Single(zonas);
        Assert.Equal("1", zonas[0].Id);
    }
}
