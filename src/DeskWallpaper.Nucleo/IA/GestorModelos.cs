using System.Security.Cryptography;

namespace DeskWallpaper.Nucleo;

/// <summary>Fallo al descargar un modelo, con el mensaje en español para el usuario.</summary>
public sealed class ExcepcionModelo(string mensaje, Exception? causa = null) : Exception(mensaje, causa);

/// <summary>Cuánto se ha descargado. <see cref="Comprobando"/>: ya ha llegado todo y se valida la huella.</summary>
public sealed record ProgresoDescarga(long Recibidos, long Total, bool Comprobando = false)
{
    public double Fraccion => Total > 0 ? Math.Clamp((double)Recibidos / Total, 0, 1) : 0;

    public string Texto => Comprobando
        ? "Comprobando que el archivo es correcto…"
        : $"Descargando: {Megas(Recibidos)} MB de {Megas(Total)} MB";

    private static long Megas(long bytes) => (long)Math.Round(bytes / (1024.0 * 1024.0));
}

/// <summary>
/// Modelos de IA en <c>&lt;datos&gt;\modelos</c>: se descargan la primera vez, desde una
/// dirección fija, a un «.part» que solo se renombra si su SHA-256 es el esperado. Nunca queda un modelo a medias.
/// </summary>
public sealed class GestorModelos(string carpeta, HttpMessageHandler manejador)
{
    /// <summary>Si el servidor deja de enviar datos este tiempo, la descarga se da por perdida.</summary>
    public static readonly TimeSpan TiempoSinDatos = TimeSpan.FromSeconds(30);

    private const string Prefijo = "No se ha podido descargar el modelo: ";

    private readonly HttpClient _http = new(manejador, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };

    public string Carpeta { get; } = carpeta;

    public string Ruta(ModeloIA m) => Path.Combine(Carpeta, m.Archivo);

    /// <summary>Todos sus archivos existen y tienen el tamaño esperado (la huella se comprobó al descargarlos).</summary>
    public bool EstaDescargado(ModeloIA m) => m.Archivos.All(a =>
    {
        var info = new FileInfo(Path.Combine(Carpeta, a.Nombre));
        return info.Exists && info.Length == a.Bytes;
    });

    /// <summary>Queda algún archivo suyo (completo, a medias o suelto): se puede eliminar.</summary>
    public bool HayArchivos(ModeloIA m) => m.Archivos.Any(a =>
        File.Exists(Path.Combine(Carpeta, a.Nombre)) || File.Exists(Path.Combine(Carpeta, a.Nombre) + ".part"));

