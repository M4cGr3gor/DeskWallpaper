namespace DeskWallpaper.Nucleo;

/// <summary>
/// Lo pintado con el pincel del editor de borrado, en píxeles de la imagen.
/// Un píxel está pintado si su centro (x+0,5; y+0,5) queda a una distancia del trazo no mayor que el radio.
/// </summary>
public sealed class Mascara(int ancho, int alto)
{
    /// <summary>Lo pintado se agranda estos píxeles antes de suavizar, para tapar el halo de la marca.</summary>
    public const int Dilatacion = 3;

    /// <summary>Radio del suavizado del borde: así no se nota el corte entre lo rellenado y lo original.</summary>
    public const int Suavizado = 3;

    private readonly bool[] _pintado = new bool[ancho * alto];
    private int _x0 = int.MaxValue, _y0 = int.MaxValue, _x1 = -1, _y1 = -1;

    public int Ancho { get; } = ancho;
    public int Alto { get; } = alto;

    public bool Vacia => _x1 < 0;

    /// <summary>El rectángulo más pequeño que contiene todo lo pintado, o null si no hay nada.</summary>
    public RectPx? Limites => Vacia ? null : new RectPx(_x0, _y0, _x1 - _x0 + 1, _y1 - _y0 + 1);

    public bool this[int x, int y] => x >= 0 && y >= 0 && x < Ancho && y < Alto && _pintado[y * Ancho + x];

    /// <summary>Pinta una línea de grosor 2·radio con extremos redondos. Un punto es un trazo con origen = destino.</summary>
    public void PintarTrazo(double x0, double y0, double x1, double y1, double radio)
    {
        var minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1) - radio));
        var maxX = Math.Min(Ancho - 1, (int)Math.Ceiling(Math.Max(x0, x1) + radio));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - radio));
        var maxY = Math.Min(Alto - 1, (int)Math.Ceiling(Math.Max(y0, y1) + radio));
        var dx = x1 - x0;
        var dy = y1 - y0;
        var largo2 = dx * dx + dy * dy;
        var r2 = radio * radio;

        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5 - x0;
                var py = y + 0.5 - y0;
                var t = largo2 > 0 ? Math.Clamp((px * dx + py * dy) / largo2, 0, 1) : 0;
                var ex = px - t * dx;
                var ey = py - t * dy;
                if (ex * ex + ey * ey > r2) continue;
                _pintado[y * Ancho + x] = true;
                if (x < _x0) _x0 = x;
                if (x > _x1) _x1 = x;
                if (y < _y0) _y0 = y;
                if (y > _y1) _y1 = y;
            }
    }

    public void Limpiar()
    {
        Array.Clear(_pintado);
        _x0 = _y0 = int.MaxValue;
        _x1 = _y1 = -1;
    }

    /// <summary>
    /// Peso 0–1 de cada píxel de <paramref name="region"/> para mezclar el relleno con el original, fila a fila:
    /// lo pintado vale 1, alrededor baja suavemente y, a más de <see cref="Dilatacion"/> + <see cref="Suavizado"/>
    /// píxeles, vale 0 exacto (el original queda idéntico).
    /// </summary>
    public float[] Alfa(RectPx region)
    {
        // Zona de trabajo: la región más el suavizado, recortada a la imagen.
        var zx0 = Math.Max(0, region.X - Suavizado);
        var zy0 = Math.Max(0, region.Y - Suavizado);
        var zx1 = Math.Min(Ancho - 1, region.X + region.Ancho - 1 + Suavizado);
        var zy1 = Math.Min(Alto - 1, region.Y + region.Alto - 1 + Suavizado);
        var zw = zx1 - zx0 + 1;
        var zh = zy1 - zy0 + 1;

        // Dilatación cuadrada separable: primero en horizontal (leyendo la máscara fuera de la zona si hace falta).
        var filas = new bool[zw * zh];
        for (var y = 0; y < zh; y++)
            for (var x = 0; x < zw; x++)
            {
                var gx = zx0 + x;
                var desde = Math.Max(0, gx - Dilatacion);
                var hasta = Math.Min(Ancho - 1, gx + Dilatacion);
                var fila = (zy0 + y) * Ancho;
                for (var k = desde; k <= hasta; k++)
                    if (_pintado[fila + k]) { filas[y * zw + x] = true; break; }
            }
        // …y en vertical, que necesita las filas de fuera de la zona: se calculan sobre la marcha.
        var dilatada = new float[zw * zh];
        for (var y = 0; y < zh; y++)
        {
            var gy = zy0 + y;
            for (var x = 0; x < zw; x++)
            {
                var gx = zx0 + x;
                var hay = false;
                for (var k = Math.Max(0, gy - Dilatacion); k <= Math.Min(Alto - 1, gy + Dilatacion) && !hay; k++)
                {
                    if (k >= zy0 && k <= zy1) hay = filas[(k - zy0) * zw + x];
                    else
                        for (var j = Math.Max(0, gx - Dilatacion); j <= Math.Min(Ancho - 1, gx + Dilatacion) && !hay; j++)
                            hay = _pintado[k * Ancho + j];
                }
                dilatada[y * zw + x] = hay ? 1 : 0;
            }
        }

        // Desenfoque de caja separable, contando solo los píxeles que existen.
        var horizontal = new float[zw * zh];
        for (var y = 0; y < zh; y++)
            for (var x = 0; x < zw; x++)
            {
                float suma = 0;
                var desde = Math.Max(0, x - Suavizado);
                var hasta = Math.Min(zw - 1, x + Suavizado);
                for (var k = desde; k <= hasta; k++) suma += dilatada[y * zw + k];
                horizontal[y * zw + x] = suma / (hasta - desde + 1);
            }

        var alfa = new float[region.Ancho * region.Alto];
        for (var y = 0; y < region.Alto; y++)
        {
            var gy = region.Y + y;
            for (var x = 0; x < region.Ancho; x++)
            {
                var gx = region.X + x;
                if (gx < 0 || gy < 0 || gx >= Ancho || gy >= Alto) continue;
                if (_pintado[gy * Ancho + gx]) { alfa[y * region.Ancho + x] = 1; continue; }
                var zx = gx - zx0;
                var zy = gy - zy0;
                float suma = 0;
                var desde = Math.Max(0, zy - Suavizado);
                var hasta = Math.Min(zh - 1, zy + Suavizado);
                for (var k = desde; k <= hasta; k++) suma += horizontal[k * zw + zx];
                alfa[y * region.Ancho + x] = suma / (hasta - desde + 1);
            }
        }
        return alfa;
    }
}
