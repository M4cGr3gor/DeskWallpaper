namespace DeskWallpaper.Nucleo;

/// <summary>Colores con nombre para filtrar la biblioteca.</summary>
public enum ColorNombre { Rojo, Naranja, Amarillo, Verde, Cian, Azul, Morado, Rosa, Marron, Blanco, Gris, Negro }

public static class PaletaColor
{
    /// <summary>Fracción mínima de la imagen que tiene que ocupar un color para contar.</summary>
    public const double FraccionMinima = 0.12;
    public const int MaximoColores = 5;

    /// <summary>Colores presentes (RGBA, 4 bytes por píxel), del más al menos abundante.</summary>
    public static IReadOnlyList<ColorNombre> Calcular(ReadOnlySpan<byte> rgba)
    {
        var cuentas = new int[Enum.GetValues<ColorNombre>().Length];
        var total = 0;
        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i + 3] < 128) continue; // transparente
            cuentas[(int)Nombrar(rgba[i], rgba[i + 1], rgba[i + 2])]++;
            total++;
        }
        if (total == 0) return [];
        return Enumerable.Range(0, cuentas.Length)
            .Where(c => cuentas[c] >= FraccionMinima * total)
            .OrderByDescending(c => cuentas[c])
            .Take(MaximoColores)
            .Select(c => (ColorNombre)c)
            .ToList();
    }

    /// <summary>Nombre de un color según su tono, saturación y brillo (HSV).</summary>
    public static ColorNombre Nombrar(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b)) / 255.0;
        var min = Math.Min(r, Math.Min(g, b)) / 255.0;
        var v = max;
        var s = max == 0 ? 0 : (max - min) / max;
        if (v < 0.2) return ColorNombre.Negro;
        if (s < 0.15) return v > 0.85 ? ColorNombre.Blanco : ColorNombre.Gris;

        var h = Tono(r / 255.0, g / 255.0, b / 255.0, max, max - min);
        if (h is >= 15 and < 45 && v < 0.6) return ColorNombre.Marron;
        return h switch
        {
            < 15 or >= 345 => ColorNombre.Rojo,
            < 45 => ColorNombre.Naranja,
            < 70 => ColorNombre.Amarillo,
            < 165 => ColorNombre.Verde,
            < 200 => ColorNombre.Cian,
            < 260 => ColorNombre.Azul,
            < 300 => ColorNombre.Morado,
            _ => ColorNombre.Rosa,
        };
    }

    private static double Tono(double r, double g, double b, double max, double d)
    {
        double h;
        if (max == r) h = 60 * ((g - b) / d);
        else if (max == g) h = 60 * ((b - r) / d) + 120;
        else h = 60 * ((r - g) / d) + 240;
        return h < 0 ? h + 360 : h;
    }
}