    /// <summary>Borra el modelo (y un «.part» que hubiera quedado). Se vuelve a descargar la próxima vez que se use.</summary>
    /// <exception cref="ExcepcionModelo">Si está en uso (la ventana de edición lo tiene cargado).</exception>
    public void Eliminar(ModeloIA m)
    {
        // Todo o nada: primero se apartan todos (renombrar un archivo abierto falla igual que borrarlo); si uno
        // no se puede, se devuelven los apartados y no se borra ninguno. Así no queda medio modelo.
        var apartados = new List<(string Original, string Apartado)>();
        try
        {
            foreach (var a in m.Archivos)
                foreach (var ruta in new[] { Path.Combine(Carpeta, a.Nombre), Path.Combine(Carpeta, a.Nombre) + ".part" })
                    if (File.Exists(ruta))
                    {
                        var apartado = ruta + ".borrar";
                        File.Move(ruta, apartado, overwrite: true);
                        apartados.Add((ruta, apartado));
                    }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var (original, apartado) in apartados)
            {
                try { File.Move(apartado, original); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* se queda apartado */ }
            }
            throw new ExcepcionModelo("No se puede eliminar el modelo porque se está usando. Cierra la ventana de edición y vuelve a intentarlo.", ex);
        }
        foreach (var (_, apartado) in apartados)
        {
            try { File.Delete(apartado); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* ya no cuenta como modelo */ }
        }
    }

    /// <returns>La ruta del modelo ya comprobado.</returns>
    /// <exception cref="ExcepcionModelo">Con el motivo en español; no queda ningún archivo.</exception>
    /// <exception cref="OperationCanceledException">Si se cancela; tampoco queda nada.</exception>
    public async Task<string> DescargarAsync(ModeloIA m, IProgress<ProgresoDescarga>? progreso, CancellationToken ct)
    {
        try { Directory.CreateDirectory(Carpeta); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExcepcionModelo("No se puede escribir en la carpeta de modelos", ex);
        }

        // Todo o nada: si falla un archivo, se borran también los que ya se habían bajado en esta descarga.
        var bajados = new List<string>();
        long previos = 0;
        try
        {
            foreach (var a in m.Archivos)
            {
                bajados.Add(await DescargarArchivoAsync(a, previos, m.Bytes, progreso, ct));
                previos += a.Bytes;
            }
            return Ruta(m);
        }
        catch
        {
            foreach (var ruta in bajados)
            {
                try { File.Delete(ruta); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* EstaDescargado ya dará false */ }
            }
            throw;
        }
    }

    /// <summary>Un archivo: «.part», huella y nombre final. <paramref name="previos"/>: bytes de los archivos anteriores.</summary>
    private async Task<string> DescargarArchivoAsync(ArchivoModelo a, long previos, long total,
                                                     IProgress<ProgresoDescarga>? progreso, CancellationToken ct)
    {
        var destino = Path.Combine(Carpeta, a.Nombre);
        var temporal = destino + ".part";
        try
        {
            using var respuesta = await PedirAsync(a.Url, ct);
            if (!respuesta.IsSuccessStatusCode) throw new ExcepcionModelo($"{Prefijo}error {(int)respuesta.StatusCode}");

            using var huella = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await CopiarAsync(respuesta.Content, temporal, huella, a.Bytes, previos, total, progreso, ct);

            progreso?.Report(new ProgresoDescarga(previos + a.Bytes, total, Comprobando: true));
            if (!string.Equals(Convert.ToHexStringLower(huella.GetHashAndReset()), a.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ExcepcionModelo($"{Prefijo}el archivo descargado no es el esperado");

            File.Move(temporal, destino, overwrite: true);
            return destino;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ExcepcionModelo("No se puede escribir en la carpeta de modelos", ex);
        }
        finally
        {
            try { if (File.Exists(temporal)) File.Delete(temporal); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* queda un .part, que no se usa */ }
        }
    }

    private async Task<HttpResponseMessage> PedirAsync(Uri url, CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
        peticion.Headers.UserAgent.ParseAdd(BuscadorOnline.AgenteUsuario);
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(BuscadorOnline.TiempoMaximo);
        try
        {
            return await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, limite.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExcepcionModelo($"{Prefijo}el servidor no responde");
        }
        catch (HttpRequestException ex)
        {
            throw new ExcepcionModelo($"{Prefijo}sin conexión", ex);
        }
    }

    private static async Task CopiarAsync(HttpContent contenido, string temporal, IncrementalHash huella, long bytesArchivo,
                                          long previos, long total, IProgress<ProgresoDescarga>? progreso, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var archivo = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        limite.CancelAfter(TiempoSinDatos);
        await using var flujo = await DeLaRedAsync(() => contenido.ReadAsStreamAsync(limite.Token), ct);
        var bufer = new byte[1 << 16];
        long recibidos = 0;
        while (true)
        {
            limite.CancelAfter(TiempoSinDatos);   // el plazo cuenta desde el último trozo recibido
            var leidos = await DeLaRedAsync(() => flujo.ReadAsync(bufer, limite.Token).AsTask(), ct);
            if (leidos == 0) break;
            recibidos += leidos;
            if (recibidos > bytesArchivo) throw new ExcepcionModelo($"{Prefijo}el archivo descargado no es el esperado");
            huella.AppendData(bufer, 0, leidos);
            await archivo.WriteAsync(bufer.AsMemory(0, leidos), ct);   // un fallo aquí es del disco
            progreso?.Report(new ProgresoDescarga(previos + recibidos, total));
        }
    }

    /// <summary>Lectura de la red con sus errores traducidos: corte de conexión o servidor que deja de enviar.</summary>
    private static async Task<T> DeLaRedAsync<T>(Func<Task<T>> leer, CancellationToken ct)
    {
        try
        {
            return await leer();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ExcepcionModelo($"{Prefijo}el servidor ha dejado de enviar el modelo");
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            throw new ExcepcionModelo($"{Prefijo}se ha cortado la conexión", ex);
        }
    }
}
