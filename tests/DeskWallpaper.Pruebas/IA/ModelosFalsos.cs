using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>LaMa de mentira: rellena todo de rojo (200, 0, 0) y recuerda la máscara recibida.</summary>
internal sealed class RellenoFalso : IModeloRelleno
{
    public float[]? UltimaMascara { get; private set; }
    public float[]? UltimaImagen { get; private set; }
    public int Llamadas { get; private set; }
    public bool Liberado { get; private set; }

    public float[] Rellenar(float[] imagen, float[] mascara)
    {
        Assert.Equal(3 * IModeloRelleno.Lado * IModeloRelleno.Lado, imagen.Length);
        Assert.Equal(IModeloRelleno.Lado * IModeloRelleno.Lado, mascara.Length);
        Llamadas++;
        UltimaImagen = imagen;
        UltimaMascara = mascara;
        var salida = new float[imagen.Length];
        Array.Fill(salida, 200f, 0, IModeloRelleno.Lado * IModeloRelleno.Lado);
        return salida;
    }

    public void Dispose() => Liberado = true;
}

/// <summary>SCUNet de mentira: suma 10/255 a cada canal y exige lados múltiplos de 64, como el de verdad.</summary>
internal sealed class LimpiezaFalsa(Action? alLlamar = null) : IModeloLimpieza
{
    public int Llamadas { get; private set; }

    public float[] Limpiar(float[] bloque, int ancho, int alto)
    {
        Llamadas++;
        alLlamar?.Invoke();
        Assert.Equal(0, ancho % 64);
        Assert.Equal(0, alto % 64);
        Assert.Equal(3 * ancho * alto, bloque.Length);
        return bloque.Select(v => Math.Min(1f, v + 10f / 255f)).ToArray();
    }

    public void Dispose() { }
}

/// <summary>Real-ESRGAN de mentira: repite cada píxel 4×4. Puede fallar a partir de la llamada <paramref name="fallaEn"/>.</summary>
internal sealed class AmpliacionFalsa(int bloque, int? fallaEn = null, Action<int>? alLlamar = null) : IModeloAmpliacion
{
    public bool UsaGrafica => true;
    public int Bloque { get; } = bloque;
    public int Llamadas { get; private set; }

    public float[] Ampliar(float[] entrada, int ancho, int alto)
    {
        Llamadas++;
        alLlamar?.Invoke(Llamadas);
        if (fallaEn is { } n && Llamadas >= n) throw new InvalidOperationException("fallo simulado");
        Assert.Equal(3 * ancho * alto, entrada.Length);
        var aw = ancho * 4;
        var ah = alto * 4;
        var salida = new float[3 * aw * ah];
        for (var c = 0; c < 3; c++)
            for (var y = 0; y < ah; y++)
                for (var x = 0; x < aw; x++)
                    salida[c * aw * ah + y * aw + x] = entrada[c * ancho * alto + (y / 4) * ancho + x / 4];
        return salida;
    }

    public void Dispose() { }
}
