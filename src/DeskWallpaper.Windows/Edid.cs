using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Windows;

/// <summary>Lectura del tamaño físico de un monitor a partir de su EDID.</summary>
public static class Edid
{
    private static readonly byte[] Cabecera = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    /// <summary>
    /// Tamaño del área visible. Prefiere los mm del primer descriptor de temporización (más precisos);
    /// si no hay, usa los cm del bloque básico. Null si el EDID no es válido o no informa del tamaño.
    /// </summary>
    public static SizeMm? TamanoFisico(byte[] edid)
    {
        if (edid.Length < 128 || !edid.AsSpan(0, 8).SequenceEqual(Cabecera)) return null;

        var anchoMm = edid[66] | ((edid[68] & 0xF0) << 4);
        var altoMm = edid[67] | ((edid[68] & 0x0F) << 8);
        if (anchoMm > 0 && altoMm > 0) return new SizeMm(anchoMm, altoMm);

        if (edid[21] > 0 && edid[22] > 0) return new SizeMm(edid[21] * 10, edid[22] * 10);
        return null;
    }

    /// <summary>
    /// Convierte la ruta de dispositivo del monitor (<c>\\?\DISPLAY#modelo#instancia#{guid}</c>) en la clave
    /// del registro (bajo HKLM) donde Windows guarda su EDID. Null si la ruta no tiene ese formato.
    /// </summary>
    public static string? ClaveRegistro(string rutaDispositivo)
    {
        var partes = rutaDispositivo.Split('#');
        if (partes.Length != 4 || !partes[0].Equals(@"\\?\DISPLAY", StringComparison.OrdinalIgnoreCase)
            || partes[1].Length == 0 || partes[2].Length == 0)
            return null;
        return $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{partes[1]}\{partes[2]}\Device Parameters";
    }
}
