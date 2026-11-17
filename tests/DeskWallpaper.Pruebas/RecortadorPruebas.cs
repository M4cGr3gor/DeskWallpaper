using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas;

public class RecortadorPruebas
{
    private const double Tol = 0.01;
    private static readonly RectMm Envolvente2y1 = new(0, 0, 1220, 340);
    private static readonly RectMm MmM2 = new(0, 0, 600, 340);
    private static readonly RectMm MmM1 = new(620, 0, 600, 340);

    private static void Igual(RectD esperado, RectD real)
    {
        Assert.Equal(esperado.X, real.X, Tol);
        Assert.Equal(esperado.Y, real.Y, Tol);
        Assert.Equal(esperado.Ancho, real.Ancho, Tol);
        Assert.Equal(esperado.Alto, real.Alto, Tol);
    }

    private static void DentroDe(SizePx imagen, RectD r)
    {
        Assert.True(r.X >= -1e-9 && r.Y >= -1e-9, $"Región fuera por arriba/izquierda: {r}");
        Assert.True(r.X + r.Ancho <= imagen.Ancho + 1e-9 && r.Y + r.Alto <= imagen.Alto + 1e-9, $"Región fuera por abajo/derecha: {r}");
    }

    [Fact]
    public void Panoramica3840_Zona2mas1()
    {
        var r = Recortador.RegionZona(new SizePx(3840, 1080), Envolvente2y1, new Encuadre());
        Igual(new RectD(0, 4.918, 3840, 1070.164), r);
    }

    [Fact]
    public void RegionMonitor_M2()
    {
        var zona = Recortador.RegionZona(new SizePx(3840, 1080), Envolvente2y1, new Encuadre());
        Igual(new RectD(0, 4.918, 1888.525, 1070.164), Recortador.RegionMonitor(zona, Envolvente2y1, MmM2));
    }

    [Fact]
    public void RegionMonitor_M1_DescartaElMarco()
    {
        var zona = Recortador.RegionZona(new SizePx(3840, 1080), Envolvente2y1, new Encuadre());
        Igual(new RectD(1951.475, 4.918, 1888.525, 1070.164), Recortador.RegionMonitor(zona, Envolvente2y1, MmM1));
    }

    [Fact]
    public void ImagenEstrecha_LlenaSinBandas()
    {
        var imagen = new SizePx(1920, 713);
        var r = Recortador.RegionZona(imagen, Envolvente2y1, new Encuadre());
        Assert.Equal(1920, r.Ancho, Tol);
        Assert.Equal(1220.0 / 340.0, r.Ancho / r.Alto, 6);
        DentroDe(imagen, r);
        var m2 = Recortador.RegionMonitor(r, Envolvente2y1, MmM2);
        Assert.True(Recortador.FactorAmpliacion(m2, new SizePx(1920, 1080)) > 1);
    }

    [Fact]
    public void ImagenMasAlta_RecortaArribaYAbajo()
    {
        var imagen = new SizePx(1920, 1080);
        var r = Recortador.RegionZona(imagen, Envolvente2y1, new Encuadre());
        Assert.Equal(1920, r.Ancho, Tol);
        Assert.Equal(1920 * 340.0 / 1220.0, r.Alto, Tol);
        DentroDe(imagen, r);
    }

    [Fact]
    public void Zoom2_MitadDeTamano()
    {
        var img = new SizePx(3840, 1080);
        var r1 = Recortador.RegionZona(img, Envolvente2y1, new Encuadre());
        var r2 = Recortador.RegionZona(img, Envolvente2y1, new Encuadre(Zoom: 2));
        Assert.Equal(r1.Ancho / 2, r2.Ancho, Tol);
        Assert.Equal(r1.Alto / 2, r2.Alto, Tol);
    }

    [Fact]
    public void CentroEnEsquina_SeLimita()
    {
        var img = new SizePx(3840, 1080);
        var r = Recortador.RegionZona(img, Envolvente2y1, new Encuadre(0, 0, 2));
        Assert.Equal(0, r.X, Tol);
        Assert.Equal(0, r.Y, Tol);
        DentroDe(img, r);
    }

    [Fact]
    public void CentroFueraPorAbajo_SeLimita()
    {
        var img = new SizePx(3840, 1080);
        var r = Recortador.RegionZona(img, Envolvente2y1, new Encuadre(1, 1, 3));
        DentroDe(img, r);
        Assert.Equal(img.Ancho, r.X + r.Ancho, Tol);
    }

    [Fact]
    public void ZoomMenorQueUno_SeTrataComoUno()
    {
        var img = new SizePx(3840, 1080);
        Igual(Recortador.RegionZona(img, Envolvente2y1, new Encuadre()),
              Recortador.RegionZona(img, Envolvente2y1, new Encuadre(Zoom: 0.5)));
    }

    [Fact]
    public void FactorAmpliacion_SalidaEntreRegion()
    {
        Assert.Equal(2.0, Recortador.FactorAmpliacion(new RectD(0, 0, 960, 540), new SizePx(1920, 1080)), 6);
    }
}
