using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas;

/// <summary>Datos de prueba que reproducen el equipo del usuario.</summary>
internal static class Ejemplos
{
    public static readonly DatosMonitor M1 = new(@"\\?\DISPLAY#GSM5BB8#UID4355#{e6f07b5f}", 1, "LG ULTRAGEAR",
        new RectPx(0, 0, 1920, 1080), 1.0, new SizeMm(600, 340), true);

    public static readonly DatosMonitor M2 = new(@"\\?\DISPLAY#GSM5BB8#UID4357#{e6f07b5f}", 2, "LG ULTRAGEAR",
        new RectPx(-1920, 0, 1920, 1080), 1.0, new SizeMm(600, 340), false);

    public static readonly DatosMonitor M3 = new(@"\\?\DISPLAY#RTK1920#UID4352#{e6f07b5f}", 3, "ANMITE",
        new RectPx(-1200, 1080, 1920, 1200), 1.5, new SizeMm(302, 189), false);

    public static readonly Disposicion Usuario = new([M1, M2, M3]);
}
