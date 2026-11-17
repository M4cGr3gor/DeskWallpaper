using SkiaSharp;

namespace DeskWallpaper.Nucleo;

/// <summary>Renderizador basado en SkiaSharp (remuestreo cúbico Catmull-Rom).</summary>
public sealed class RenderizadorSkia : IRenderizador
{
    private static readonly SKSamplingOptions Cubico = new(SKCubicResampler.CatmullRom);
    private static readonly SKSamplingOptions Lineal = new(SKFilterMode.Linear, SKMipmapMode.None);

    /// <summary>
    /// PNG rápido: estos PNG no se guardan para siempre (pantalla, fondos que se aplican), así que se comprime poco.
    /// Con «calidad 100» una panorámica de 7680×2160 tardaba unos 2,4 s; así, unos 0,35 s.
    /// </summary>
    private static readonly SKPngEncoderOptions PngRapido = new(SKPngEncoderFilterFlags.Sub, 1);

    public SizePx LeerTamano(string ruta)
    {
        using var codec = AbrirCodec(ruta, out var flujo);
        using (flujo)
        {
            var info = codec.Info;
            return GiraNoventa(codec.EncodedOrigin)
                ? new SizePx(info.Height, info.Width)
                : new SizePx(info.Width, info.Height);
        }
    }

    public void Renderizar(string rutaOrigen, RectD region, SizePx salida, string rutaDestino) =>
        Renderizar(rutaOrigen, [new Recorte(region, salida, rutaDestino)]);

