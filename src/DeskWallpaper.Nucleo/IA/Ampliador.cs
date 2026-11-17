namespace DeskWallpaper.Nucleo;

/// <summary>
/// Amplía ×1,5, ×2, ×3 o ×4 con Real-ESRGAN (que siempre amplía ×4; para los demás factores cada bloque se reduce), por bloques solapados para no agotar la memoria
/// de vídeo. De cada bloque solo se pega la parte central, así que no se ven costuras.
/// </summary>
public static class Ampliador
{
    public const int LadoMaximo = 16384;

    /// <summary>Píxeles de entrada que se añaden por cada lado del bloque para que los bordes tengan contexto.</summary>
    public const int Solape = 16;

    /// <summary>Factores que se ofrecen. ×1,5 deja justa una panorámica de 2560 px en dos monitores de 1920.</summary>
    public static readonly IReadOnlyList<double> Posibles = [1.5, 2, 3, 4];

    /// <summary>Los factores con los que el lado mayor no pasa de 16384 px; vacía si ninguno.</summary>
    public static IReadOnlyList<double> Factores(SizePx tamano) =>
        Posibles.Where(f => Destino(Math.Max(tamano.Ancho, tamano.Alto), f) <= LadoMaximo).ToList();

    /// <summary>
    /// El mayor factor (en décimas, hacia abajo) para la ampliación personalizada: hasta ×4, que es lo que el
    /// modelo amplía de una pasada, y sin que el lado mayor pase de 16384 px. 1 si ya no se puede ampliar.
    /// </summary>
    public static double FactorMaximo(SizePx tamano) =>
        Math.Clamp(Math.Floor(LadoMaximo * 10.0 / Math.Max(1, Math.Max(tamano.Ancho, tamano.Alto))) / 10, 1, 4);

    /// <summary>Tamaño del resultado: cada lado multiplicado y redondeado.</summary>
    public static SizePx Resultado(SizePx tamano, double factor) => new(Destino(tamano.Ancho, factor), Destino(tamano.Alto, factor));

    private static int Destino(int x, double factor) => (int)Math.Round(x * factor);

    /// <summary>
    /// El menor factor posible que deja la imagen sin estirar en su zona (≥ <paramref name="factorZona"/>);
    /// sin zona, ×2; si no llega ninguno, el mayor posible; 0 si no se puede ampliar.
    /// </summary>
    public static double Propuesto(SizePx tamano, double? factorZona)
    {
        var factores = Factores(tamano);
        if (factores.Count == 0) return 0;
        if (factorZona is not { } f) return factores.Contains(2) ? 2 : factores[^1];
        // Una milésima de margen: el factor de la zona sale de una división (1,4999 es ×1,5).
        return factores.FirstOrDefault(x => x >= f - 0.001, factores[^1]);
    }

    /// <summary>Número de bloques que hará <see cref="Ampliar"/> con bloques de <paramref name="bloque"/> px.</summary>
    public static int Bloques(SizePx tamano, int bloque) =>
        ((tamano.Ancho + bloque - 1) / bloque) * ((tamano.Alto + bloque - 1) / bloque);

    /// <exception cref="OperationCanceledException">Si se cancela entre dos bloques.</exception>
    public static ImagenRgba Ampliar(ImagenRgba img, IModeloAmpliacion modelo, double factor,
                                     IProgress<double>? progreso, CancellationToken ct)
    {
        if (factor <= 1 || factor > 4) throw new ArgumentException("Solo se puede ampliar hasta ×4", nameof(factor));

        var bloque = modelo.Bloque;
        var final = Resultado(img.Tamano, factor);
        var salida = new byte[(long)final.Ancho * final.Alto * 4];
        var anchoSalida = final.Ancho;
        var total = Bloques(img.Tamano, bloque);
        var hechos = 0;

        for (var by = 0; by < img.Alto; by += bloque)
            for (var bx = 0; bx < img.Ancho; bx += bloque)
            {
                ct.ThrowIfCancellationRequested();
                var nucleo = new RectPx(bx, by, Math.Min(bloque, img.Ancho - bx), Math.Min(bloque, img.Alto - by));
                var x0 = Math.Max(0, bx - Solape);
                var y0 = Math.Max(0, by - Solape);
                var conSolape = new RectPx(x0, y0,
                    Math.Min(img.Ancho, nucleo.X + nucleo.Ancho + Solape) - x0,
                    Math.Min(img.Alto, nucleo.Y + nucleo.Alto + Solape) - y0);

                var ampliado = TensorCHW.AImagen(modelo.Ampliar(TensorCHW.Desde(img.Recorte(conSolape)), conSolape.Ancho, conSolape.Alto),
                                                 conSolape.Ancho * 4, conSolape.Alto * 4, 255f);
                // Cada píxel x de la imagen va a Destino(x) del resultado; el bloque con su solape se escala a su
                // tamaño de destino en el acto, así nunca hay en memoria la imagen ×4 entera.
                var tx0 = Destino(conSolape.X, factor);
                var ty0 = Destino(conSolape.Y, factor);
                var tamTrozo = new SizePx(Destino(conSolape.X + conSolape.Ancho, factor) - tx0,
                                          Destino(conSolape.Y + conSolape.Alto, factor) - ty0);
                if (factor != 4) ampliado = ArchivoImagen.Escalar(ampliado, tamTrozo);

                var dx0 = Destino(nucleo.X, factor);
                var dy0 = Destino(nucleo.Y, factor);
                var dx1 = Destino(nucleo.X + nucleo.Ancho, factor);
                var dy1 = Destino(nucleo.Y + nucleo.Alto, factor);
                var filaBytes = (dx1 - dx0) * 4;
                for (var y = dy0; y < dy1; y++)
                    Buffer.BlockCopy(ampliado.Pixeles, ((y - ty0) * ampliado.Ancho + dx0 - tx0) * 4,
                                     salida, (int)((long)y * anchoSalida * 4 + dx0 * 4L), filaBytes);

                progreso?.Report(++hechos / (double)total);
            }

        var resultado = new ImagenRgba(final.Ancho, final.Alto, salida);
        CopiarAlfa(img, resultado);
        return resultado;
    }

    /// <summary>La transparencia no pasa por el modelo: se escala aparte (solo si la imagen tiene).</summary>
    private static void CopiarAlfa(ImagenRgba origen, ImagenRgba destino)
    {
        var opaca = true;
        for (var i = 3; i < origen.Pixeles.Length && opaca; i += 4) opaca = origen.Pixeles[i] == 255;
        var d = destino.Pixeles;
        if (opaca)
        {
            for (var i = 3; i < d.Length; i += 4) d[i] = 255;
            return;
        }
        var escalada = ArchivoImagen.Escalar(origen, destino.Tamano).Pixeles;
        for (var i = 3; i < d.Length; i += 4) d[i] = escalada[i];
    }
}
