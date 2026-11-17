using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>Ampliar por bloques solapados sin costuras.</summary>
public class AmpliadorPruebas
{
    private static ImagenRgba Aleatoria(int ancho, int alto, int semilla = 7, Func<int, int, byte>? alfa = null)
    {
        var azar = new Random(semilla);
        var p = new byte[ancho * alto * 4];
        azar.NextBytes(p);
        for (var y = 0; y < alto; y++)
            for (var x = 0; x < ancho; x++)
                p[(y * ancho + x) * 4 + 3] = alfa?.Invoke(x, y) ?? 255;
        return new ImagenRgba(ancho, alto, p);
    }

    [Fact]
    public void Factores()
    {
        Assert.Equal([1.5, 2, 3, 4], Ampliador.Factores(new SizePx(2560, 1024)));
        Assert.Equal([1.5, 2], Ampliador.Factores(new SizePx(7680, 2160)));
        Assert.Equal([1.5], Ampliador.Factores(new SizePx(9000, 1000)));
        Assert.Empty(Ampliador.Factores(new SizePx(11000, 1000)));
    }

    [Fact]
    public void Propuesto()
    {
        // 2560×1024 en una zona de 3840 de ancho: ×1,5 la deja justa.
        Assert.Equal(1.5, Ampliador.Propuesto(new SizePx(2560, 1024), 1.5));
        Assert.Equal(1.5, Ampliador.Propuesto(new SizePx(2560, 1024), 1.4999));
        Assert.Equal(2, Ampliador.Propuesto(new SizePx(2560, 1024), 1.6));
        Assert.Equal(3, Ampliador.Propuesto(new SizePx(2560, 1024), 2.5));
        Assert.Equal(2, Ampliador.Propuesto(new SizePx(2560, 1024), null));
        Assert.Equal(2, Ampliador.Propuesto(new SizePx(7680, 2160), 3));
        Assert.Equal(1.5, Ampliador.Propuesto(new SizePx(9000, 1000), null));
        Assert.Equal(0, Ampliador.Propuesto(new SizePx(11000, 1000), null));
    }

    [Fact]
    public void FactorMaximo_HastaCuatroYSinPasarDel_Limite()
    {
        Assert.Equal(4, Ampliador.FactorMaximo(new SizePx(2560, 1024)));
        Assert.Equal(3.2, Ampliador.FactorMaximo(new SizePx(5120, 1440)), 6);   // 16384 / 5120
        Assert.Equal(1.8, Ampliador.FactorMaximo(new SizePx(9000, 1000)), 6);   // 1,82 → 1,8 (hacia abajo)
        Assert.Equal(1, Ampliador.FactorMaximo(new SizePx(16384, 100)), 6);
        Assert.True(Ampliador.Resultado(new SizePx(9000, 1000), Ampliador.FactorMaximo(new SizePx(9000, 1000))).Ancho <= Ampliador.LadoMaximo);
    }

    [Theory]
    [InlineData(2.7, 405, 243)]
    [InlineData(1.1, 165, 99)]
    public void Ampliar_FactorPersonalizado(double factor, int ancho, int alto)
    {
        var r = Ampliador.Ampliar(Aleatoria(150, 90), new AmpliacionFalsa(64), factor, null, CancellationToken.None);

        Assert.Equal(new SizePx(ancho, alto), r.Tamano);
    }

    [Fact]
    public void Bloques()
    {
        Assert.Equal(10, Ampliador.Bloques(new SizePx(2560, 1024), 512));
        Assert.Equal(6, Ampliador.Bloques(new SizePx(150, 90), 64));
    }

    [Fact]
    public void Ampliar4_IgualQueRepetirPixeles()
    {
        var img = Aleatoria(150, 90);

        var r = Ampliador.Ampliar(img, new AmpliacionFalsa(64), 4, null, CancellationToken.None);

        Assert.Equal(new SizePx(600, 360), r.Tamano);
        for (var y = 0; y < 360; y++)
            for (var x = 0; x < 600; x++)
                for (var c = 0; c < 3; c++)
                    Assert.Equal(img.Pixeles[((y / 4) * 150 + x / 4) * 4 + c], r.Pixeles[(y * 600 + x) * 4 + c]);
    }

