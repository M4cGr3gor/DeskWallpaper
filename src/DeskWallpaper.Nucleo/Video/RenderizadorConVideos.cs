namespace DeskWallpaper.Nucleo;

/// <summary>
/// Renderizador que trata cada vídeo como su fotograma de referencia (<see cref="FotogramasVideo"/>): así el lienzo,
/// el encuadre, el aplicador (fondo de reserva) y el historial funcionan con vídeos sin cambios. Las imágenes pasan tal cual.
/// </summary>
public sealed class RenderizadorConVideos(IRenderizador interior, FotogramasVideo fotogramas) : IRenderizador
{
    public FotogramasVideo Fotogramas => fotogramas;

    /// <summary>La ruta a usar para leer píxeles: el fotograma si es un vídeo, la misma si no.</summary>
    public string Ruta(string ruta) => FormatosVideo.EsVideo(ruta) ? fotogramas.Preparar(ruta).RutaFotograma : ruta;

    public SizePx LeerTamano(string ruta) => interior.LeerTamano(Ruta(ruta));

    public void Renderizar(string rutaOrigen, IReadOnlyList<Recorte> recortes) => interior.Renderizar(Ruta(rutaOrigen), recortes);

    public (byte[] Png, SizePx Original) Reducida(string ruta, int ladoMaximo) => interior.Reducida(Ruta(ruta), ladoMaximo);

    public byte[] PixelesReducidos(string ruta, int lado) => interior.PixelesReducidos(Ruta(ruta), lado);

    public byte[] Miniatura(string ruta, int lado) => interior.Miniatura(Ruta(ruta), lado);

    public byte[] ComponerMiniatura(Combinacion c, Disposicion d, LienzoFisico l, int ancho) =>
        interior.ComponerMiniatura(c with { Zonas = c.Zonas.Select(z => z with { RutaImagen = Ruta(z.RutaImagen) }).ToList() }, d, l, ancho);
}
