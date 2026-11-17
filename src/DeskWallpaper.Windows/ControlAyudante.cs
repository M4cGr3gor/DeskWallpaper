using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Windows;

/// <summary>
/// El proceso del ayudante de fondos animados de una carpeta de datos: se encuentra por la clase de su ventana de
/// control (<c>DeskWallpaper.Animado.Control.&lt;id&gt;</c>) y se le hablan con mensajes de ventana.
/// </summary>
public sealed partial class ControlAyudante(string rutaAyudante, string carpetaDatos, string id) : IControlAyudante
{
    private const uint WmClose = 0x0010;
    private const uint MensajeRecargar = 0x8000 + 1;   // WM_APP + 1

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string clase, string? titulo);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint ventana, uint mensaje, nint w, nint l);

    private nint Ventana() => FindWindow($"DeskWallpaper.Animado.Control.{id}", null);

    public bool EnMarcha() => Ventana() != 0;

    /// <exception cref="FileNotFoundException">Si no está el ayudante junto al programa.</exception>
    public void Arrancar()
    {
        if (!File.Exists(rutaAyudante))
            throw new FileNotFoundException("No se encuentra el ayudante de fondos animados (DeskWallpaper.Animado.exe)", rutaAyudante);
        var inicio = new ProcessStartInfo(rutaAyudante) { UseShellExecute = false };
        inicio.ArgumentList.Add("--datos");
        inicio.ArgumentList.Add(carpetaDatos);
        inicio.ArgumentList.Add("--id");
        inicio.ArgumentList.Add(id);
        Process.Start(inicio)?.Dispose();
    }

    public void Recargar()
    {
        if (Ventana() is var v and not 0) PostMessage(v, MensajeRecargar, 0, 0);
    }

    /// <summary>Le pide que se cierre y espera (hasta 5 s) a que lo haga.</summary>
    public void Cerrar()
    {
        if (Ventana() is not (var v and not 0)) return;
        PostMessage(v, WmClose, 0, 0);
        for (var i = 0; i < 50 && EnMarcha(); i++) Thread.Sleep(100);
    }
}
