namespace DeskWallpaper.Nucleo;

/// <summary>Una imagen ya guardada y comprobada.</summary>
public sealed record ImagenDescargada(string Ruta, SizePx Tamano);

/// <summary>Fallo de una descarga, con el mensaje en español para la barra de estado.</summary>
public sealed class ExcepcionDescarga(string mensaje, Exception? causa = null) : Exception(mensaje, causa);

/// <summary>
/// Descarga imágenes a una carpeta: escribe en un temporal ".part",
/// comprueba que es una imagen legible y solo entonces la renombra, sin sobrescribir nunca nada.
/// </summary>
public sealed class DescargadorImagenes(HttpMessageHandler manejador, IRenderizador renderizador)
{
    private readonly HttpClient _http = new(manejador, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Ninguna imagen de fondo razonable pesa más: así una descarga no se come la memoria.</summary>
    public const long TamanoMaximo = 200L * 1024 * 1024;

    /// <summary>Tiempo máximo para bajar el cuerpo: una descarga colgada no se queda "Descargando…" para siempre.</summary>
    public static readonly TimeSpan TiempoMaximoCuerpo = TimeSpan.FromMinutes(2);


    /// <summary>Admite http(s) y data:image/…;base64. Crea la carpeta si no existe.</summary>
    /// <exception cref="ExcepcionDescarga">El mensaje explica qué ha fallado; nunca queda ningún archivo.</exception>
    public async Task<ImagenDescargada> DescargarAsync(Uri origen, string carpeta, CancellationToken ct)
    {
        var (datos, nombreBase) = origen.Scheme == "data"
            ? DeDataUri(origen)
            : await DeInternetAsync(origen, ct);

        // La extensión sale del contenido, no de la dirección ni de lo que diga el servidor.
        var extension = Formato(datos) switch
        {
            null => throw new ExcepcionDescarga("El archivo descargado no es una imagen"),
            ".gif" => throw new ExcepcionDescarga("Es un GIF: DeskWallpaper usa imágenes JPG, PNG, WebP o BMP"),
            var e => e,
        };
        var nombre = nombreBase + extension;

        try { Directory.CreateDirectory(carpeta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExcepcionDescarga("No se puede escribir en la carpeta de descargas", ex);
        }

        var temporal = Path.Combine(carpeta, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            await File.WriteAllBytesAsync(temporal, datos, ct);
            SizePx tamano;
            try { tamano = renderizador.LeerTamano(temporal); }
            catch (ExcepcionImagen ex) { throw new ExcepcionDescarga("El archivo descargado no es una imagen", ex); }
            // La cabecera puede estar bien y faltar el final: se decodifica (reducida) para estar seguro.
            try { renderizador.PixelesReducidos(temporal, 16); }
            catch (ExcepcionImagen ex) { throw new ExcepcionDescarga("El archivo descargado está dañado o incompleto", ex); }

            // Si otro proceso ocupa el nombre justo entonces, se prueba el siguiente libre.
            for (var intento = 0; ; intento++)
            {
                var destino = Path.Combine(carpeta, NombreLibre(carpeta, nombre));
                try
                {
                    File.Move(temporal, destino, overwrite: false);
                    return new ImagenDescargada(destino, tamano);
                }
                catch (IOException) when (intento < 5 && File.Exists(destino)) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExcepcionDescarga("No se puede escribir en la carpeta de descargas", ex);
        }
        finally
        {
            try { if (File.Exists(temporal)) File.Delete(temporal); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* se queda un .part; no es la imagen */ }
        }
    }

    /// <summary>Tope de un vídeo descargado (un 4K de un par de minutos ronda los 500 MB).</summary>
    public const long TamanoMaximoVideo = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Descarga un vídeo a la carpeta (búsqueda de fondos animados): directo a un «.part» en disco, sin cargarlo en
    /// memoria, con progreso de 0 a 1 si el servidor dice el tamaño. Comprueba por el contenido que es un MP4/MOV o un
    /// WebM/MKV, le pone su extensión y lo renombra sin sobrescribir nada. Si falla o se cancela, no queda ningún archivo.
    /// </summary>
    /// <returns>La ruta del vídeo guardado.</returns>
    /// <exception cref="ExcepcionDescarga">El mensaje explica qué ha fallado.</exception>
    public async Task<string> DescargarVideoAsync(Uri origen, string carpeta, IProgress<double>? progreso, CancellationToken ct)
    {
        try { Directory.CreateDirectory(carpeta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExcepcionDescarga("No se puede escribir en la carpeta de descargas", ex);
        }

        var temporal = Path.Combine(carpeta, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, origen);
            peticion.Headers.UserAgent.ParseAdd(BuscadorOnline.AgenteUsuario);
            HttpResponseMessage respuesta;
            using (var limite = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                limite.CancelAfter(BuscadorOnline.TiempoMaximo);
                try { respuesta = await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, limite.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new ExcepcionDescarga("No se ha podido descargar: el servidor no responde");
                }
                catch (HttpRequestException ex) { throw new ExcepcionDescarga("No se ha podido descargar: sin conexión", ex); }
            }

            using (respuesta)
            {
                if (!respuesta.IsSuccessStatusCode)
                    throw new ExcepcionDescarga($"No se ha podido descargar: error {(int)respuesta.StatusCode}");
                var total = respuesta.Content.Headers.ContentLength;
                if (total > TamanoMaximoVideo) throw new ExcepcionDescarga("No se ha podido descargar: el vídeo es demasiado grande");
                try
                {
                    await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
                    await using var archivo = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    var bufer = new byte[81920];
                    long escritos = 0;
                    int leidos;
                    while ((leidos = await flujo.ReadAsync(bufer, ct)) > 0)
                    {
                        escritos += leidos;
                        if (escritos > TamanoMaximoVideo) throw new ExcepcionDescarga("No se ha podido descargar: el vídeo es demasiado grande");
                        await archivo.WriteAsync(bufer.AsMemory(0, leidos), ct);
                        if (total > 0) progreso?.Report(Math.Min(1, (double)escritos / total.Value));
                    }
                }
                catch (HttpRequestException ex) { throw new ExcepcionDescarga("No se ha podido descargar: se ha cortado la conexión", ex); }
            }

            var extension = FormatoVideo(temporal) ?? throw new ExcepcionDescarga("El archivo descargado no es un vídeo");
            var nombre = NombreVideo(Uri.UnescapeDataString(origen.Segments.LastOrDefault() ?? "").Trim('/')) + extension;
            for (var intento = 0; ; intento++)
            {
                var destino = Path.Combine(carpeta, NombreLibre(carpeta, nombre));
                try
                {
                    File.Move(temporal, destino, overwrite: false);
                    return destino;
                }
                catch (IOException) when (intento < 5 && File.Exists(destino)) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExcepcionDescarga("No se puede escribir en la carpeta de descargas", ex);
        }
        finally
        {
            try { if (File.Exists(temporal)) File.Delete(temporal); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* queda un .part; no es el vídeo */ }
        }
    }

    /// <summary>Por los primeros bytes: ".mp4" o ".mov" (caja «ftyp»), ".webm" o ".mkv" (EBML); null si no es un vídeo conocido.</summary>
    private static string? FormatoVideo(string ruta)
    {
        var d = new byte[64];
        int n;
        using (var f = File.OpenRead(ruta)) n = f.Read(d, 0, d.Length);
        if (n >= 12 && d.AsSpan(4, 4).SequenceEqual("ftyp"u8)) return d.AsSpan(8, 4).SequenceEqual("qt  "u8) ? ".mov" : ".mp4";
        if (n >= 4 && d.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
            return d.AsSpan(0, n).IndexOf("webm"u8) >= 0 ? ".webm" : ".mkv";
        return null;
    }

    /// <summary>Nombre válido en Windows, sin extensión de vídeo (la pone el contenido).</summary>
    private static string NombreVideo(string segmento)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(segmento.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (FormatosVideo.Extensiones.Contains(Path.GetExtension(limpio), StringComparer.OrdinalIgnoreCase))
            limpio = Path.GetFileNameWithoutExtension(limpio).Trim(' ', '.');
        if (limpio.Length == 0) limpio = "video";
        return limpio.Length > 120 ? limpio[..120] : limpio;
    }

    /// <summary>"foto.jpg" → "foto (2).jpg" si ya existe, y así sucesivamente.</summary>
    public static string NombreLibre(string carpeta, string nombre)
    {
        if (!File.Exists(Path.Combine(carpeta, nombre))) return nombre;
        var baseNombre = Path.GetFileNameWithoutExtension(nombre);
        var extension = Path.GetExtension(nombre);
        for (var n = 2; ; n++)
        {
            var candidato = $"{baseNombre} ({n}){extension}";
            if (!File.Exists(Path.Combine(carpeta, candidato))) return candidato;
        }
    }

    /// <summary>Deja como mucho los <paramref name="maximo"/> archivos más recientes (imágenes vistas en el visor).</summary>
    public static void RecortarTemporales(string carpeta, int maximo)
    {
        if (!Directory.Exists(carpeta)) return;
        foreach (var f in new DirectoryInfo(carpeta).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).Skip(maximo))
        {
            try { f.Delete(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* en uso: la próxima vez */ }
        }
    }

    private async Task<(byte[] Datos, string Nombre)> DeInternetAsync(Uri origen, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, origen);
        peticion.Headers.UserAgent.ParseAdd(BuscadorOnline.AgenteUsuario);
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(BuscadorOnline.TiempoMaximo);
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, limite.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExcepcionDescarga("No se ha podido descargar: el servidor no responde");
        }
        catch (HttpRequestException ex)
        {
            throw new ExcepcionDescarga("No se ha podido descargar: sin conexión", ex);
        }

        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
                throw new ExcepcionDescarga($"No se ha podido descargar: error {(int)respuesta.StatusCode}");
            if (respuesta.Content.Headers.ContentLength > TamanoMaximo)
                throw new ExcepcionDescarga("No se ha podido descargar: el archivo es demasiado grande");
            var datos = await LeerCuerpoAsync(respuesta.Content, ct);
            var segmento = Uri.UnescapeDataString(origen.Segments.LastOrDefault() ?? "").Trim('/');
            return (datos, Nombre(segmento));
        }
    }

    /// <summary>
    /// Lee el cuerpo con su propio límite de tiempo (más largo que el de 15 s de la cabecera: una imagen grande
    /// puede tardar) y de tamaño. Un corte de red da un error claro en español.
    /// </summary>
    private static async Task<byte[]> LeerCuerpoAsync(HttpContent contenido, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoMaximoCuerpo);
        try
        {
            await using var flujo = await contenido.ReadAsStreamAsync(limite.Token);
            using var memoria = new MemoryStream();
            var bufer = new byte[81920];
            int leidos;
            while ((leidos = await flujo.ReadAsync(bufer, limite.Token)) > 0)
            {
                if (memoria.Length + leidos > TamanoMaximo)
                    throw new ExcepcionDescarga("No se ha podido descargar: el archivo es demasiado grande");
                memoria.Write(bufer, 0, leidos);
            }
            return memoria.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExcepcionDescarga("No se ha podido descargar: el servidor ha dejado de enviar la imagen");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new ExcepcionDescarga("No se ha podido descargar: se ha cortado la conexión", ex);
        }
    }

    private static (byte[] Datos, string Nombre) DeDataUri(Uri origen)
    {
        // data:image/png;base64,AAAA…
        var texto = origen.OriginalString;
        var coma = texto.IndexOf(',', StringComparison.Ordinal);
        var cabecera = coma > 5 ? texto[5..coma] : "";
        if (coma < 0 || !cabecera.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            throw new ExcepcionDescarga("El archivo descargado no es una imagen");
        byte[] datos;
        try { datos = Convert.FromBase64String(Uri.UnescapeDataString(texto[(coma + 1)..])); }
        catch (FormatException ex) { throw new ExcepcionDescarga("El archivo descargado no es una imagen", ex); }
        return (datos, $"imagen-{DateTime.Now:yyyyMMdd-HHmmss}");
    }

    /// <summary>Nombre válido en Windows, sin extensión (la pone el contenido).</summary>
    private static string Nombre(string segmento)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(segmento.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        var sinExtension = FormatosImagen.Extensiones.Contains(Path.GetExtension(limpio).ToLowerInvariant()) || Path.GetExtension(limpio) is ".gif"
            ? Path.GetFileNameWithoutExtension(limpio)
            : limpio;
        sinExtension = sinExtension.Trim(' ', '.');
        if (sinExtension.Length == 0) sinExtension = "imagen";
        return sinExtension.Length > 120 ? sinExtension[..120] : sinExtension;
    }

    /// <summary>Formato por los primeros bytes: ".jpg", ".png", ".webp", ".bmp", ".gif" o null si no es una imagen conocida.</summary>
    private static string? Formato(byte[] d)
    {
        bool Empieza(int desde, params byte[] firma) => d.Length >= desde + firma.Length && d.AsSpan(desde, firma.Length).SequenceEqual(firma);
        if (Empieza(0, 0xFF, 0xD8, 0xFF)) return ".jpg";
        if (Empieza(0, 0x89, (byte)'P', (byte)'N', (byte)'G')) return ".png";
        if (Empieza(0, (byte)'R', (byte)'I', (byte)'F', (byte)'F') && Empieza(8, (byte)'W', (byte)'E', (byte)'B', (byte)'P')) return ".webp";
        if (Empieza(0, (byte)'B', (byte)'M')) return ".bmp";
        if (Empieza(0, (byte)'G', (byte)'I', (byte)'F', (byte)'8')) return ".gif";
        return null;
    }
}
