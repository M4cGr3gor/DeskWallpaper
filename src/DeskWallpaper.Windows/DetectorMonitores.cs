using System.Runtime.InteropServices;
using DeskWallpaper.Nucleo;
using Microsoft.Win32;
using static DeskWallpaper.Windows.Interop.NativoPantallas;

namespace DeskWallpaper.Windows;

/// <summary>Lee de Windows los monitores activos, su colocación, escala y tamaño físico.</summary>
public static class DetectorMonitores
{
    public static Disposicion Detectar()
    {
        // Trabajamos en píxeles físicos aunque el proceso que llama no sea Per-Monitor v2.
        var anterior = SetThreadDpiAwarenessContext(ContextoPerMonitorV2);
        try
        {
            var gdi = MonitoresGdi();
            var monitores = new List<DatosMonitor>();

            foreach (var (ruta, modo) in RutasActivas())
            {
                var destino = new NombreDestino
                {
                    Cabecera = new CabeceraInfoDispositivo
                    {
                        Type = ObtenerNombreDestino, Size = (uint)Marshal.SizeOf<NombreDestino>(),
                        AdapterId = ruta.Destino.AdapterId, Id = ruta.Destino.Id,
                    },
                };
                var origen = new NombreOrigen
                {
                    Cabecera = new CabeceraInfoDispositivo
                    {
                        Type = ObtenerNombreOrigen, Size = (uint)Marshal.SizeOf<NombreOrigen>(),
                        AdapterId = ruta.Origen.AdapterId, Id = ruta.Origen.Id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref destino) != 0 || DisplayConfigGetDeviceInfo(ref origen) != 0) continue;

                var numero = int.TryParse(new string(origen.NombreGdi.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray()), out var n) ? n : 0;
                var escritorio = new RectPx(modo.X, modo.Y, (int)modo.Ancho, (int)modo.Alto);
                gdi.TryGetValue(origen.NombreGdi, out var infoGdi);
                var dpi = infoGdi.Dpi == 0 ? 96u : infoGdi.Dpi;

                var tamano = LeerEdid(destino.RutaDispositivo) is { } edid && Edid.TamanoFisico(edid) is { } mm
                    ? TamanoPantalla.PixelesCuadrados(mm, escritorio.Ancho, escritorio.Alto)
                    : new SizeMm(escritorio.Ancho / (double)dpi * 25.4, escritorio.Alto / (double)dpi * 25.4);

                var nombre = string.IsNullOrWhiteSpace(destino.NombreAmigable) ? $"Monitor {numero}" : destino.NombreAmigable;
                monitores.Add(new DatosMonitor(destino.RutaDispositivo, numero, nombre, escritorio,
                                               dpi / 96.0, tamano, infoGdi.Principal));
            }

            return new Disposicion(monitores.OrderBy(m => m.Numero).ToList());
        }
        finally
        {
            SetThreadDpiAwarenessContext(anterior);
        }
    }

    private static IEnumerable<(InfoRuta Ruta, ModoOrigenPantalla Modo)> RutasActivas()
    {
        for (var intento = 0; intento < 3; intento++)
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var numRutas, out var numModos) != 0) yield break;
            var rutas = new InfoRuta[numRutas];
            var modos = new InfoModo[numModos];
            var r = QueryDisplayConfig(QdcOnlyActivePaths, ref numRutas, rutas, ref numModos, modos, 0);
            if (r == 122) continue; // ERROR_INSUFFICIENT_BUFFER: la configuración cambió entre llamadas
            if (r != 0) yield break;

            for (var i = 0; i < numRutas; i++)
            {
                var idx = rutas[i].Origen.ModeInfoIdx;
                if (idx < numModos && modos[idx].InfoType == ModoOrigen)
                    yield return (rutas[i], modos[idx].Origen);
            }
            yield break;
        }
    }

    private static Dictionary<string, (uint Dpi, bool Principal)> MonitoresGdi()
    {
        var resultado = new Dictionary<string, (uint, bool)>(StringComparer.OrdinalIgnoreCase);
        EnumDisplayMonitors(0, 0, (nint h, nint _, ref Rect _, nint _) =>
        {
            var info = new InfoMonitorEx { Size = Marshal.SizeOf<InfoMonitorEx>() };
            if (GetMonitorInfo(h, ref info))
            {
                var dpi = GetDpiForMonitor(h, 0 /* MDT_EFFECTIVE_DPI */, out var dx, out _) == 0 ? dx : 96u;
                resultado[info.Dispositivo] = (dpi, (info.Flags & MonitorPrincipal) != 0);
            }
            return true;
        }, 0);
        return resultado;
    }

    private static byte[]? LeerEdid(string rutaDispositivo)
    {
        var clave = Edid.ClaveRegistro(rutaDispositivo);
        if (clave is null) return null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(clave);
            return k?.GetValue("EDID") as byte[];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
