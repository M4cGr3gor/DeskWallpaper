using System.Security.Cryptography;
using System.Text;

namespace DeskWallpaper.Nucleo;

/// <summary>
/// Resumen corto de una disposición de monitores: qué monitores hay, su resolución y su
/// posición relativa. No incluye el tamaño físico (las correcciones no cambian la disposición).
/// </summary>
public static class FirmaDisposicion
{
    public static string Calcular(Disposicion d)
    {
        if (d.Monitores.Count == 0) return new string('0', 16);

        var minX = d.Monitores.Min(m => m.Escritorio.X);
        var minY = d.Monitores.Min(m => m.Escritorio.Y);
        var lineas = d.Monitores
            .OrderBy(m => m.Id.ToUpperInvariant(), StringComparer.Ordinal)
            .Select(m => $"{m.Id.ToUpperInvariant()}:{m.Escritorio.Ancho}x{m.Escritorio.Alto}@{m.Escritorio.X - minX},{m.Escritorio.Y - minY}");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', lineas)));
        return Convert.ToHexStringLower(hash)[..16];
    }
}
