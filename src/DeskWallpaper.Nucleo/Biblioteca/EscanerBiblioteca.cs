namespace DeskWallpaper.Nucleo;

/// <summary>Avance de un escaneo: archivos revisados, total y omitidos (dañados o ilegibles).</summary>
public sealed record ProgresoEscaneo(int Hechas, int Total, int Omitidas);

/// <summary>
/// Sincroniza la biblioteca con las carpetas vigiladas sin tocar los originales. Es incremental:
/// un archivo con el mismo tamaño y fecha que la última vez no se vuelve a leer.
/// </summary>
/// <param name="carpetaExcluida">La carpeta de datos de la aplicación: nunca se indexa, aunque esté dentro de una vigilada.</param>
/// <param name="fotogramas">Para recoger también los vídeos; sin él, solo imágenes.</param>
public sealed class EscanerBiblioteca(RepositorioBiblioteca repo, IRenderizador renderizador, string? carpetaExcluida = null,
                                      FotogramasVideo? fotogramas = null)
{
    /// <summary>Lado de la imagen reducida con la que se calcula la paleta.</summary>
    public const int LadoPaleta = 64;

    private readonly Lock _baseDeDatos = new();

    /// <summary>
    /// Si un cambio que avisa el vigilante de carpetas merece un escaneo: imágenes, vídeos y carpetas, pero nada de
    /// la carpeta de datos (generar miniaturas o fondos dentro de una carpeta vigilada provocaría escaneos sin fin).
    /// </summary>
    public static bool CambioInteresa(string ruta, string carpetaDatos) =>
        !Path.GetFullPath(ruta).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpetaDatos)) + "\\", StringComparison.OrdinalIgnoreCase)
        && (FormatosImagen.EsImagenAdmitida(ruta) || FormatosVideo.EsVideo(ruta) || !Path.HasExtension(ruta) || Directory.Exists(ruta));

    /// <summary>
    /// Añade o actualiza las imágenes de las carpetas y quita de la biblioteca las rutas que ya no
    /// existen o que ya no están en ninguna carpeta vigilada. Las carpetas que no existen (disco
    /// desconectado) se ignoran sin quitar nada de ellas.
    /// </summary>
    public Task<ProgresoEscaneo> EscanearAsync(IReadOnlyList<CarpetaVigilada> carpetas, IProgress<ProgresoEscaneo>? progreso,
                                               CancellationToken ct) =>
        Task.Run(() => Escanear(carpetas, progreso, ct), ct);

    private ProgresoEscaneo Escanear(IReadOnlyList<CarpetaVigilada> carpetas, IProgress<ProgresoEscaneo>? progreso, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var disponibles = carpetas.Where(c => Directory.Exists(c.Ruta)).ToList();
        var encontrados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in disponibles)
        {
            var opciones = new EnumerationOptions { RecurseSubdirectories = c.IncluirSubcarpetas, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(c.Ruta, "*", opciones)
                         .Where(f => FormatosImagen.EsImagenAdmitida(f) || (fotogramas is not null && FormatosVideo.EsVideo(f))))
            {
                var completa = Path.GetFullPath(f);
                if (carpetaExcluida is null || !Dentro(completa, carpetaExcluida)) encontrados.Add(completa);
            }
        }

        var total = encontrados.Count;
        int hechas = 0, omitidas = 0;
        // Leer y analizar imágenes es lo lento: va en paralelo. Las escrituras en la base de datos se
        // hacen de una en una (bloqueo) para no pelearse por el archivo.
        var opcionesParalelo = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
        };
        Parallel.ForEach(encontrados, opcionesParalelo, ruta =>
        {
            if (!Procesar(ruta)) Interlocked.Increment(ref omitidas);
            var n = Interlocked.Increment(ref hechas);
            if (n % 10 == 0 || n == total) progreso?.Report(new ProgresoEscaneo(n, total, Volatile.Read(ref omitidas)));
        });

        // Fuera de la biblioteca: lo que ya no existe dentro de una carpeta disponible y lo que ya no
        // está en ninguna carpeta vigilada. Lo de carpetas no disponibles se deja tal cual.
        // Un escaneo cancelado (lo sustituye otro con otras carpetas) no debe quitar nada.
        ct.ThrowIfCancellationRequested();
        var sobran = repo.Rutas().Where(ruta =>
            !encontrados.Contains(ruta)
            && (disponibles.Any(c => Dentro(ruta, c.Ruta)) || !carpetas.Any(c => Dentro(ruta, c.Ruta)))).ToList();
        ct.ThrowIfCancellationRequested();
        if (sobran.Count > 0) lock (_baseDeDatos) repo.QuitarRutas(sobran);

        fotogramas?.Recortar();   // la caché de fotogramas también tiene tope
        var final = new ProgresoEscaneo(hechas, total, omitidas);
        progreso?.Report(final);
        return final;
    }

    /// <summary>True si la imagen está (o queda) en la biblioteca; false si se ha omitido por no poder leerse.</summary>
    private bool Procesar(string ruta)
    {
        try
        {
            var info = new FileInfo(ruta);
            (long Bytes, DateTime Modificado)? firma;
            lock (_baseDeDatos) firma = repo.FirmaDeRuta(ruta);
            var esVideo = fotogramas is not null && FormatosVideo.EsVideo(ruta);
            if (firma is { } f && f.Bytes == info.Length && f.Modificado == info.LastWriteTimeUtc)
            {
                // Un vídeo guardado sin duración (versiones anteriores del ayudante no la sacaban de los MP4 que no la
                // declaran) se vuelve a leer aunque no haya cambiado.
                long? duracion = null;
                if (esVideo) lock (_baseDeDatos) duracion = repo.DuracionMsDeRuta(ruta);
                if (!esVideo || duracion is not 0) return true;
            }

            var huella = Huella.Calcular(ruta);
            if (esVideo)
            {
                // Un vídeo: tamaño, duración y colores salen de su fotograma (lo saca el ayudante, como al reproducir).
                var (fotograma, video) = fotogramas!.Preparar(ruta);
                var coloresVideo = PaletaColor.Calcular(renderizador.PixelesReducidos(fotograma, LadoPaleta));
                lock (_baseDeDatos)
                    repo.Guardar(huella, ruta, video.Tamano, coloresVideo, info.Length, info.LastWriteTimeUtc, (long)video.Duracion.TotalMilliseconds);
                return true;
            }
            var tamano = renderizador.LeerTamano(ruta);
            var colores = PaletaColor.Calcular(renderizador.PixelesReducidos(ruta, LadoPaleta));
            lock (_baseDeDatos) repo.Guardar(huella, ruta, tamano, colores, info.Length, info.LastWriteTimeUtc);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cualquier fallo con un archivo (dañado, ilegible, tan grande que no cabe en memoria…) lo omite a él
            // solo: si saliera de aquí, abortaría el escaneo entero y nunca entrarían las demás.
            return false;
        }
    }

    private static bool Dentro(string ruta, string carpeta)
    {
        var prefijo = Path.GetFullPath(carpeta).TrimEnd('\\', '/') + "\\";
        return ruta.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase);
    }
}
