namespace DeskWallpaper.Nucleo;

/// <summary>
/// Encuadre con el ratón: desplazar y hacer zoom sin dejar bandas vacías y sin "saltos".
/// Todo resultado sale normalizado: su centro es el de la región que realmente se ve.
/// </summary>
public static class EncuadreInteractivo
{
    public const double ZoomMaximo = 8.0;

    /// <summary>
    /// Encuadre cuyo centro es el de la región ya limitada por <see cref="Recortador.RegionZona"/>.
    /// Sin esto, un centro "fuera" se quedaría guardado y el siguiente zoom haría saltar la imagen.
    /// </summary>
    public static Encuadre Normalizar(SizePx imagen, RectMm envolvente, Encuadre e)
    {
        var zoom = Math.Clamp(e.Zoom, 1.0, ZoomMaximo);
        var r = Recortador.RegionZona(imagen, envolvente, e with { Zoom = zoom });
        return new Encuadre((r.X + r.Ancho / 2) / imagen.Ancho, (r.Y + r.Alto / 2) / imagen.Alto, zoom);
    }

    /// <summary>Mueve la imagen: dx/dy en píxeles de la imagen (positivo = la imagen va a la derecha/abajo).</summary>
    public static Encuadre Desplazar(SizePx imagen, RectMm envolvente, Encuadre e, double dxImagen, double dyImagen) =>
        Normalizar(imagen, envolvente, e with
        {
            CentroX = e.CentroX - dxImagen / imagen.Ancho,
            CentroY = e.CentroY - dyImagen / imagen.Alto,
        });

    /// <summary>Multiplica el zoom (la rueda usa 1,1 por paso), limitado a [1, <see cref="ZoomMaximo"/>].</summary>
    public static Encuadre Zoom(SizePx imagen, RectMm envolvente, Encuadre e, double factor) =>
        Normalizar(imagen, envolvente, e with { Zoom = Math.Clamp(e.Zoom * factor, 1.0, ZoomMaximo) });
}
