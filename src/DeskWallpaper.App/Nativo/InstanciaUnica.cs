using System.Runtime.InteropServices;
using DeskWallpaper.Nucleo;

namespace DeskWallpaper.App.Nativo;

/// <summary>
/// Una sola copia del programa por carpeta de datos: dos copias contra la
/// misma base de datos y configuración se pisarían. La segunda pide a la primera que se traiga al frente y termina.
/// </summary>
internal static class InstanciaUnica
{
    private static Mutex? _mutex;

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string nombre);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint ventana, uint mensaje, nint w, nint l);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int proceso);

    private static string Id() => ServicioAnimado.Id(CarpetaDatos.Resolver(Environment.GetCommandLineArgs(),
        () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

    /// <summary>El mensaje con el que se pide traer al frente la ventana de esa carpeta de datos.</summary>
    public static uint MensajeTraer(string id) => RegisterWindowMessage($"DeskWallpaper.Traer.{id}");

    /// <summary>True si esta es la única copia; si no, avisa a la otra y devuelve false.</summary>
    public static bool Reclamar()
    {
        var id = Id();
        _mutex = new Mutex(true, $@"Local\DeskWallpaper.App.{id}", out var nueva);
        if (nueva) return true;
        AllowSetForegroundWindow(-1);   // ASFW_ANY: que la otra copia pueda ponerse delante
        PostMessage(0xFFFF, MensajeTraer(id), 0, 0);   // HWND_BROADCAST
        _mutex.Dispose();
        _mutex = null;
        return false;
    }
}
