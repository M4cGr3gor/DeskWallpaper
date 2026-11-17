using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>Deshacer y rehacer en la ventana de edición.</summary>
public class HistorialEdicionPruebas
{
    private static ImagenRgba Img(byte valor, int lado = 4)
    {
        var p = new byte[lado * lado * 4];
        Array.Fill(p, valor);
        return new ImagenRgba(lado, lado, p);
    }

    [Fact]
    public void Inicial_SinCambios()
    {
        var original = Img(1);
        var h = new HistorialEdicion(original);

        Assert.Same(original, h.Original);
        Assert.Same(original, h.Actual);
        Assert.False(h.PuedeDeshacer);
        Assert.False(h.PuedeRehacer);
        Assert.False(h.HayCambiosSinGuardar);
        Assert.False(h.HayRetoque);
        Assert.Equal(1, h.FactorTotal);
    }

    [Fact]
    public void DeshacerYRehacer()
    {
        var h = new HistorialEdicion(Img(1));
        var a = Img(2);
        var b = Img(3);
        h.Anadir(new PasoEdicion(TipoPaso.Borrado, a));
        h.Anadir(new PasoEdicion(TipoPaso.Borrado, b));

        h.Deshacer();
        Assert.Same(a, h.Actual);
        Assert.True(h.PuedeRehacer);
        h.Deshacer();
        Assert.Same(h.Original, h.Actual);
        Assert.False(h.PuedeDeshacer);
        h.Deshacer();   // sin efecto
        Assert.Same(h.Original, h.Actual);

        h.Rehacer();
        h.Rehacer();
        Assert.Same(b, h.Actual);
        Assert.False(h.PuedeRehacer);
    }

    [Fact]
    public void AnadirTrasDeshacer_BorraLoRehacible()
    {
        var h = new HistorialEdicion(Img(1));
        h.Anadir(new PasoEdicion(TipoPaso.Borrado, Img(2)));
        h.Deshacer();

        var c = Img(4);
        h.Anadir(new PasoEdicion(TipoPaso.Borrado, c));

        Assert.False(h.PuedeRehacer);
        Assert.Same(c, h.Actual);
    }

    [Fact]
    public void HayRetoqueYFactor()
    {
        var h = new HistorialEdicion(Img(1));
        h.Anadir(new PasoEdicion(TipoPaso.Borrado, Img(2)));
        h.Anadir(new PasoEdicion(TipoPaso.Ampliado, Img(3, 8), 2));

        Assert.True(h.HayRetoque);
        Assert.Equal(2, h.FactorTotal);
        h.Deshacer();
        Assert.Equal(1, h.FactorTotal);
        Assert.True(h.HayRetoque);
        h.Deshacer();
        Assert.False(h.HayRetoque);

        var g = new HistorialEdicion(Img(1));
        g.Anadir(new PasoEdicion(TipoPaso.Ampliado, Img(2, 8), 2));
        g.Anadir(new PasoEdicion(TipoPaso.Ampliado, Img(3, 32), 4));
        Assert.Equal(8, g.FactorTotal);

        var m = new HistorialEdicion(Img(1));
        m.Anadir(new PasoEdicion(TipoPaso.Ampliado, Img(2, 6), 1.5));
        Assert.Equal(1.5, m.FactorTotal);
        Assert.False(g.HayRetoque);
    }

    [Fact]
    public void Limpieza_CuentaComoRetoque()
    {
        var h = new HistorialEdicion(Img(1));

        h.Anadir(new PasoEdicion(TipoPaso.Limpiado, Img(2)));

        Assert.True(h.HayRetoque);
        Assert.Equal(1, h.FactorTotal);
    }

    [Fact]
    public void MarcarGuardado_YVolverAlMismoPunto()
    {
        var h = new HistorialEdicion(Img(1));
        h.Anadir(new PasoEdicion(TipoPaso.Borrado, Img(2)));
        Assert.True(h.HayCambiosSinGuardar);

        h.MarcarGuardado();
        Assert.False(h.HayCambiosSinGuardar);
        h.Deshacer();
        Assert.True(h.HayCambiosSinGuardar);
        h.Rehacer();
        Assert.False(h.HayCambiosSinGuardar);

        h.Deshacer();
        h.Anadir(new PasoEdicion(TipoPaso.Borrado, Img(5)));   // el estado guardado ya no se puede alcanzar
        Assert.True(h.HayCambiosSinGuardar);
    }

    [Fact]
    public void TopeDeMemoria_DescartaLosPasosMasAntiguos()
    {
        var original = Img(1);
        var h = new HistorialEdicion(original, limiteBytes: 3 * original.Bytes);
        for (byte i = 2; i <= 6; i++) h.Anadir(new PasoEdicion(TipoPaso.Borrado, Img(i)));

        var deshechos = 0;
        while (h.PuedeDeshacer) { h.Deshacer(); deshechos++; }

        Assert.Equal(2, deshechos);
        Assert.Same(original, h.Original);
        Assert.True(h.HayCambiosSinGuardar);
        Assert.True(h.HayRetoque);   // lo descartado cuenta: la imagen sigue teniendo un borrado
    }

    [Fact]
    public void TopeDeMemoria_SiempreQuedaElUltimoPaso()
    {
        var original = Img(1);
        var h = new HistorialEdicion(original, limiteBytes: 1);
        var grande = Img(9, 16);

        h.Anadir(new PasoEdicion(TipoPaso.Ampliado, grande, 4));

        Assert.Same(grande, h.Actual);
        Assert.Equal(4, h.FactorTotal);
    }
}
