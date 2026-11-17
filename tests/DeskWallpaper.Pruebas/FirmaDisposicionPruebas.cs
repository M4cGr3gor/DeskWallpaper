using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class FirmaDisposicionPruebas
{
    [Fact]
    public void Firma_NoDependeDelOrden()
    {
        var a = FirmaDisposicion.Calcular(new Disposicion([M1, M2, M3]));
        var b = FirmaDisposicion.Calcular(new Disposicion([M3, M1, M2]));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Firma_CambiaSiSeMueveUnMonitor()
    {
        var movido = M3 with { Escritorio = M3.Escritorio with { X = -1000 } };
        Assert.NotEqual(FirmaDisposicion.Calcular(Usuario), FirmaDisposicion.Calcular(new Disposicion([M1, M2, movido])));
    }

    [Fact]
    public void Firma_CambiaSiCambiaLaResolucion()
    {
        var otro = M3 with { Escritorio = M3.Escritorio with { Ancho = 2560, Alto = 1600 } };
        Assert.NotEqual(FirmaDisposicion.Calcular(Usuario), FirmaDisposicion.Calcular(new Disposicion([M1, M2, otro])));
    }

    [Fact]
    public void Firma_NoCambiaConElTamanoFisico()
    {
        var corregida = Usuario.ConCorrecciones(new Dictionary<string, SizeMm> { [M3.Id] = new SizeMm(310, 194) });
        Assert.Equal(new SizeMm(310, 194), corregida.Buscar(M3.Id)!.TamanoFisico);
        Assert.Equal(FirmaDisposicion.Calcular(Usuario), FirmaDisposicion.Calcular(corregida));
    }

    [Fact]
    public void Firma_NoCambiaSiSeDesplazaTodoElEscritorio()
    {
        DatosMonitor Mover(DatosMonitor m) => m with { Escritorio = m.Escritorio with { X = m.Escritorio.X + 100 } };
        var desplazada = new Disposicion([Mover(M1), Mover(M2), Mover(M3)]);
        Assert.Equal(FirmaDisposicion.Calcular(Usuario), FirmaDisposicion.Calcular(desplazada));
    }

    [Fact]
    public void Firma_Tiene16CaracteresHexadecimales()
    {
        var f = FirmaDisposicion.Calcular(Usuario);
        Assert.Matches("^[0-9a-f]{16}$", f);
    }

    [Fact]
    public void Buscar_NoDistingueMayusculas()
    {
        Assert.Same(M1, Usuario.Buscar(M1.Id.ToLowerInvariant()));
    }
}
