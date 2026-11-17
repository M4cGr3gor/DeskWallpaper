namespace DeskWallpaper.Nucleo;

/// <summary>Paso entre <see cref="ImagenRgba"/> y los tensores de los modelos: RGB en planos (CHW).</summary>
internal static class TensorCHW
{
    /// <summary>RGB en 0–1, en planos. La transparencia no pasa al modelo.</summary>
    public static float[] Desde(ImagenRgba img)
    {
        var plano = img.Ancho * img.Alto;
        var t = new float[3 * plano];
        for (var i = 0; i < plano; i++)
            for (var c = 0; c < 3; c++)
                t[c * plano + i] = img.Pixeles[i * 4 + c] / 255f;
        return t;
    }

    /// <summary>Tensor CHW a imagen opaca. <paramref name="escala"/>: 255 si el modelo da 0–1, 1 si da 0–255.</summary>
    /// <exception cref="InvalidOperationException">Si el tensor no tiene el tamaño esperado.</exception>
    public static ImagenRgba AImagen(float[] t, int ancho, int alto, float escala)
    {
        var plano = ancho * alto;
        if (t.Length != 3 * plano) throw new InvalidOperationException("El modelo ha devuelto un tamaño inesperado");
        var p = new byte[plano * 4];
        for (var i = 0; i < plano; i++)
        {
            for (var c = 0; c < 3; c++)
                p[i * 4 + c] = (byte)Math.Clamp(Math.Round(t[c * plano + i] * escala), 0, 255);
            p[i * 4 + 3] = 255;
        }
        return new ImagenRgba(ancho, alto, p);
    }
}
