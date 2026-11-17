using System.Runtime.InteropServices;

namespace DeskWallpaper.App.Nativo;

/// <summary>
/// Escucha mensajes de Windows que WinUI no expone. Aquí: WM_DISPLAYCHANGE (cambio de resolución,
/// monitores conectados o desconectados).
/// </summary>
internal sealed class SubclaseVentana : IDisposable
{
    private const uint WmDisplayChange = 0x007E;
    private const nuint IdSubclase = 0x4457; // "DW"

    private delegate nint ProcSubclase(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint datos);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint hwnd, ProcSubclase proc, nuint id, nuint datos);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint hwnd, ProcSubclase proc, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);

    private readonly nint _hwnd;
    private readonly ProcSubclase _proc; // referencia viva: si el GC lo recoge, Windows llamaría a memoria liberada
    private readonly Action _alCambiarPantallas;
    private readonly uint _mensajeTraer;
    private readonly Action? _alTraer;

    /// <param name="mensajeTraer">Mensaje registrado con el que otra copia del programa pide traer esta ventana al frente.</param>
    public SubclaseVentana(nint hwnd, Action alCambiarPantallas, uint mensajeTraer = 0, Action? alTraer = null)
    {
        _hwnd = hwnd;
        _alCambiarPantallas = alCambiarPantallas;
        _mensajeTraer = mensajeTraer;
        _alTraer = alTraer;
        _proc = Procesar;
        SetWindowSubclass(hwnd, _proc, IdSubclase, 0);
    }

    private nint Procesar(nint hwnd, uint msg, nint wParam, nint lParam, nuint id, nuint datos)
    {
        if (msg == WmDisplayChange) _alCambiarPantallas();
        else if (_mensajeTraer != 0 && msg == _mensajeTraer) _alTraer?.Invoke();
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose() => RemoveWindowSubclass(_hwnd, _proc, IdSubclase);
}
