namespace DeskWallpaper.Nucleo;

/// <summary>Resultado de un borrado. <see cref="ZonaGrande"/>: avisar de que puede quedar borroso.</summary>
public sealed record ResultadoBorrado(ImagenRgba Imagen, bool ZonaGrande);

/// <summary>
/// Borra lo pintado con LaMa: se toma una zona de contexto alrededor de lo pintado,
/// se lleva a 512×512, se rellena y se vuelve a pegar mezclando solo lo pintado (con el borde suavizado).
/// El resto de la imagen queda idéntico píxel a píxel. Con marcas pequeñas, el contexto mide 512 px y no se escala.
/// </summary>
public static class Borrador
{
    public const int MargenMinimo = 32;
    public const int LadoZonaGrande = 2048;

    private const int Lado = IModeloRelleno.Lado;

    /// <summary>
    /// Zona alrededor de <paramref name="limites"/>: margen de max(32, lado mayor / 2) por cada lado y al menos
    /// 512 px, recortada al tamaño de la imagen y desplazada para quedar dentro.
    /// </summary>
    public static RectPx Contexto(RectPx limites, SizePx imagen)
    {
        var lado = LadoConMargen(limites);
        var ancho = Math.Min(Math.Max(Lado, lado), imagen.Ancho);
        var alto = Math.Min(Math.Max(Lado, lado), imagen.Alto);
        int Encajar(int centro, int tamano, int total) => Math.Clamp(centro - tamano / 2, 0, total - tamano);
        return new RectPx(
            Encajar(limites.X + limites.Ancho / 2, ancho, imagen.Ancho),
            Encajar(limites.Y + limites.Alto / 2, alto, imagen.Alto),
            ancho, alto);
    }

    /// <exception cref="InvalidOperationException">Si no hay nada pintado.</exception>
    public static ResultadoBorrado Borrar(ImagenRgba imagen, Mascara mascara, IModeloRelleno modelo)
    {
        var limites = mascara.Limites ?? throw new InvalidOperationException("No hay nada pintado");
        var contexto = Contexto(limites, imagen.Tamano);

        var entrada = ArchivoImagen.Escalar(imagen.Recorte(contexto), new SizePx(Lado, Lado));
        var salida = modelo.Rellenar(TensorCHW.Desde(entrada), MascaraReducida(mascara, contexto));
        // LaMa devuelve 0–255.
        var relleno = ArchivoImagen.Escalar(TensorCHW.AImagen(salida, Lado, Lado, 1f), new SizePx(contexto.Ancho, contexto.Alto));

        var resultado = imagen.Copia();
        var alfa = mascara.Alfa(contexto);
        var p = resultado.Pixeles;
        var r = relleno.Pixeles;
        for (var y = 0; y < contexto.Alto; y++)
            for (var x = 0; x < contexto.Ancho; x++)
            {
                var a = alfa[y * contexto.Ancho + x];
                if (a <= 0) continue;
                var i = ((contexto.Y + y) * imagen.Ancho + contexto.X + x) * 4;
                var j = (y * contexto.Ancho + x) * 4;
                for (var c = 0; c < 3; c++)
                    p[i + c] = (byte)Math.Round(p[i + c] + (r[j + c] - p[i + c]) * a);
                // El canal alfa (transparencia) se conserva.
            }

        return new ResultadoBorrado(resultado, LadoConMargen(limites) > LadoZonaGrande);
    }

    private static int LadoConMargen(RectPx limites)
    {
        var mayor = Math.Max(limites.Ancho, limites.Alto);
        return mayor + 2 * Math.Max(MargenMinimo, mayor / 2);
    }

    /// <summary>
    /// La máscara del contexto llevada a 512×512. Al reducir, un píxel cuenta como pintado si lo está
    /// cualquiera de los que cubre: así un trazo fino nunca desaparece.
    /// </summary>
    private static float[] MascaraReducida(Mascara m, RectPx contexto)
    {
        var t = new float[Lado * Lado];
        for (var j = 0; j < Lado; j++)
        {
            var y0 = j * contexto.Alto / Lado;
            var y1 = Math.Max(y0, (j + 1) * contexto.Alto / Lado - 1);
            for (var i = 0; i < Lado; i++)
            {
                var x0 = i * contexto.Ancho / Lado;
                var x1 = Math.Max(x0, (i + 1) * contexto.Ancho / Lado - 1);
                var pintado = false;
                for (var y = y0; y <= y1 && !pintado; y++)
                    for (var x = x0; x <= x1 && !pintado; x++)
                        pintado = m[contexto.X + x, contexto.Y + y];
                t[j * Lado + i] = pintado ? 1 : 0;
            }
        }
        return t;
    }
}
