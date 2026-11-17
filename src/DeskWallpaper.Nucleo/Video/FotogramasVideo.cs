using System.Text.Json;

namespace DeskWallpaper.Nucleo;

/// <summary>
/// Fotograma de referencia de cada vídeo: el programa «ve» un vídeo como
/// este JPEG de su tamaño nativo en el lienzo, el encuadre, el historial y el fondo de reserva. Se guarda por huella en
/// <c>cache\fotogramas</c> junto a un JSON con el tamaño y la duración; se puede borrar cuando se quiera, porque se
/// vuelve a sacar al necesitarlo.
/// </summary>
public sealed class FotogramasVideo(string carpetaDatos, ILectorVideo lector)
{
    /// <summary>Tope de la caché de fotogramas (un fotograma 4K en JPEG ronda 1 MB).</summary>
    public const long TopeBytes = 500L * 1024 * 1024;

    public string Carpeta => Path.Combine(carpetaDatos, "cache", "fotogramas");

    private sealed record Datos(int Ancho, int Alto, long DuracionMs);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <exception cref="ExcepcionImagen">Si el vídeo no se puede leer (no deja nada en la caché).</exception>
    public (string RutaFotograma, InfoVideo Info) Preparar(string rutaVideo)
    {
        string huella;
        try { huella = Huella.Calcular(rutaVideo); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExcepcionImagen($"No se puede leer el vídeo «{Path.GetFileName(rutaVideo)}»", ex);
        }
        var jpg = Path.Combine(Carpeta, huella + ".jpg");
        var json = Path.ChangeExtension(jpg, ".json");

        // Sin duración se vuelve a leer: versiones anteriores del ayudante daban 0 en los MP4 que no la declaran.
        if (File.Exists(jpg) && File.Exists(json) && LeerDatos(json) is { DuracionMs: > 0 } guardados)
        {
            Tocar(jpg);
            Tocar(json);
            return (jpg, Info(guardados));
        }

        Directory.CreateDirectory(Carpeta);
        var temporal = Path.Combine(Carpeta, $"{huella}.{Guid.NewGuid():N}.tmp.jpg");
        try
        {
            var info = lector.Leer(rutaVideo, temporal);
            File.Move(temporal, jpg, overwrite: true);
            File.WriteAllText(json, JsonSerializer.Serialize(
                new Datos(info.Tamano.Ancho, info.Tamano.Alto, (long)info.Duracion.TotalMilliseconds), Json));
            return (jpg, info);
        }
        finally
        {
            try { if (File.Exists(temporal)) File.Delete(temporal); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* queda un temporal */ }
        }
    }

    /// <summary>Borra los fotogramas usados hace más tiempo hasta quedar por debajo del tope.</summary>
    public void Recortar(long topeBytes = TopeBytes)
    {
        if (!Directory.Exists(Carpeta)) return;
        var pares = new DirectoryInfo(Carpeta).EnumerateFiles("*.jpg")
            .Where(f => !f.Name.Contains(".tmp.", StringComparison.Ordinal))
            .Select(f => (Jpg: f, Json: new FileInfo(Path.ChangeExtension(f.FullName, ".json"))))
            .OrderBy(p => p.Jpg.LastWriteTimeUtc)
            .ToList();
        var total = pares.Sum(p => p.Jpg.Length + (p.Json.Exists ? p.Json.Length : 0));
        foreach (var (jpg, json) in pares)
        {
            if (total <= topeBytes) break;
            total -= jpg.Length + (json.Exists ? json.Length : 0);
            try
            {
                jpg.Delete();
                if (json.Exists) json.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* en uso: se queda */ }
        }
    }

    private static Datos? LeerDatos(string json)
    {
        try { return JsonSerializer.Deserialize<Datos>(File.ReadAllText(json), Json); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static InfoVideo Info(Datos d) => new(new SizePx(d.Ancho, d.Alto), TimeSpan.FromMilliseconds(d.DuracionMs));

    /// <summary>Marca el uso para el recorte (los más antiguos se borran primero).</summary>
    private static void Tocar(string ruta)
    {
        try { File.SetLastWriteTimeUtc(ruta, DateTime.UtcNow); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* da igual */ }
    }
}
