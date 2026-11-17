using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>Quitar artefactos y ruido por bloques sin costuras.</summary>
public class LimpiadorPruebas
{
    private static ImagenRgba Lisa(int ancho, int alto, byte valor = 100, Func<int, int, byte>? alfa = null)
    {
        var p = new byte[ancho * alto * 4];
        for (var y = 0; y < alto; y++)
            for (var x = 0; x < ancho; x++)
            {
                var i = (y * ancho + x) * 4;
                p[i] = p[i + 1] = p[i + 2] = valor;
                p[i + 3] = alfa?.Invoke(x, y) ?? 255;
            }
        return new ImagenRgba(ancho, alto, p);
    }

    [Fact]
    public void Limpiar_MismoTamanoYSinCosturas()
    {
        var r = Limpiador.Limpiar(Lisa(600, 300), new LimpiezaFalsa(), null, CancellationToken.None);

        Assert.Equal(new SizePx(600, 300), r.Tamano);
        for (var i = 0; i < r.Pixeles.Length; i += 4)
        {
            Assert.InRange(r.Pixeles[i], 109, 111);
            Assert.Equal(255, r.Pixeles[i + 3]);
        }
    }

    [Theory]
    [InlineData(300, 201)]
    [InlineData(100, 37)]
    [InlineData(5, 3)]
    public void Limpiar_LadosNoMultiplosDe8YPequena(int ancho, int alto)
    {
        var modelo = new LimpiezaFalsa();

        var r = Limpiador.Limpiar(Lisa(ancho, alto), modelo, null, CancellationToken.None);

        Assert.Equal(new SizePx(ancho, alto), r.Tamano);
        Assert.All(Enumerable.Range(0, ancho * alto), i => Assert.InRange(r.Pixeles[i * 4], 109, 111));
        Assert.True(modelo.Llamadas > 0);
    }

    [Fact]
    public void Limpiar_ConservaLaTransparencia()
    {
        var img = Lisa(300, 100, alfa: (x, _) => (byte)(x % 256));

        var r = Limpiador.Limpiar(img, new LimpiezaFalsa(), null, CancellationToken.None);

        for (var i = 3; i < img.Pixeles.Length; i += 4) Assert.Equal(img.Pixeles[i], r.Pixeles[i]);
    }

    [Fact]
    public void Limpiar_ProgresoYBloques()
    {
        var valores = new List<double>();

        Limpiador.Limpiar(Lisa(600, 300), new LimpiezaFalsa(), new ProgresoSincrono(valores.Add), CancellationToken.None);

        Assert.Equal(6, Limpiador.Bloques(new SizePx(600, 300)));
        Assert.Equal(6, valores.Count);
        Assert.Equal(1.0, valores[^1], 6);
    }

    [Fact]
    public void Limpiar_Cancelado_Lanza()
    {
        using var cts = new CancellationTokenSource();
        var modelo = new LimpiezaFalsa(alLlamar: () => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() => Limpiador.Limpiar(Lisa(600, 300), modelo, null, cts.Token));
        Assert.Equal(1, modelo.Llamadas);
    }

    [Fact]
    public void Limpiar_SigueLaImagen()
    {
        // Mitad izquierda oscura y derecha clara: la frontera sigue en su sitio aunque caiga entre bloques.
        var p = new byte[600 * 40 * 4];
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 600; x++)
            {
                var i = (y * 600 + x) * 4;
                p[i] = p[i + 1] = p[i + 2] = (byte)(x < 300 ? 20 : 200);
                p[i + 3] = 255;
            }

        var r = Limpiador.Limpiar(new ImagenRgba(600, 40, p), new LimpiezaFalsa(), null, CancellationToken.None);

        Assert.InRange(r.Pixeles[(20 * 600 + 299) * 4], 29, 31);
        Assert.InRange(r.Pixeles[(20 * 600 + 300) * 4], 209, 211);
        Assert.InRange(r.Pixeles[(20 * 600 + 256) * 4], 29, 31);   // borde de bloque
    }

    private sealed class ProgresoSincrono(Action<double> accion) : IProgress<double>
    {
        public void Report(double value) => accion(value);
    }
}
