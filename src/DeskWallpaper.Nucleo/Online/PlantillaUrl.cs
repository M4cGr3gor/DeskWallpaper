using System.Globalization;
using System.Text.RegularExpressions;

namespace DeskWallpaper.Nucleo;

/// <summary>Plantillas de dirección de las fuentes de búsqueda.</summary>
public static partial class PlantillaUrl
{
    /// <summary>Sustituye {texto} y {clave} (codificados para URL), {ancho}, {alto}, {proporcion} y {pagina}.</summary>
    /// <exception cref="UriFormatException">La plantilla no da una dirección absoluta válida.</exception>
    public static Uri Rellenar(string plantilla, string texto, SizePx? resolucion, int pagina, string clave = "")
    {
        var s = plantilla
            .Replace("{texto}", Uri.EscapeDataString(texto.Trim()), StringComparison.Ordinal)
            .Replace("{clave}", Uri.EscapeDataString(clave.Trim()), StringComparison.Ordinal)
            .Replace("{pagina}", pagina.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (resolucion is { } r)
            s = s.Replace("{ancho}", r.Ancho.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                 .Replace("{alto}", r.Alto.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                 .Replace("{proporcion}", Proporcion(r), StringComparison.Ordinal);
        return new Uri(s, UriKind.Absolute);
    }

    /// <summary>
    /// Quita los trozos (separados por '&amp;', '+' o espacio) que dependen de la resolución, para buscar sin
    /// filtro de tamaño: "…&amp;q={texto}+imagesize:{ancho}x{alto}" → "…&amp;q={texto}".
    /// </summary>
    public static string SinResolucion(string plantilla)
    {
        var s = TrozoConResolucion().Replace(plantilla, "");
        return s.Replace("?&", "?", StringComparison.Ordinal).TrimEnd('&', '+', ' ');
    }

    /// <summary>
    /// Forma reducida con su nombre habitual: 3840×1080 → "32x9"; 1920×1200 → "16x10" (no "8x5", que es como
    /// la llaman las webs de fondos); 2560×1080 y 3440×1440 → "21x9".
    /// </summary>
    public static string Proporcion(SizePx tamano)
    {
        var m = Mcd(tamano.Ancho, tamano.Alto);
        if (m == 0) return "0x0";
        var reducida = $"{tamano.Ancho / m}x{tamano.Alto / m}";
        return NombresHabituales.GetValueOrDefault(reducida, reducida);
    }

    private static readonly Dictionary<string, string> NombresHabituales = new(StringComparer.Ordinal)
    {
        ["8x5"] = "16x10",
        ["64x27"] = "21x9",
        ["43x18"] = "21x9",
    };

    private static int Mcd(int a, int b) => b == 0 ? Math.Abs(a) : Mcd(b, a % b);

    [GeneratedRegex(@"[&+ ]?[^&+ ?/]*\{(?:ancho|alto|proporcion)\}[^&+ ?/]*")]
    private static partial Regex TrozoConResolucion();
}
