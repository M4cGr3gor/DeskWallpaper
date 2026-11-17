using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas;

public class EncuadreInteractivoPruebas
{
    private static readonly SizePx Imagen = new(3840, 1080);
    private static readonly RectMm Env = new(0, 0, 1220, 340);

    private static RectD Region(Encuadre e) => Recortador.RegionZona(Imagen, Env, e);

    [Fact]
    public void Normalizar_CentroFueraSeAjusta()
    {
        var n = EncuadreInteractivo.Normalizar(Imagen, Env, new Encuadre(0, 0, 2));
        var r = Region(new Encuadre(0, 0, 2));
        Assert.Equal(2, n.Zoom);
        Assert.Equal((r.X + r.Ancho / 2) / Imagen.Ancho, n.CentroX, 9);
        Assert.Equal((r.Y + r.Alto / 2) / Imagen.Alto, n.CentroY, 9);
    }

    [Fact]
    public void Desplazar_DerechaMueveElCentroAIzquierda()
    {
        var e = new Encuadre(0.5, 0.5, 2);
        var d = EncuadreInteractivo.Desplazar(Imagen, Env, e, dxImagen: 100, dyImagen: 0);
        Assert.Equal(0.5 - 100.0 / 3840, d.CentroX, 9);
    }

    [Fact]
    public void Desplazar_MasAllaDelBorde_SinBandas()
    {
        var d = EncuadreInteractivo.Desplazar(Imagen, Env, new Encuadre(0.5, 0.5, 2), 100000, 0);
        Assert.Equal(0, Region(d).X, 6);
    }

    [Fact]
    public void Zoom_SeLimita()
    {
        Assert.Equal(EncuadreInteractivo.ZoomMaximo, EncuadreInteractivo.Zoom(Imagen, Env, new Encuadre(), 100).Zoom);
        Assert.Equal(1, EncuadreInteractivo.Zoom(Imagen, Env, new Encuadre(Zoom: 2), 0.01).Zoom);
    }

    [Fact]
    public void ZoomTrasDesplazarAlBorde_NoSalta()
    {
        // Sin normalizar, el centro guardado quedaría muy a la izquierda (fuera) y al acercar la vista
        // la región "saltaría". Normalizado, el zoom se hace alrededor del centro que realmente se ve.
        var alBorde = EncuadreInteractivo.Desplazar(Imagen, Env, new Encuadre(0.5, 0.5, 2), 100000, 0);
        var antes = Region(alBorde);
        var despues = Region(EncuadreInteractivo.Zoom(Imagen, Env, alBorde, 1.1));
        Assert.Equal(antes.X + antes.Ancho / 2, despues.X + despues.Ancho / 2, 6);
        Assert.Equal(antes.Y + antes.Alto / 2, despues.Y + despues.Alto / 2, 6);
    }
}
