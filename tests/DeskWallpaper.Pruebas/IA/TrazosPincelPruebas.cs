using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>Trazos del pincel aún sin aplicar: se pueden quitar uno a uno.</summary>
public class TrazosPincelPruebas
{
    [Fact]
    public void Nuevo_SinTrazos()
    {
        var t = new TrazosPincel(100, 100);

        Assert.Equal(0, t.Cantidad);
        Assert.True(t.Mascara.Vacia);
    }

    [Fact]
    public void UnTrazo_PintaLaMascara()
    {
        var t = new TrazosPincel(100, 100);

        t.Empezar(10, 10, 3);
        t.Seguir(50, 10);
        t.Terminar();

        Assert.Equal(1, t.Cantidad);
        Assert.True(t.Mascara[30, 10]);
        Assert.Single(t.Trazos);
        Assert.Equal(2, t.Trazos[0].Count);   // el punto inicial y un segmento
    }

    [Fact]
    public void QuitarUltimo_DejaSoloLosAnteriores()
    {
        var t = new TrazosPincel(100, 100);
        t.Empezar(10, 10, 3); t.Seguir(20, 10); t.Terminar();
        t.Empezar(80, 80, 3); t.Seguir(90, 80); t.Terminar();

        Assert.True(t.QuitarUltimo());

        Assert.Equal(1, t.Cantidad);
        Assert.True(t.Mascara[15, 10]);
        Assert.False(t.Mascara[85, 80]);
        Assert.Equal(new RectPx(7, 7, 16, 6), t.Mascara.Limites);
    }

    [Fact]
    public void QuitarUltimo_SinTrazos_False()
    {
        var t = new TrazosPincel(100, 100);

        Assert.False(t.QuitarUltimo());
    }

    [Fact]
    public void QuitarUltimo_TrazoQueTapaOtro_RecuperaElAnterior()
    {
        // El segundo trazo pasa por encima del primero: al quitarlo, lo del primero sigue pintado.
        var t = new TrazosPincel(100, 100);
        t.Empezar(10, 50, 4); t.Seguir(90, 50); t.Terminar();
        t.Empezar(50, 10, 4); t.Seguir(50, 90); t.Terminar();

        t.QuitarUltimo();

        Assert.True(t.Mascara[50, 50]);
        Assert.False(t.Mascara[50, 20]);
    }

    [Fact]
    public void Limpiar_QuitaTodo()
    {
        var t = new TrazosPincel(100, 100);
        t.Empezar(10, 10, 3); t.Terminar();

        t.Limpiar();

        Assert.Equal(0, t.Cantidad);
        Assert.True(t.Mascara.Vacia);
    }

    [Fact]
    public void SeguirSinEmpezar_NoHaceNada()
    {
        var t = new TrazosPincel(100, 100);

        t.Seguir(10, 10);

        Assert.Equal(0, t.Cantidad);
        Assert.True(t.Mascara.Vacia);
    }
}
