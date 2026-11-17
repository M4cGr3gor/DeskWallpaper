using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class PlanRecortePruebas
{
    private const double Tol = 0.01;
    private static readonly LienzoFisico Lienzo =
        LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double> { [LienzoFisico.ClaveMarco(M2.Id, M1.Id)] = 20 });

    [Fact]
    public void Panoramica_DosRecortesEnOrden()
    {
        var plan = PlanRecorte.Calcular(new SizePx(3840, 1080), [M2.Id, M1.Id], Usuario, Lienzo, new Encuadre());

        Assert.Equal([M2.Id, M1.Id], plan.Select(p => p.MonitorId));
        Assert.All(plan, p => Assert.Equal(new SizePx(1920, 1080), p.Nativa));
        Assert.Equal(0, plan[0].Region.X, Tol);
        Assert.Equal(4.918, plan[0].Region.Y, Tol);
        Assert.Equal(1888.525, plan[0].Region.Ancho, Tol);
        Assert.Equal(1951.475, plan[1].Region.X, Tol);
    }

    [Fact]
    public void FactorEsElDeFactorAmpliacion()
    {
        var plan = PlanRecorte.Calcular(new SizePx(1920, 713), [M2.Id, M1.Id], Usuario, Lienzo, new Encuadre());
        Assert.All(plan, p =>
        {
            Assert.True(p.Factor > 1);
            Assert.Equal(Recortador.FactorAmpliacion(p.Region, p.Nativa), p.Factor, 9);
        });
    }

    [Fact]
    public void MonitorDesconocido_Lanza()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            PlanRecorte.Calcular(new SizePx(1920, 1080), ["NO-EXISTE"], Usuario, Lienzo, new Encuadre()));
    }
}
