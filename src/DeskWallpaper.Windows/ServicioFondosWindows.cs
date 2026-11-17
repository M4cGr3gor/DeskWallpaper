using System.Runtime.InteropServices;
using DeskWallpaper.Nucleo;
using DeskWallpaper.Windows.Interop;

namespace DeskWallpaper.Windows;

/// <summary>
/// Fondo por monitor mediante IDesktopWallpaper. Los Id de monitor son las mismas rutas de dispositivo
/// que da QueryDisplayConfig.
/// </summary>
public sealed class ServicioFondosWindows : IServicioFondos, IDisposable
{
    private readonly IDesktopWallpaper _api = (IDesktopWallpaper)new DesktopWallpaperClase();

    public IReadOnlyList<string> MonitorIds()
    {
        var n = _api.GetMonitorDevicePathCount();
        var ids = new List<string>((int)n);
        for (uint i = 0; i < n; i++)
        {
            var id = _api.GetMonitorDevicePathAt(i);
            // Los monitores inactivos devuelven un rectángulo vacío; solo nos interesan los activos.
            try
            {
                var r = _api.GetMonitorRECT(id);
                if (r.Right > r.Left && r.Bottom > r.Top) ids.Add(id);
            }
            catch (COMException)
            {
            }
        }
        return ids;
    }

    public string? ObtenerFondo(string monitorId)
    {
        try
        {
            return _api.GetWallpaper(monitorId);
        }
        catch (COMException)
        {
            return null;
        }
    }

    public void PonerFondo(string monitorId, string ruta) => _api.SetWallpaper(monitorId, ruta);

    public PosicionFondo ObtenerPosicion() => _api.GetPosition();

    public void PonerPosicion(PosicionFondo posicion) => _api.SetPosition(posicion);

    public void QuitarImagenes()
    {
        // SPI_SETDESKWALLPAPER con ruta vacía es la forma documentada de quitar el fondo (lo que hace
        // "Color sólido" en Configuración); SetWallpaper con ruta vacía no es estable.
        if (!SystemParametersInfo(0x0014, 0, "", 0x01 | 0x02))
            throw new COMException("No se ha podido quitar la imagen de fondo", Marshal.GetHRForLastWin32Error());
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint accion, uint parametro, string valor, uint opciones);

    public uint ObtenerColorFondo() => _api.GetBackgroundColor();

    public void PonerColorFondo(uint color) => _api.SetBackgroundColor(color);

    public void Dispose() => Marshal.ReleaseComObject(_api);
}
