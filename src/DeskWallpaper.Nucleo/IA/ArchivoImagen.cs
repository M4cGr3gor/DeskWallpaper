using System.Runtime.InteropServices;
using SkiaSharp;

namespace DeskWallpaper.Nucleo;

/// <summary>
/// Leer, escalar y guardar imágenes para la edición con IA: se lee ya
/// orientada según su EXIF y se guarda como archivo nuevo, en el formato que indique la extensión,
/// sin metadatos y nunca encima del original.
/// </summary>
public static class ArchivoImagen
{
    public const int CalidadJpeg = 95;
    public const int CalidadWebp = 95;

    private static readonly SKSamplingOptions Cubico = new(SKCubicResampler.CatmullRom);

    /// <exception cref="ExcepcionImagen">No se puede leer o no es una imagen.</exception>
    public static ImagenRgba Leer(string ruta)
    {
        using var bmp = RenderizadorSkia.Decodificar(ruta);
        return DeBitmap(bmp);
    }

    /// <summary>Remuestreo Catmull-Rom (el mismo que el aplicador), con el canal alfa incluido.</summary>
    public static ImagenRgba Escalar(ImagenRgba img, SizePx tamano)
    {
        if (img.Tamano == tamano) return img.Copia();
        using var origen = ABitmap(img);
        var info = new SKImageInfo(tamano.Ancho, tamano.Alto, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var escalada = origen.Resize(info, Cubico) ?? throw new ExcepcionEdicion("No se puede escalar la imagen");
        return DeBitmap(escalada);
    }

    /// <summary>
    /// Guarda en <paramref name="destino"/> con el formato de su extensión (JPG y WebP calidad 95, PNG sin pérdida, BMP).
    /// Escribe en un temporal y lo mueve, así que un fallo no deja un archivo a medias.
    /// </summary>
    /// <exception cref="ExcepcionEdicion">Si es la ruta del original o no se puede escribir.</exception>
    public static void Guardar(ImagenRgba img, string destino, string rutaOriginal)
    {
        if (MismoArchivo(destino, rutaOriginal))
            throw new ExcepcionEdicion("No se puede guardar encima del original") { EsElOriginal = true };

        var datos = Codificar(img, Path.GetExtension(destino).ToLowerInvariant());
        var temporal = destino + ".part";
        try
        {
            File.WriteAllBytes(temporal, datos);
            File.Move(temporal, destino, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ExcepcionEdicion("No se ha podido guardar la imagen", ex);
        }
        finally
        {
            try { if (File.Exists(temporal)) File.Delete(temporal); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* queda un .part, no la imagen */ }
        }
    }

    /// <summary>
    /// True si las dos rutas llevan al mismo archivo. Comparar el texto no basta: una unión, un enlace, una unidad
    /// asignada frente a su ruta de red o un nombre corto 8.3 llevan al original con otro texto. Si los dos existen,
    /// se compara su identidad (volumen e índice del archivo).
    /// </summary>
    internal static bool MismoArchivo(string a, string b)
    {
        if (string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase)) return true;
        if (!File.Exists(a) || !File.Exists(b)) return false;
        try
        {
            return Identidad(a) == Identidad(b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static (uint Volumen, uint Alto, uint Bajo) Identidad(string ruta)
    {
        using var h = File.OpenHandle(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(h, out var info)) throw new IOException($"No se puede leer «{ruta}»");
        return (info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InfoArchivo
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle archivo, out InfoArchivo info);

    /// <summary>
    /// Ruta propuesta junto al original: «nombre (editada).ext» si se ha borrado algo, «nombre (×2).ext» si solo
    /// se ha ampliado; « (2)», « (3)»… si ya existe.
    /// </summary>
    public static string NombrePropuesto(string rutaOriginal, bool hayBorrado, double factor)
    {
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(rutaOriginal))!;
        var sufijo = hayBorrado || factor <= 1 ? "editada" : "×" + TextoFactor(factor);
        var nombre = $"{Path.GetFileNameWithoutExtension(rutaOriginal)} ({sufijo}){Path.GetExtension(rutaOriginal)}";
        return Path.Combine(carpeta, DescargadorImagenes.NombreLibre(carpeta, nombre));
    }

    /// <summary>«1,5», «2», «3»… con coma decimal, como se escribe en español.</summary>
    public static string TextoFactor(double factor) =>
        factor.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-ES"));

    private static byte[] Codificar(ImagenRgba img, string extension)
    {
        if (extension == ".bmp") return Bmp(img);
        var (formato, calidad) = extension switch
        {
            ".jpg" or ".jpeg" => (SKEncodedImageFormat.Jpeg, CalidadJpeg),
            ".png" => (SKEncodedImageFormat.Png, 0),
            ".webp" => (SKEncodedImageFormat.Webp, CalidadWebp),
            _ => throw new ExcepcionEdicion($"No se puede guardar en formato «{extension}»"),
        };
        using var bmp = ABitmap(img);
        if (formato == SKEncodedImageFormat.Png)
        {
            // Compresión 3 de 9: el archivo pesa casi lo mismo que con la máxima y una imagen ampliada ×4 se
            // guarda en una fracción del tiempo.
            using var pixeles = bmp.PeekPixels();
            using var png = pixeles.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 3))
                            ?? throw new ExcepcionEdicion("No se ha podido codificar la imagen");
            return png.ToArray();
        }
        using var datos = bmp.Encode(formato, calidad) ?? throw new ExcepcionEdicion("No se ha podido codificar la imagen");
        return datos.ToArray();
    }

    /// <summary>BMP de 32 bits (BITMAPV4HEADER con máscaras), de arriba abajo: Skia no codifica BMP.</summary>
    private static byte[] Bmp(ImagenRgba img)
    {
        const int cabecera = 14 + 108;
        var tamanoPixeles = img.Ancho * img.Alto * 4;
        var datos = new byte[cabecera + tamanoPixeles];
        var w = new BinaryWriter(new MemoryStream(datos));
        w.Write((byte)'B'); w.Write((byte)'M');
        w.Write(datos.Length); w.Write(0); w.Write(cabecera);
        w.Write(108); w.Write(img.Ancho); w.Write(-img.Alto); // alto negativo: filas de arriba abajo
        w.Write((short)1); w.Write((short)32);
        w.Write(3); // BI_BITFIELDS
        w.Write(tamanoPixeles); w.Write(2835); w.Write(2835); w.Write(0); w.Write(0);
        w.Write(0x00FF0000); w.Write(0x0000FF00); w.Write(0x000000FF); w.Write(unchecked((int)0xFF000000));
        w.Write(0x73524742); // 'sRGB'
        // El resto (extremos de color y gamma) se queda a cero.
        var p = img.Pixeles;
        for (int i = 0, o = cabecera; i < p.Length; i += 4, o += 4)
        {
            datos[o] = p[i + 2]; datos[o + 1] = p[i + 1]; datos[o + 2] = p[i]; datos[o + 3] = p[i + 3];
        }
        return datos;
    }

    internal static SKBitmap ABitmap(ImagenRgba img)
    {
        var bmp = new SKBitmap(new SKImageInfo(img.Ancho, img.Alto, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        Marshal.Copy(img.Pixeles, 0, bmp.GetPixels(), img.Pixeles.Length);
        return bmp;
    }

    internal static ImagenRgba DeBitmap(SKBitmap bmp)
    {
        var info = new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var datos = new byte[info.BytesSize];
        var mano = GCHandle.Alloc(datos, GCHandleType.Pinned);
        try
        {
            using var pixmap = bmp.PeekPixels();
            if (pixmap is null || !pixmap.ReadPixels(info, mano.AddrOfPinnedObject(), info.RowBytes, 0, 0))
                throw new ExcepcionEdicion("No se pueden leer los píxeles de la imagen");
        }
        finally
        {
            mano.Free();
        }
        return new ImagenRgba(bmp.Width, bmp.Height, datos);
    }
}
