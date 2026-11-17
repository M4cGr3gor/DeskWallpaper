// Detrás de los iconos: en Windows 11 24H2+ la ventana es hija de Progman, con capa opaca y justo debajo de
// SHELLDLL_DefView; antes, hija del WorkerW que va detrás del que contiene los iconos.

#include "Escritorio.h"

bool BuscarEscritorio(Escritorio& e)
{
    e = {};
    e.progman = FindWindowW(L"Progman", nullptr);
    if (!e.progman) return false;
    e.nuevo = (GetWindowLongPtrW(e.progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP) != 0;

    // Pide a Progman que cree el WorkerW del fondo (si ya existe, no hace nada).
    SendMessageTimeoutW(e.progman, 0x052C, 0xD, 0x1, SMTO_NORMAL, 1000, nullptr);

    if (e.nuevo)
    {
        e.defView = FindWindowExW(e.progman, nullptr, L"SHELLDLL_DefView", nullptr);
        e.workerW = FindWindowExW(e.progman, nullptr, L"WorkerW", nullptr);
        return e.defView != nullptr;
    }
    EnumWindows(
        [](HWND w, LPARAM p) -> BOOL {
            auto& esc = *reinterpret_cast<Escritorio*>(p);
            if (HWND d = FindWindowExW(w, nullptr, L"SHELLDLL_DefView", nullptr))
            {
                esc.defView = d;
                esc.workerW = FindWindowExW(nullptr, w, L"WorkerW", nullptr);
            }
            return TRUE;
        },
        reinterpret_cast<LPARAM>(&e));
    return e.defView && e.workerW;
}

bool Colocar(HWND ventana, const RECT& destino, const Escritorio& e)
{
    HWND padre = e.nuevo ? e.progman : e.workerW;
    SetWindowLongPtrW(ventana, GWL_STYLE, WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS);
    if (e.nuevo)
    {
        // Ventana con capa opaca: es lo que pide Windows para poder presentar con DirectX bajo los iconos.
        SetWindowLongPtrW(ventana, GWL_EXSTYLE, GetWindowLongPtrW(ventana, GWL_EXSTYLE) | WS_EX_LAYERED);
        SetLayeredWindowAttributes(ventana, 0, 255, LWA_ALPHA);
    }
    if (GetParent(ventana) != padre && !SetParent(ventana, padre)) return false;

    POINT origen{destino.left, destino.top};
    MapWindowPoints(HWND_DESKTOP, padre, &origen, 1);
    if (!SetWindowPos(ventana, e.nuevo ? e.defView : HWND_TOP, origen.x, origen.y, destino.right - destino.left,
                      destino.bottom - destino.top, SWP_NOACTIVATE | SWP_SHOWWINDOW))
        return false;
    if (e.nuevo && e.workerW)
        SetWindowPos(e.workerW, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    return true;
}
