namespace DeskWallpaper.Nucleo;

/// <summary>
/// Cómo se coloca una imagen dentro de su zona.
/// </summary>
/// <param name="CentroX">Centro del encuadre, relativo al ancho de la imagen (0 = borde izquierdo, 1 = derecho).</param>
/// <param name="CentroY">Centro del encuadre, relativo al alto de la imagen.</param>
/// <param name="Zoom">1 = la región más grande que rellena la zona; 2 = la mitad de ancho y alto, etc. Mínimo 1.</param>
public sealed record Encuadre(double CentroX = 0.5, double CentroY = 0.5, double Zoom = 1.0);
