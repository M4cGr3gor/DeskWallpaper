namespace DeskWallpaper.Nucleo;

/// <summary>Una imagen de la biblioteca (identificada por su huella, no por su ruta).</summary>
public sealed record ImagenBiblioteca(long Id, string Huella, string Ruta, SizePx Tamano, IReadOnlyList<ColorNombre> Colores,
                                      bool Favorita, IReadOnlyList<string> Etiquetas, DateTime Anadida)
{
    public string Nombre => Path.GetFileName(Ruta);

    /// <summary>Un fondo animado: va a su propia pestaña y su tamaño y colores salen de su fotograma.</summary>
    public bool EsVideo => FormatosVideo.EsVideo(Ruta);

    /// <summary>Duración de un vídeo en milisegundos (0 en las imágenes).</summary>
    public long DuracionMs { get; init; }

    /// <summary>False si ninguna de sus rutas existe ahora (disco desconectado, carpeta de red caída…).</summary>
    public bool Disponible { get; init; } = true;
}