    [Fact]
    public void Ampliar2_TamanoYSinCosturas()
    {
        var p = new byte[150 * 90 * 4];
        for (var i = 0; i < p.Length; i += 4) { p[i] = 40; p[i + 1] = 120; p[i + 2] = 220; p[i + 3] = 255; }

        var r = Ampliador.Ampliar(new ImagenRgba(150, 90, p), new AmpliacionFalsa(64), 2, null, CancellationToken.None);

        Assert.Equal(new SizePx(300, 180), r.Tamano);
        for (var i = 0; i < r.Pixeles.Length; i += 4)
        {
            Assert.InRange(r.Pixeles[i], 39, 41);
            Assert.InRange(r.Pixeles[i + 1], 119, 121);
            Assert.InRange(r.Pixeles[i + 2], 219, 221);
            Assert.Equal(255, r.Pixeles[i + 3]);
        }
    }

    [Theory]
    [InlineData(1.5, 225, 135)]
    [InlineData(3, 450, 270)]
    public void AmpliarFactorNoEntero_TamanoYSinCosturas(double factor, int ancho, int alto)
    {
        var p = new byte[150 * 90 * 4];
        for (var i = 0; i < p.Length; i += 4) { p[i] = 40; p[i + 1] = 120; p[i + 2] = 220; p[i + 3] = 255; }

        var r = Ampliador.Ampliar(new ImagenRgba(150, 90, p), new AmpliacionFalsa(64), factor, null, CancellationToken.None);

        Assert.Equal(new SizePx(ancho, alto), r.Tamano);
        for (var i = 0; i < r.Pixeles.Length; i += 4)
        {
            Assert.InRange(r.Pixeles[i], 39, 41);
            Assert.InRange(r.Pixeles[i + 1], 119, 121);
            Assert.InRange(r.Pixeles[i + 2], 219, 221);
        }
    }

    [Fact]
    public void Ampliar15_SigueLaImagen()
    {
        // Franja izquierda roja y derecha azul: tras ×1,5 la frontera queda en 1,5 veces su sitio (sin desplazar bloques).
        var p = new byte[200 * 40 * 4];
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 200; x++)
            {
                var i = (y * 200 + x) * 4;
                p[i] = (byte)(x < 100 ? 255 : 0); p[i + 2] = (byte)(x < 100 ? 0 : 255); p[i + 3] = 255;
            }

        var r = Ampliador.Ampliar(new ImagenRgba(200, 40, p), new AmpliacionFalsa(64), 1.5, null, CancellationToken.None);

        int Rojo(int x) => r.Pixeles[(20 * 300 + x) * 4];
        Assert.True(Rojo(140) > 200);
        Assert.True(Rojo(160) < 50);
        Assert.True(Rojo(5) > 200 && Rojo(95) > 200);   // dentro de bloques distintos, sin huecos
    }

    [Fact]
    public void Ampliar_ConservaLaTransparencia()
    {
        var img = Aleatoria(100, 60, alfa: (x, _) => x < 50 ? (byte)0 : (byte)255);

        var r = Ampliador.Ampliar(img, new AmpliacionFalsa(64), 2, null, CancellationToken.None);

        Assert.Equal(0, r.Pixeles[(60 * 200 + 20) * 4 + 3]);
        Assert.Equal(255, r.Pixeles[(60 * 200 + 180) * 4 + 3]);
    }

    [Fact]
    public void Ampliar_InformaProgresoHastaUno()
    {
        var valores = new List<double>();
        var progreso = new ProgresoSincrono(valores.Add);

        Ampliador.Ampliar(Aleatoria(150, 90), new AmpliacionFalsa(64), 4, progreso, CancellationToken.None);

        Assert.Equal(Ampliador.Bloques(new SizePx(150, 90), 64), valores.Count);
        Assert.Equal(valores.Order(), valores);
        Assert.Equal(1.0, valores[^1], 6);
    }

    [Fact]
    public void Ampliar_Cancelado_Lanza()
    {
        using var cts = new CancellationTokenSource();
        var modelo = new AmpliacionFalsa(64, alLlamar: n => { if (n == 1) cts.Cancel(); });

        Assert.Throws<OperationCanceledException>(() =>
            Ampliador.Ampliar(Aleatoria(150, 90), modelo, 4, null, cts.Token));
        Assert.Equal(1, modelo.Llamadas);
    }

    [Fact]
    public void Ampliar_FactorNoPermitido_Lanza() =>
        Assert.Throws<ArgumentException>(() =>
            Ampliador.Ampliar(Aleatoria(10, 10), new AmpliacionFalsa(64), 5, null, CancellationToken.None));

    /// <summary>IProgress que informa en el acto (Progress&lt;T&gt; lo haría en otro hilo).</summary>
    private sealed class ProgresoSincrono(Action<double> accion) : IProgress<double>
    {
        public void Report(double value) => accion(value);
    }
}
