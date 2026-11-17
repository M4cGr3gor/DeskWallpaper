using SkiaSharp;

namespace DeskWallpaper.Pruebas;

/// <summary>Crea imágenes sintéticas en una carpeta temporal con caracteres difíciles en la ruta.</summary>
internal sealed class ImagenesPrueba : IDisposable
{
    public string Carpeta { get; } =
        Path.Combine(Path.GetTempPath(), "dw-pruebas", $"Prueba & ñ á {Guid.NewGuid():N}");

    public ImagenesPrueba() => Directory.CreateDirectory(Carpeta);

    /// <summary>Imagen dividida en franjas verticales de colores iguales, de izquierda a derecha.</summary>
    public string Franjas(string nombre, int ancho, int alto, params SKColor[] colores)
    {
        var ruta = Path.Combine(Carpeta, nombre);
        using var bmp = new SKBitmap(ancho, alto);
        using (var lienzo = new SKCanvas(bmp))
        {
            var franja = (float)ancho / colores.Length;
            for (var i = 0; i < colores.Length; i++)
            {
                using var pintura = new SKPaint { Color = colores[i] };
                lienzo.DrawRect(i * franja, 0, franja, alto, pintura);
            }
        }
        using var datos = bmp.Encode(Path.GetExtension(nombre).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg, 95);
        using var archivo = File.Create(ruta);
        datos.SaveTo(archivo);
        return ruta;
    }

    /// <summary>Copia un JPEG añadiéndole una cabecera EXIF mínima con la orientación indicada (1-8).</summary>
    public string ConOrientacion(string jpeg, string nombre, ushort orientacion)
    {
        var original = File.ReadAllBytes(jpeg);
        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // "II*\0" + desplazamiento del IFD
            0x01, 0x00,                                     // 1 entrada
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, // etiqueta 0x0112 (Orientation), SHORT, 1 valor
            (byte)orientacion, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                         // sin más IFD
        ];
        var carga = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var longitud = carga.Length + 2;
        var resultado = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1, (byte)(longitud >> 8), (byte)longitud };
        resultado.AddRange(carga);
        resultado.AddRange(original.Skip(2));
        var ruta = Path.Combine(Carpeta, nombre);
        File.WriteAllBytes(ruta, resultado.ToArray());
        return ruta;
    }

    public string Texto(string nombre, string contenido)
    {
        var ruta = Path.Combine(Carpeta, nombre);
        File.WriteAllText(ruta, contenido);
        return ruta;
    }

    public static SKColor Pixel(string ruta, int x, int y)
    {
        using var bmp = SKBitmap.Decode(ruta);
        return bmp.GetPixel(x, y);
    }

    public static (int Ancho, int Alto) Tamano(string ruta)
    {
        using var codec = SKCodec.Create(ruta);
        return (codec.Info.Width, codec.Info.Height);
    }

    /// <summary>¿El color es claramente rojo / azul? (tolerante a la compresión JPEG).</summary>
    public static bool EsRojo(SKColor c) => c.Red > 200 && c.Green < 60 && c.Blue < 60;
    public static bool EsAzul(SKColor c) => c.Blue > 200 && c.Red < 60 && c.Green < 60;

    public void Dispose()
    {
        try { Directory.Delete(Carpeta, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
