namespace DeskWallpaper.Nucleo;

/// <summary>Modelo que quita artefactos JPEG y ruido sin cambiar el tamaño (SCUNet). Lados múltiplos de 64.</summary>
public interface IModeloLimpieza : IDisposable
{
    /// <summary>CHW RGB 0–1 de ancho×alto (múltiplos de 64) → CHW RGB 0–1 del mismo tamaño.</summary>
    float[] Limpiar(float[] bloque, int ancho, int alto);
}

/// <summary>
/// Quita artefactos JPEG y ruido por bloques solapados: de cada bloque solo se pega el núcleo,
/// así no se ven costuras. Cada bloque se rellena por reflejo hasta múltiplos de 64: SCUNet reduce la imagen ×8 y
/// trabaja ahí con ventanas de 8 (comprobado en real: con 272 px falla un «Reshape»; la ficha del modelo decía 8).
/// La transparencia no pasa por el modelo.
/// </summary>
public static class Limpiador
{
    /// <summary>Núcleo del bloque: con el solape a cada lado suma 256, múltiplo de 64 sin relleno de más.</summary>
    public const int Bloque = 224;
    public const int Solape = 16;
    public const int Multiplo = 64;

    public static int Bloques(SizePx tamano) =>
        ((tamano.Ancho + Bloque - 1) / Bloque) * ((tamano.Alto + Bloque - 1) / Bloque);

    /// <exception cref="OperationCanceledException">Si se cancela entre dos bloques.</exception>
    public static ImagenRgba Limpiar(ImagenRgba img, IModeloLimpieza modelo, IProgress<double>? progreso, CancellationToken ct)
    {
        var resultado = img.Copia();
        var total = Bloques(img.Tamano);
        var hechos = 0;

        for (var by = 0; by < img.Alto; by += Bloque)
            for (var bx = 0; bx < img.Ancho; bx += Bloque)
            {
                ct.ThrowIfCancellationRequested();
                var nucleo = new RectPx(bx, by, Math.Min(Bloque, img.Ancho - bx), Math.Min(Bloque, img.Alto - by));
                var x0 = Math.Max(0, bx - Solape);
                var y0 = Math.Max(0, by - Solape);
                var trozo = new RectPx(x0, y0,
                    Math.Min(img.Ancho, nucleo.X + nucleo.Ancho + Solape) - x0,
                    Math.Min(img.Alto, nucleo.Y + nucleo.Alto + Solape) - y0);

                var anchoRelleno = (trozo.Ancho + Multiplo - 1) / Multiplo * Multiplo;
                var altoRelleno = (trozo.Alto + Multiplo - 1) / Multiplo * Multiplo;
                var salida = modelo.Limpiar(Tensor(img, trozo, anchoRelleno, altoRelleno), anchoRelleno, altoRelleno);
                if (salida.Length != 3 * anchoRelleno * altoRelleno) throw new InvalidOperationException("El modelo ha devuelto un tamaño inesperado");

                var plano = anchoRelleno * altoRelleno;
                for (var y = 0; y < nucleo.Alto; y++)
                {
                    var ty = nucleo.Y - trozo.Y + y;
                    for (var x = 0; x < nucleo.Ancho; x++)
                    {
                        var tx = nucleo.X - trozo.X + x;
                        var i = ((nucleo.Y + y) * img.Ancho + nucleo.X + x) * 4;
                        for (var c = 0; c < 3; c++)
                            resultado.Pixeles[i + c] = (byte)Math.Clamp(Math.Round(salida[c * plano + ty * anchoRelleno + tx] * 255f), 0, 255);
                    }
                }
                progreso?.Report(++hechos / (double)total);
            }
        return resultado;
    }

    /// <summary>El trozo en CHW 0–1 de anchoRelleno×altoRelleno, rellenando lo que sobra por reflejo.</summary>
    private static float[] Tensor(ImagenRgba img, RectPx trozo, int anchoRelleno, int altoRelleno)
    {
        var plano = anchoRelleno * altoRelleno;
        var t = new float[3 * plano];
        for (var y = 0; y < altoRelleno; y++)
        {
            var sy = trozo.Y + Reflejo(y, trozo.Alto);
            for (var x = 0; x < anchoRelleno; x++)
            {
                var sx = trozo.X + Reflejo(x, trozo.Ancho);
                var i = (sy * img.Ancho + sx) * 4;
                for (var c = 0; c < 3; c++) t[c * plano + y * anchoRelleno + x] = img.Pixeles[i + c] / 255f;
            }
        }
        return t;
    }

    /// <summary>Índice reflejado dentro de 0..n-1 (n−1, n−2… al pasarse por el final).</summary>
    private static int Reflejo(int i, int n)
    {
        if (n == 1) return 0;
        while (i < 0 || i >= n) i = i < 0 ? -i : 2 * n - 2 - i;
        return i;
    }
}