    public void Renderizar(string rutaOrigen, IReadOnlyList<Recorte> recortes)
    {
        using var origen = Decodificar(rutaOrigen);
        foreach (var r in recortes)
        {
            using var datos = RenderizarRecorte(origen, Path.GetFileName(rutaOrigen), r.Region, r.Salida);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(r.RutaDestino))!);
            using var archivo = File.Create(r.RutaDestino);
            datos.SaveTo(archivo);
        }
    }

    public (byte[] Png, SizePx Original) Reducida(string ruta, int ladoMaximo)
    {
        using var origen = Decodificar(ruta);
        var original = new SizePx(origen.Width, origen.Height);
        using var datos = RenderizarRecorte(origen, Path.GetFileName(ruta), new RectD(0, 0, origen.Width, origen.Height),
                                            Reducir(origen, ladoMaximo));
        return (datos.ToArray(), original);
    }

    public byte[] Miniatura(string ruta, int lado)
    {
        // Decodificada ya reducida (en JPEG, escalado DCT): con miles de imágenes grandes es mucho
        // más rápido y gasta mucha menos memoria que decodificar a tamaño completo.
        using var origen = Decodificar(ruta, lado);
        using var imagen = RecortarImagen(origen, Path.GetFileName(ruta), new RectD(0, 0, origen.Width, origen.Height), Reducir(origen, lado));
        using var jpeg = imagen.Encode(SKEncodedImageFormat.Jpeg, 85);
        return jpeg.ToArray();
    }

    public byte[] PixelesReducidos(string ruta, int lado)
    {
        using var origen = Decodificar(ruta, lado);
        var salida = Reducir(origen, lado);
        var info = new SKImageInfo(salida.Ancho, salida.Alto, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var reducida = origen.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                             ?? throw new ExcepcionImagen($"No se puede reducir la imagen «{Path.GetFileName(ruta)}»");
        return reducida.Bytes;
    }

    public byte[] ComponerMiniatura(Combinacion c, Disposicion d, LienzoFisico l, int ancho)
    {
        var rects = d.Monitores.ToDictionary(m => m.Id, m => l.Monitor(m.Id), StringComparer.OrdinalIgnoreCase);
        var minX = rects.Values.Min(r => r.X);
        var minY = rects.Values.Min(r => r.Y);
        var escala = ancho / (rects.Values.Max(r => r.Derecha) - minX);
        var alto = Math.Max(1, (int)Math.Ceiling((rects.Values.Max(r => r.Abajo) - minY) * escala));
        SKRect APixeles(RectMm r) => SKRect.Create((float)((r.X - minX) * escala), (float)((r.Y - minY) * escala),
                                                   (float)(r.Ancho * escala), (float)(r.Alto * escala));

        using var superficie = SKSurface.Create(new SKImageInfo(ancho, alto, SKColorType.Rgba8888, SKAlphaType.Premul))
                              ?? throw new ExcepcionImagen("No se puede crear la superficie de dibujo");
        var lienzo = superficie.Canvas;
        lienzo.Clear(SKColors.Transparent);
        using (var vacio = new SKPaint { Color = new SKColor(0x2B, 0x2F, 0x36) })
            foreach (var r in rects.Values) lienzo.DrawRect(APixeles(r), vacio);

        foreach (var zona in c.Zonas.Where(z => z.MonitorIds.All(id => d.Buscar(id) is not null)))
        {
            using var origen = Decodificar(zona.RutaImagen);
            using var imagen = SKImage.FromBitmap(origen);
            var plan = PlanRecorte.Calcular(new SizePx(origen.Width, origen.Height), zona.MonitorIds, d, l, zona.Encuadre);
            foreach (var p in plan)
                lienzo.DrawImage(imagen,
                    SKRect.Create((float)p.Region.X, (float)p.Region.Y, (float)p.Region.Ancho, (float)p.Region.Alto),
                    APixeles(rects[p.MonitorId]), Cubico, null);
        }

        using var resultado = superficie.Snapshot();
        using var datos = PngDe(resultado);
        return datos.ToArray();
    }

    /// <summary>Tamaño de la imagen con su lado mayor reducido a <paramref name="lado"/> (nunca ampliada).</summary>
    private static SizePx Reducir(SKBitmap origen, int lado)
    {
        var escala = Math.Min(1.0, (double)lado / Math.Max(origen.Width, origen.Height));
        return new SizePx(Math.Max(1, (int)Math.Round(origen.Width * escala)), Math.Max(1, (int)Math.Round(origen.Height * escala)));
    }

    private static SKData RenderizarRecorte(SKBitmap origen, string nombre, RectD region, SizePx salida)
    {
        using var imagen = RecortarImagen(origen, nombre, region, salida);
        return PngDe(imagen);
    }

    private static SKData PngDe(SKImage imagen)
    {
        using var pixeles = imagen.PeekPixels() ?? throw new ExcepcionImagen("No se ha podido codificar la imagen");
        return pixeles.Encode(PngRapido) ?? throw new ExcepcionImagen("No se ha podido codificar la imagen");
    }

    private static SKImage RecortarImagen(SKBitmap origen, string nombre, RectD region, SizePx salida)
    {
        var actual = origen;
        var rx = region.X; var ry = region.Y; var rw = region.Ancho; var rh = region.Alto;

        try
        {
            // Reducciones grandes: primero a mitades sucesivas para evitar el aliasing.
            while (salida.Ancho / rw < 0.5 && actual.Width > 1 && actual.Height > 1)
            {
                var mitad = actual.Resize(new SKImageInfo(Math.Max(1, actual.Width / 2), Math.Max(1, actual.Height / 2)), Lineal)
                            ?? throw new ExcepcionImagen($"No se puede reducir la imagen «{nombre}»");
                var fx = (double)mitad.Width / actual.Width;
                var fy = (double)mitad.Height / actual.Height;
                rx *= fx; rw *= fx; ry *= fy; rh *= fy;
                if (!ReferenceEquals(actual, origen)) actual.Dispose();
                actual = mitad;
            }

            using var superficie = SKSurface.Create(new SKImageInfo(salida.Ancho, salida.Alto, SKColorType.Rgba8888, SKAlphaType.Premul))
                                  ?? throw new ExcepcionImagen("No se puede crear la superficie de dibujo");
            using (var imagen = SKImage.FromBitmap(actual))
            {
                superficie.Canvas.Clear(SKColors.Black);
                superficie.Canvas.DrawImage(imagen,
                    SKRect.Create((float)rx, (float)ry, (float)rw, (float)rh),
                    SKRect.Create(0, 0, salida.Ancho, salida.Alto),
                    Cubico, null);
            }

            return superficie.Snapshot();
        }
        finally
        {
            if (!ReferenceEquals(actual, origen)) actual.Dispose();
        }
    }

    private static SKCodec AbrirCodec(string ruta, out Stream flujo)
    {
        var nombre = Path.GetFileName(ruta);
        try
        {
            flujo = File.OpenRead(ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ExcepcionImagen($"No se puede leer la imagen «{nombre}»", ex);
        }

        var codec = SKCodec.Create(flujo);
        if (codec is null)
        {
            flujo.Dispose();
            throw new ExcepcionImagen($"No se puede leer la imagen «{nombre}»");
        }
        return codec;
    }

    /// <summary>
    /// Decodifica y aplica la orientación EXIF para que la imagen quede derecha. Con
    /// <paramref name="ladoMinimo"/>, el códec puede devolverla reducida (JPEG) siempre que su lado
    /// mayor siga siendo al menos el doble de ese valor, para remuestrear después con calidad.
    /// </summary>
    internal static SKBitmap Decodificar(string ruta, int? ladoMinimo = null)
    {
        using var codec = AbrirCodec(ruta, out var flujo);
        using (flujo)
        {
            var tamano = codec.Info.Size;
            if (ladoMinimo is { } lado && Math.Max(tamano.Width, tamano.Height) > 2 * lado)
            {
                var escalado = codec.GetScaledDimensions((float)(2.0 * lado / Math.Max(tamano.Width, tamano.Height)));
                if (Math.Max(escalado.Width, escalado.Height) >= 2 * lado) tamano = escalado;
            }
            var info = new SKImageInfo(tamano.Width, tamano.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            var bmp = new SKBitmap(info);
            var resultado = codec.GetPixels(info, bmp.GetPixels());
            // IncompleteInput (archivo truncado, p. ej. a medio descargar) también es un error:
            // si no, saldría de fondo con una franja vacía.
            if (resultado != SKCodecResult.Success)
            {
                bmp.Dispose();
                throw new ExcepcionImagen($"No se puede leer la imagen «{Path.GetFileName(ruta)}»");
            }
            return Orientar(bmp, codec.EncodedOrigin);
        }
    }

    private static bool GiraNoventa(SKEncodedOrigin o) =>
        o is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    private static SKBitmap Orientar(SKBitmap bmp, SKEncodedOrigin origen)
    {
        if (origen == SKEncodedOrigin.TopLeft) return bmp;

        var w = bmp.Width; var h = bmp.Height;
        var rotado = GiraNoventa(origen) ? new SKBitmap(h, w) : new SKBitmap(w, h);
        // x' = a·x + b·y + c ; y' = d·x + e·y + f (convenio EXIF de las 8 orientaciones)
        var m = origen switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        using (var c = new SKCanvas(rotado))
        using (var imagen = SKImage.FromBitmap(bmp))
        {
            c.SetMatrix(m);
            c.DrawImage(imagen, 0, 0, Lineal);
        }
        bmp.Dispose();
        return rotado;
    }
}
