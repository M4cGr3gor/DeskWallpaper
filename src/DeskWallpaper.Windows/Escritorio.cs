using System.Runtime.InteropServices;

namespace DeskWallpaper.Windows;

/// <summary>
/// Minimizar todas las ventanas para ver el escritorio (como Win+M) y deshacerlo después, con las
/// mismas órdenes que la barra de tareas. Se usa en "Probar 15 s".
/// </summary>
public static partial class Escritorio
{
    private const uint WmCommand = 0x0111;
    private const nint MinimizarTodas = 419;   // el mismo comando que Win+M
    private const nint DeshacerMinimizar = 416; // el mismo que Win+Mayús+M

    /// <summary>Minimiza todas las ventanas. False si no se encuentra la barra de tareas.</summary>
    public static bool MinimizarTodo() => Enviar(MinimizarTodas);

    /// <summary>Vuelve a poner las ventanas que se minimizaron con <see cref="MinimizarTodo"/>.</summary>
    public static bool DeshacerMinimizarTodo() => Enviar(DeshacerMinimizar);

    private static bool Enviar(nint comando)
    {
        var barra = FindWindowW("Shell_TrayWnd", null);
        if (barra == 0) return false;
        SendMessageW(barra, WmCommand, comando, 0);
        return true;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowW(string clase, string? titulo);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint ventana, uint mensaje, nint w, nint l);
}
