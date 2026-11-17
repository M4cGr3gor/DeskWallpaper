namespace DeskWallpaper.Nucleo;

/// <summary>Rectángulo en píxeles enteros (escritorio virtual, en píxeles físicos).</summary>
public readonly record struct RectPx(int X, int Y, int Ancho, int Alto)
{
    public int Derecha => X + Ancho;
    public int Abajo => Y + Alto;
}

/// <summary>Rectángulo en píxeles con decimales (regiones dentro de una imagen).</summary>
public readonly record struct RectD(double X, double Y, double Ancho, double Alto);

/// <summary>Tamaño en píxeles enteros.</summary>
public readonly record struct SizePx(int Ancho, int Alto);

/// <summary>Tamaño físico en milímetros.</summary>
public readonly record struct SizeMm(double Ancho, double Alto);

/// <summary>Rectángulo físico en milímetros.</summary>
public readonly record struct RectMm(double X, double Y, double Ancho, double Alto)
{
    public double Derecha => X + Ancho;
    public double Abajo => Y + Alto;
}
