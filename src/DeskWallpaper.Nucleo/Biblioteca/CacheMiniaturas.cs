namespace DeskWallpaper.Nucleo;

/// <summary>
/// Miniaturas de la biblioteca en <c>cache\miniaturas\{huella}.jpg</c>, con tope de tamaño (la caché
/// nunca crece sin control). La fecha de modificación hace de "último uso".
/// </summary>
public sealed class CacheMiniaturas(string carpetaDatos, IRenderizador renderizador)
{
    public const int Lado = 256;

    private string Carpeta => Path.Combine(carpetaDatos, "cache", "miniaturas");

    /// <summary>Ruta de la miniatura; la genera si no existe y la marca como usada.</summary>
    /// <exception cref="ExcepcionImagen">La imagen original no se puede leer.</exception>
    public string Obtener(string huella, string rutaImagen)
    {
        var ruta = Path.Combine(Carpeta, huella + ".jpg");
        if (File.Exists(ruta))
        {
            try
            {
                File.SetLastWriteTimeUtc(ruta, DateTime.UtcNow);
                return ruta;
            }
            catch (FileNotFoundException) { /* la acaba de borrar un recorte: se vuelve a generar */ }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ruta; /* solo afecta al orden de recorte */ }
        }

        var jpeg = renderizador.Miniatura(rutaImagen, Lado);
        Directory.CreateDirectory(Carpeta);
        var temporal = ruta + "." + Guid.NewGuid().ToString("N")[..6] + ".tmp";
        File.WriteAllBytes(temporal, jpeg);
        File.Move(temporal, ruta, overwrite: true);
        return ruta;
    }

    public long BytesOcupados() =>
        Directory.Exists(Carpeta) ? new DirectoryInfo(Carpeta).EnumerateFiles("*.jpg").Sum(f => f.Length) : 0;

    /// <summary>Si se supera el tope, borra las de uso más antiguo hasta quedar en el 90 % del tope.</summary>
    public void Recortar(long topeBytes)
    {
        if (!Directory.Exists(Carpeta)) return;
        var archivos = new DirectoryInfo(Carpeta).EnumerateFiles("*.jpg").OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = archivos.Sum(f => f.Length);
        if (total <= topeBytes) return;

        var objetivo = (long)(topeBytes * 0.9);
        foreach (var f in archivos)
        {
            if (total <= objetivo) break;
            try
            {
                var largo = f.Length;
                f.Delete();
                total -= largo;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* en uso: se intenta la siguiente */ }
        }
    }

    public void Vaciar()
    {
        if (!Directory.Exists(Carpeta)) return;
        foreach (var f in Directory.EnumerateFiles(Carpeta))
        {
            try { File.Delete(f); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
