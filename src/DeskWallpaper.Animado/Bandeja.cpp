// Icono junto al reloj (área de notificación).

#include "Bandeja.h"

#include <shellapi.h>

namespace
{
constexpr UINT IdIcono = 1;
HICON g_icono = nullptr;

enum : UINT_PTR
{
    CmdPausa = 1,
    CmdAbrir = 2,
    CmdQuitar = 3,
    CmdCerrar = 4,
};
}  // namespace

bool CrearIcono(HWND control, const std::wstring& carpetaExe)
{
    if (!g_icono)
        g_icono = static_cast<HICON>(LoadImageW(nullptr, (carpetaExe + L"\\Recursos\\DeskWallpaper.ico").c_str(), IMAGE_ICON,
                                                GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE));
    NOTIFYICONDATAW datos{};
    datos.cbSize = sizeof datos;
    datos.hWnd = control;
    datos.uID = IdIcono;
    datos.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    datos.uCallbackMessage = MensajeBandeja;
    datos.hIcon = g_icono ? g_icono : LoadIconW(nullptr, IDI_APPLICATION);
    wcscpy_s(datos.szTip, L"DeskWallpaper: fondo animado");
    return Shell_NotifyIconW(NIM_ADD, &datos) != FALSE;
}

void QuitarIcono(HWND control)
{
    NOTIFYICONDATAW datos{};
    datos.cbSize = sizeof datos;
    datos.hWnd = control;
    datos.uID = IdIcono;
    Shell_NotifyIconW(NIM_DELETE, &datos);
}

OrdenBandeja MostrarMenu(HWND control, bool pausado)
{
    HMENU menu = CreatePopupMenu();
    AppendMenuW(menu, MF_STRING, CmdPausa, pausado ? L"Reanudar" : L"Pausar");
    AppendMenuW(menu, MF_STRING, CmdAbrir, L"Abrir DeskWallpaper");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, CmdCerrar, L"Cerrar (vuelve al reiniciar)");
    AppendMenuW(menu, MF_STRING, CmdQuitar, L"Quitar fondo animado");
    POINT cursor{};
    GetCursorPos(&cursor);
    // Sin esto el menú no se cierra al pinchar fuera (comportamiento documentado del área de notificación).
    SetForegroundWindow(control);
    const auto orden = static_cast<UINT_PTR>(
        TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, cursor.x, cursor.y, 0, control, nullptr));
    PostMessageW(control, WM_NULL, 0, 0);
    DestroyMenu(menu);
    switch (orden)
    {
    case CmdPausa: return OrdenBandeja::Pausa;
    case CmdAbrir: return OrdenBandeja::Abrir;
    case CmdQuitar: return OrdenBandeja::Quitar;
    case CmdCerrar: return OrdenBandeja::Cerrar;
    default: return OrdenBandeja::Ninguna;
    }
}
