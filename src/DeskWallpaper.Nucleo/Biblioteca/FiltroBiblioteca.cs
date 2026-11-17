using System.Globalization;
using System.Text;

namespace DeskWallpaper.Nucleo;

public enum SeccionBiblioteca { Panoramica, UnMonitor, Otras, Favoritas, Todas }

/// <summary>Los dos apartados de la biblioteca.</summary>
public enum Pestana { Imagenes, Animados }

/// <summary>Qué se está viendo de la biblioteca: sección, búsqueda y filtros.</summary>
public sealed record CriterioBiblioteca(SeccionBiblioteca Seccion, string Texto, string? Etiqueta, ColorNombre? Color,
                                        Pestana Pestana = Pestana.Imagenes);

/// <summary>Filtra y ordena la biblioteca.</summary>
public static class FiltroBiblioteca
{
    public static IReadOnlyList<ImagenBiblioteca> Aplicar(IReadOnlyList<ImagenBiblioteca> imagenes, CriterioBiblioteca c,
                                                          IReadOnlyList<Zona> zonas, LienzoFisico l)
    {
        var palabras = Normalizar(c.Texto).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var etiqueta = c.Etiqueta is null ? null : Normalizar(c.Etiqueta);

        return imagenes
            .Where(i => i.EsVideo == (c.Pestana == Pestana.Animados))
            .Where(i => c.Seccion switch
            {
                SeccionBiblioteca.Favoritas => i.Favorita,
                SeccionBiblioteca.Todas => true,
                SeccionBiblioteca.Panoramica => ClasificadorProporcion.Clasificar(i.Tamano, zonas, l) == Categoria.Panoramica,
                SeccionBiblioteca.UnMonitor => ClasificadorProporcion.Clasificar(i.Tamano, zonas, l) == Categoria.UnMonitor,
                _ => ClasificadorProporcion.Clasificar(i.Tamano, zonas, l) == Categoria.Otras,
            })
            .Where(i => c.Color is not { } color || i.Colores.Contains(color))
            .Where(i => etiqueta is null || i.Etiquetas.Any(e => Normalizar(e) == etiqueta))
            .Where(i =>
            {
                if (palabras.Length == 0) return true;
                var donde = Normalizar(i.Nombre + " " + string.Join(' ', i.Etiquetas));
                return palabras.All(donde.Contains);
            })
            .OrderBy(i => i.Nombre, OrdenNatural.Instancia)
            .ToList();
    }

    /// <summary>Minúsculas y sin acentos: "Fantasía" → "fantasia".</summary>
    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>Orden "natural": "img2" antes que "img10"; sin distinguir mayúsculas.</summary>
    private sealed class OrdenNatural : IComparer<string>
    {
        public static readonly OrdenNatural Instancia = new();

        public int Compare(string? x, string? y)
        {
            if (x is null || y is null) return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int fi = i, fj = j;
                    while (fi < x.Length && char.IsDigit(x[fi])) fi++;
                    while (fj < y.Length && char.IsDigit(y[fj])) fj++;
                    var a = x[i..fi].TrimStart('0');
                    var b = y[j..fj].TrimStart('0');
                    var porLargo = a.Length.CompareTo(b.Length);
                    if (porLargo != 0) return porLargo;
                    var porValor = string.CompareOrdinal(a, b);
                    if (porValor != 0) return porValor;
                    i = fi;
                    j = fj;
                }
                else
                {
                    var r = string.Compare(x[i].ToString(), y[j].ToString(), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase);
                    if (r != 0) return r;
                    i++;
                    j++;
                }
            }
            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
