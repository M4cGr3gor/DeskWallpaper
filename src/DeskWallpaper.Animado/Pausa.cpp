// Un monitor está tapado si alguna ventana visible de primer nivel está maximizada en él o lo cubre entero.
// No cuentan las minimizadas, las ocultas por DWM, las del escritorio o la barra de tareas, las de herramientas o
// superposiciones (no se activan), ni las del propio ayudante.

#include "Pausa.h"

#include <dwmapi.h>

#include <cwchar>

namespace
{
bool EsDelEscritorio(HWND w)
{
    wchar_t clase[64]{};
    GetClassNameW(w, clase, 64);
    for (const wchar_t* c : {L"Progman", L"WorkerW", L"Shell_TrayWnd", L"Shell_SecondaryTrayWnd"})
        if (wcscmp(clase, c) == 0) return true;
    return false;
}

BOOL CALLBACK Revisar(HWND w, LPARAM p)
{
    auto& tapados = *reinterpret_cast<std::vector<RECT>*>(p);
    if (!IsWindowVisible(w) || IsIconic(w)) return TRUE;
    DWORD proceso = 0;
    GetWindowThreadProcessId(w, &proceso);
    if (proceso == GetCurrentProcessId() || EsDelEscritorio(w)) return TRUE;
    const LONG_PTR ex = GetWindowLongPtrW(w, GWL_EXSTYLE);
    if (ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) return TRUE;
    BOOL oculta = FALSE;
    DwmGetWindowAttribute(w, DWMWA_CLOAKED, &oculta, sizeof oculta);
    if (oculta) return TRUE;

    HMONITOR monitor = MonitorFromWindow(w, MONITOR_DEFAULTTONULL);
    if (!monitor) return TRUE;
    MONITORINFO m{};
    m.cbSize = sizeof m;
    if (!GetMonitorInfoW(monitor, &m)) return TRUE;
    RECT r{};
    GetWindowRect(w, &r);
    const bool completa = r.left <= m.rcMonitor.left && r.top <= m.rcMonitor.top && r.right >= m.rcMonitor.right &&
                          r.bottom >= m.rcMonitor.bottom;
    if (!completa && !IsZoomed(w)) return TRUE;
    for (const RECT& t : tapados)
        if (EqualRect(&t, &m.rcMonitor)) return TRUE;
    tapados.push_back(m.rcMonitor);
    return TRUE;
}
}  // namespace

std::vector<RECT> MonitoresTapados()
{
    std::vector<RECT> tapados;
    EnumWindows(Revisar, reinterpret_cast<LPARAM>(&tapados));
    return tapados;
}
