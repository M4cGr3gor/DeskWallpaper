using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class LienzoFisicoPruebas
{
    private const double Tol = 0.01;

    private static void Igual(RectMm esperado, RectMm real)
    {
        Assert.Equal(esperado.X, real.X, Tol);
        Assert.Equal(esperado.Y, real.Y, Tol);
        Assert.Equal(esperado.Ancho, real.Ancho, Tol);
        Assert.Equal(esperado.Alto, real.Alto, Tol);
    }

    private static LienzoFisico LienzoUsuario() =>
        LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double> { [LienzoFisico.ClaveMarco(M2.Id, M1.Id)] = 20 });

    [Fact]
    public void Usuario_PosicionesConMarco()
    {
        var l = LienzoUsuario();
        Igual(new RectMm(0, 0, 600, 340), l.Monitor(M2.Id));
        Igual(new RectMm(620, 0, 600, 340), l.Monitor(M1.Id));
        // Centros alineados: el centro de M3 está 1200 px a la izquierda del de M1 → 375 mm con la densidad de M1.
        // Centro M1 = 920 mm → centro M3 = 545 mm → X = 545 − 151 = 394.
        Igual(new RectMm(394, 340, 302, 189), l.Monitor(M3.Id));
    }

    [Fact]
    public void MonitorPequenoCentradoEnWindows_QuedaCentrado()
    {
        // Como el equipo del usuario: el 3 (1920 px, 346 mm) centrado bajo la unión de 2 y 1 en Windows.
        var m1 = M1 with { Escritorio = new RectPx(0, 0, 1920, 1080) };
        var m2 = M2 with { Escritorio = new RectPx(-1920, 0, 1920, 1080) };
        var m3 = M3 with { Escritorio = new RectPx(-960, 1080, 1920, 1200), TamanoFisico = new SizeMm(346, 216) };
        var l = LienzoFisico.DesdeDisposicion(new Disposicion([m1, m2, m3]), new Dictionary<string, double>());
        var union = l.Monitor(m1.Id).X;
        var r3 = l.Monitor(m3.Id);
        Assert.Equal(union, r3.X + r3.Ancho / 2, Tol);
    }

    [Fact]
    public void Usuario_Envolventes()
    {
        var l = LienzoUsuario();
        var zonas = ZonasPorDefecto.Calcular(Usuario);
        Igual(new RectMm(0, 0, 1220, 340), l.Envolvente(zonas[0]));
        Igual(new RectMm(394, 340, 302, 189), l.Envolvente(zonas[1]));
    }

    [Fact]
    public void SinMarcos_M1PegadoAM2()
    {
        var l = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());
        Assert.Equal(600, l.Monitor(M1.Id).X, Tol);
    }

    [Fact]
    public void ClaveMarco_Simetrica()
    {
        Assert.Equal(LienzoFisico.ClaveMarco(M1.Id, M2.Id), LienzoFisico.ClaveMarco(M2.Id, M1.Id));
    }

    [Fact]
    public void Monitor_NoDistingueMayusculas()
    {
        Igual(LienzoUsuario().Monitor(M1.Id), LienzoUsuario().Monitor(M1.Id.ToLowerInvariant()));
    }
}
