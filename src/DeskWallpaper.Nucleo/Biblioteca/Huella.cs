using System.Security.Cryptography;

namespace DeskWallpaper.Nucleo;

/// <summary>
/// Identidad de una imagen por su contenido, para que conserve etiquetas y favorita aunque se
/// renombre o se mueva. Rápida con miles de archivos: tamaño + primeros y últimos 64 KB.
/// </summary>
public static class Huella
{
    private const int Trozo = 64 * 1024;

    /// <exception cref="IOException">El archivo no existe o no se puede leer.</exception>
    public static string Calcular(string ruta)
    {
        using var f = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, Trozo);
        var largo = f.Length;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(BitConverter.GetBytes(largo));

        var bufer = new byte[Trozo];
        // Los BMP no van comprimidos: dos del mismo tamaño pueden coincidir al principio y al final
        // (franjas negras) y ser distintos en medio. Son raros, así que se leen enteros.
        var completo = largo <= 2L * Trozo || string.Equals(Path.GetExtension(ruta), ".bmp", StringComparison.OrdinalIgnoreCase);
        if (completo)
        {
            int leidos;
            while ((leidos = f.Read(bufer, 0, bufer.Length)) > 0) sha.AppendData(bufer, 0, leidos);
        }
        else
        {
            f.ReadExactly(bufer, 0, Trozo);
            sha.AppendData(bufer);
            f.Seek(-Trozo, SeekOrigin.End);
            f.ReadExactly(bufer, 0, Trozo);
            sha.AppendData(bufer);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }
}
