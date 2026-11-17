using System.Runtime.InteropServices;

namespace DeskWallpaper.Windows.Interop;

// Declaraciones de user32/shcore para QueryDisplayConfig, monitores y DPI.
internal static class NativoPantallas
{
    public const uint QdcOnlyActivePaths = 0x2;
    public const uint ModoOrigen = 1;            // DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE
    public const uint ObtenerNombreOrigen = 1;   // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
    public const uint ObtenerNombreDestino = 2;  // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
    public const uint MonitorPrincipal = 1;      // MONITORINFOF_PRIMARY
    public static readonly nint ContextoPerMonitorV2 = -4;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Racional { public uint Numerador; public uint Denominador; }

    [StructLayout(LayoutKind.Sequential)]
    public struct InfoOrigenRuta { public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct InfoDestinoRuta
    {
        public Luid AdapterId; public uint Id; public uint ModeInfoIdx; public uint OutputTechnology;
        public uint Rotation; public uint Scaling; public Racional RefreshRate; public uint ScanLineOrdering;
        public int TargetAvailable; public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InfoRuta { public InfoOrigenRuta Origen; public InfoDestinoRuta Destino; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModoOrigenPantalla { public uint Ancho; public uint Alto; public uint FormatoPixel; public int X; public int Y; }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct InfoModo
    {
        [FieldOffset(0)] public uint InfoType;
        [FieldOffset(4)] public uint Id;
        [FieldOffset(8)] public Luid AdapterId;
        [FieldOffset(16)] public ModoOrigenPantalla Origen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CabeceraInfoDispositivo { public uint Type; public uint Size; public Luid AdapterId; public uint Id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NombreDestino
    {
        public CabeceraInfoDispositivo Cabecera;
        public uint Flags; public uint OutputTechnology; public ushort EdidManufactureId; public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string NombreAmigable;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string RutaDispositivo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NombreOrigen
    {
        public CabeceraInfoDispositivo Cabecera;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string NombreGdi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct InfoMonitorEx
    {
        public int Size; public Rect Monitor; public Rect Trabajo; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Dispositivo;
    }

    public delegate bool EnumMonitoresProc(nint hMonitor, nint hdc, ref Rect rect, nint datos);

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numRutas, out uint numModos);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(uint flags, ref uint numRutas, [Out] InfoRuta[] rutas,
                                                ref uint numModos, [Out] InfoModo[] modos, nint topologia);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref NombreDestino info);

    [DllImport("user32.dll")]
    public static extern int DisplayConfigGetDeviceInfo(ref NombreOrigen info);

    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(nint hdc, nint clip, EnumMonitoresProc proc, nint datos);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(nint hMonitor, ref InfoMonitorEx info);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(nint hMonitor, int tipo, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    public static extern nint SetThreadDpiAwarenessContext(nint contexto);
}
