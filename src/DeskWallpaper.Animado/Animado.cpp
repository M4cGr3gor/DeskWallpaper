// DeskWallpaper.Animado: ayudante de fondos animados. Es «tonto»: la app calcula todo y lo deja en animado.json.
//
//   DeskWallpaper.Animado.exe --info <vídeo> <fotograma.jpg>      fotograma y datos del vídeo (Info.cpp)
//   DeskWallpaper.Animado.exe --reducir <vídeo> <salida.mp4> <ancho> <alto> [<duración ms>]   copia reducida (Reducir.cpp)
//   DeskWallpaper.Animado.exe --datos <carpeta> --id <id>         reproduce según <carpeta>\animado.json
//
// Una sola instancia por carpeta de datos (mutex Local\DeskWallpaper.Animado.<id>); una segunda le pide a la primera
// que recargue y termina. Órdenes a la ventana de clase DeskWallpaper.Animado.Control.<id>: WM_APP+1 recargar,
// WM_APP+2 cambiar la pausa manual, WM_CLOSE salir.

#include "Bandeja.h"
#include "Comun.h"
#include "Config.h"
#include "Escritorio.h"
#include "Info.h"
#include "Reducir.h"
#include "Pausa.h"
#include "Reproductor.h"

#include <d3d11.h>
#include <mfapi.h>
#include <shellapi.h>
#include <shobjidl_core.h>
#include <wtsapi32.h>

#include <cwchar>
#include <memory>
#include <string>
#include <vector>

namespace
{
constexpr UINT_PTR TemporizadorRecolocar = 1;
constexpr wchar_t ValorArranque[] = L"DeskWallpaper.Animado";

std::wstring g_carpeta, g_id, g_carpetaExe;
Config g_cfg;
std::vector<std::unique_ptr<Zona>> g_zonas;
ComPtr<ID3D11Device> g_dispositivo;
ComPtr<IDesktopWallpaper> g_fondos;
UINT g_mensajeBarraCreada = 0;
bool g_bloqueada = false;
bool g_icono = false;
int g_reintentos = 0;

std::wstring ClaseControl(const std::wstring& id) { return L"DeskWallpaper.Animado.Control." + id; }

bool Montar()
{
    Escritorio e;
    if (!BuscarEscritorio(e))
    {
        Registrar(L"No se encuentra el escritorio (Progman / SHELLDLL_DefView)");
        return false;
    }
    Registrar(L"Escritorio: estructura %ls", e.nuevo ? L"nueva (24H2+)" : L"antigua");
    // IDesktopWallpaper lo sirve Explorer: si Explorer se ha reiniciado, el objeto anterior ya no responde.
    g_fondos.Reset();
    if (FAILED(CoCreateInstance(__uuidof(DesktopWallpaper), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&g_fondos))))
    {
        Registrar(L"No se puede consultar los monitores (IDesktopWallpaper)");
        return false;
    }
    bool todo = true;
    for (auto& z : g_zonas) todo = z->Montar(e, g_fondos.Get()) && todo;
    return todo;
}

/// Sigue intentando colocar las ventanas y poner el icono (al iniciar sesión el escritorio y la barra pueden llegar
/// después que el ayudante, que arranca desde la clave Run).
void Reintentar(HWND control)
{
    g_reintentos = 0;
    SetTimer(control, TemporizadorRecolocar, 1000, nullptr);
}

void Evaluar()
{
    const bool global = g_bloqueada || GetSystemMetrics(SM_REMOTESESSION) || g_cfg.pausaManual;
    const auto tapados = g_cfg.pausarMaximizadas ? MonitoresTapados() : std::vector<RECT>{};
    for (auto& z : g_zonas) z->Pausa(tapados, global);
}

void ActualizarIcono(HWND control)
{
    if (!g_cfg.zonas.empty() && !g_icono) g_icono = CrearIcono(control, g_carpetaExe);
    else if (g_cfg.zonas.empty() && g_icono)
    {
        QuitarIcono(control);
        g_icono = false;
    }
}

void Cargar(HWND control)
{
    g_zonas.clear();
    if (!LeerConfig(g_carpeta, g_cfg)) Registrar(L"animado.json no existe o está dañado: sin fondo animado");
    for (const auto& zc : g_cfg.zonas) g_zonas.push_back(std::make_unique<Zona>(g_dispositivo.Get(), zc, g_cfg.topeFps));
    Registrar(L"Configuración: %zu zona(s) · tope %d fps · pausa con maximizadas %ls · pausa manual %ls", g_cfg.zonas.size(),
              g_cfg.topeFps, g_cfg.pausarMaximizadas ? L"sí" : L"no", g_cfg.pausaManual ? L"sí" : L"no");
    const bool colocado = Montar();
    Evaluar();
    ActualizarIcono(control);
    if (!colocado || (!g_cfg.zonas.empty() && !g_icono))
    {
        Registrar(L"Escritorio o barra de tareas sin preparar: se reintenta");
        Reintentar(control);
    }
}

/// Quita la tarea programada «DeskWallpaper.Animado.<id>» de esta carpeta de datos (la pone la app al aplicar).
void QuitarTarea()
{
    std::wstring orden = L"schtasks.exe /delete /tn \"DeskWallpaper.Animado." + g_id + L"\" /f";
    STARTUPINFOW si{};
    si.cb = sizeof si;
    PROCESS_INFORMATION pi{};
    if (!CreateProcessW(nullptr, orden.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi))
        return;
    DWORD codigo = 1;
    if (WaitForSingleObject(pi.hProcess, 10000) == WAIT_OBJECT_0) GetExitCodeProcess(pi.hProcess, &codigo);
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    if (codigo == 0) Registrar(L"Quitada la tarea de arranque con Windows");
}

/// Quita el arranque con Windows: la tarea programada y, de versiones anteriores, el valor de la clave Run (solo si es
/// el de este ayudante, que lleva su --id).
void QuitarArranque()
{
    QuitarTarea();
    HKEY clave = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0,
                      KEY_QUERY_VALUE | KEY_SET_VALUE, &clave) != ERROR_SUCCESS)
        return;
    wchar_t valor[4096]{};
    DWORD bytes = sizeof valor - sizeof(wchar_t);
    if (RegQueryValueExW(clave, ValorArranque, nullptr, nullptr, reinterpret_cast<BYTE*>(valor), &bytes) == ERROR_SUCCESS &&
        wcsstr(valor, (L"--id " + g_id).c_str()))
    {
        RegDeleteValueW(clave, ValorArranque);
        Registrar(L"Quitado el arranque con Windows");
    }
    RegCloseKey(clave);
}

void CambiarPausaManual()
{
    g_cfg.pausaManual = !g_cfg.pausaManual;
    GuardarPausaManual(g_carpeta, g_cfg.pausaManual);
    Registrar(L"Pausa manual: %ls", g_cfg.pausaManual ? L"sí" : L"no");
    Evaluar();
}

void Abrir()
{
    const std::wstring exe = g_carpetaExe + L"\\DeskWallpaper.exe";
    const std::wstring parametros = L"--data=\"" + g_carpeta + L"\"";
    if (reinterpret_cast<INT_PTR>(ShellExecuteW(nullptr, L"open", exe.c_str(), parametros.c_str(), nullptr, SW_SHOWNORMAL)) <= 32)
        Registrar(L"No se puede abrir «%ls»", exe.c_str());
}

LRESULT CALLBACK ProcControl(HWND w, UINT m, WPARAM wp, LPARAM lp)
{
    if (m == g_mensajeBarraCreada && g_mensajeBarraCreada != 0)
    {
        Registrar(L"Explorer se ha reiniciado: recolocando");
        // El icono se pone aunque no lo hubiera: si el ayudante arrancó antes que la barra, no se pudo poner.
        g_icono = !g_cfg.zonas.empty() && CrearIcono(w, g_carpetaExe);
        Reintentar(w);
        return 0;
    }
    switch (m)
    {
    case MensajeRecargar:
        Registrar(L"Recargar");
        Cargar(w);
        return 0;
    case MensajePausaManual:
        CambiarPausaManual();
        return 0;
    case MensajeBandeja:
        if (LOWORD(lp) == WM_RBUTTONUP || LOWORD(lp) == WM_LBUTTONUP)
            switch (MostrarMenu(w, g_cfg.pausaManual))
            {
            case OrdenBandeja::Pausa: CambiarPausaManual(); break;
            case OrdenBandeja::Abrir: Abrir(); break;
            case OrdenBandeja::Quitar:
                Registrar(L"Quitar fondo animado (desde el icono)");
                GuardarSinZonas(g_carpeta);
                QuitarArranque();
                DestroyWindow(w);
                break;
            case OrdenBandeja::Cerrar:
                // Sin tocar animado.json ni el arranque: vuelve al iniciar Windows (para actualizar o mover la carpeta).
                Registrar(L"Cerrar hasta el próximo inicio (desde el icono)");
                DestroyWindow(w);
                break;
            case OrdenBandeja::Ninguna: break;
            }
        return 0;
    case WM_TIMER:
        if (wp == TemporizadorRecolocar)
        {
            const bool colocado = Montar();
            ActualizarIcono(w);
            if (colocado && (g_cfg.zonas.empty() || g_icono))
            {
                KillTimer(w, TemporizadorRecolocar);
                Registrar(L"Recolocado");
                Evaluar();
            }
            else if (++g_reintentos >= 15)
            {
                KillTimer(w, TemporizadorRecolocar);
                Registrar(L"No se ha podido recolocar tras 15 intentos");
            }
        }
        return 0;
    case WM_WTSSESSION_CHANGE:
        if (wp == WTS_SESSION_LOCK)
        {
            g_bloqueada = true;
            Registrar(L"Sesión bloqueada");
        }
        if (wp == WTS_SESSION_UNLOCK)
        {
            g_bloqueada = false;
            Registrar(L"Sesión desbloqueada");
            // Al desbloquear puede haber desaparecido el WorkerW: se comprueba recolocando.
            g_reintentos = 0;
            SetTimer(w, TemporizadorRecolocar, 500, nullptr);
        }
        Evaluar();
        return 0;
    case WM_DISPLAYCHANGE:
        Registrar(L"Han cambiado los monitores: recolocando");
        g_reintentos = 0;
        SetTimer(w, TemporizadorRecolocar, 1000, nullptr);
        return 0;
    case WM_CLOSE:
        DestroyWindow(w);
        return 0;
    case WM_DESTROY:
        if (g_icono) QuitarIcono(w);
        g_icono = false;
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(w, m, wp, lp);
}

HRESULT CrearDispositivo()
{
    const D3D_FEATURE_LEVEL niveles[] = {D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0};
    COMPROBAR(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr,
                                D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT, niveles,
                                ARRAYSIZE(niveles), D3D11_SDK_VERSION, &g_dispositivo, nullptr, nullptr));
    ComPtr<ID3D10Multithread> multihilo;
    COMPROBAR(g_dispositivo.As(&multihilo));
    multihilo->SetMultithreadProtected(TRUE);
    return S_OK;
}

int Reproducir(HINSTANCE instancia)
{
    // Una sola instancia por carpeta de datos: la segunda pide recargar a la primera y se va.
    HANDLE mutex = CreateMutexW(nullptr, TRUE, (L"Local\\DeskWallpaper.Animado." + g_id).c_str());
    if (GetLastError() == ERROR_ALREADY_EXISTS)
    {
        for (int i = 0; i < 20; ++i)
        {
            if (HWND otra = FindWindowW(ClaseControl(g_id).c_str(), nullptr))
            {
                PostMessageW(otra, MensajeRecargar, 0, 0);
                break;
            }
            Sleep(100);
        }
        if (mutex) CloseHandle(mutex);
        return 0;
    }

    AbrirRegistro(g_carpeta);
    Registrar(L"Inicio · datos «%ls» · id %ls", g_carpeta.c_str(), g_id.c_str());
    int codigo = 0;
    if (FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)) || FAILED(MFStartup(MF_VERSION)))
        codigo = 3;
    else
    {
        RegistrarClasePantalla(instancia);
        const std::wstring claseControl = ClaseControl(g_id);
        WNDCLASSEXW clase{};
        clase.cbSize = sizeof clase;
        clase.hInstance = instancia;
        clase.lpfnWndProc = ProcControl;
        clase.lpszClassName = claseControl.c_str();
        RegisterClassExW(&clase);
        // De primer nivel y nunca visible: recibe «TaskbarCreated», los avisos de sesión, el icono y las órdenes.
        HWND control = CreateWindowExW(WS_EX_TOOLWINDOW, claseControl.c_str(), L"DeskWallpaper.Animado", WS_POPUP, 0, 0, 0,
                                       0, nullptr, nullptr, instancia, nullptr);
        g_mensajeBarraCreada = RegisterWindowMessageW(L"TaskbarCreated");
        WTSRegisterSessionNotification(control, NOTIFY_FOR_THIS_SESSION);

        if (!control || FAILED(CrearDispositivo()))
            codigo = 4;
        else
        {
            Cargar(control);
            HANDLE evaluar = CreateWaitableTimerExW(nullptr, nullptr, 0, TIMER_ALL_ACCESS);
            LARGE_INTEGER inicio{};
            inicio.QuadPart = -1;
            SetWaitableTimer(evaluar, &inicio, 500, nullptr, nullptr, FALSE);

            for (bool seguir = true; seguir;)
            {
                std::vector<HANDLE> esperas{evaluar};
                std::vector<Zona*> deEspera{nullptr};
                for (auto& z : g_zonas)
                    if (z->Valida() && z->Temporizador())
                    {
                        esperas.push_back(z->Temporizador());
                        deEspera.push_back(z.get());
                    }
                const DWORD r = MsgWaitForMultipleObjectsEx(static_cast<DWORD>(esperas.size()), esperas.data(), INFINITE,
                                                            QS_ALLINPUT, MWMO_INPUTAVAILABLE);
                if (r == WAIT_OBJECT_0) Evaluar();
                else if (r > WAIT_OBJECT_0 && r < WAIT_OBJECT_0 + esperas.size()) deEspera[r - WAIT_OBJECT_0]->Tic();
                MSG msg;
                while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE))
                {
                    if (msg.message == WM_QUIT)
                    {
                        seguir = false;
                        break;
                    }
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
            }
            CloseHandle(evaluar);
        }
        g_zonas.clear();
        g_fondos.Reset();
        g_dispositivo.Reset();
        if (control && IsWindow(control)) DestroyWindow(control);
        MFShutdown();
        CoUninitialize();
    }
    Registrar(L"Fin (código %d)", codigo);
    CerrarRegistro();
    if (mutex)
    {
        ReleaseMutex(mutex);
        CloseHandle(mutex);
    }
    return codigo;
}
}  // namespace

int WINAPI wWinMain(HINSTANCE instancia, HINSTANCE, PWSTR, int)
{
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

    int n = 0;
    LPWSTR* args = CommandLineToArgvW(GetCommandLineW(), &n);
    if (n >= 2 && std::wstring(args[1]) == L"--info")
    {
        const int codigo = n == 4 ? EjecutarInfo(args[2], args[3]) : 2;
        LocalFree(args);
        return codigo;
    }
    if (n >= 2 && std::wstring(args[1]) == L"--reducir")
    {
        const int codigo = n == 6 || n == 7
                               ? EjecutarReducir(args[2], args[3], static_cast<UINT32>(_wtoi(args[4])),
                                                 static_cast<UINT32>(_wtoi(args[5])), n == 7 ? _wtoi64(args[6]) : 0)
                               : 2;
        LocalFree(args);
        return codigo;
    }
    for (int i = 1; i + 1 < n; ++i)
    {
        const std::wstring a = args[i];
        if (a == L"--datos") g_carpeta = args[++i];
        else if (a == L"--id") g_id = args[++i];
    }
    LocalFree(args);
    if (g_carpeta.empty() || g_id.empty()) return 2;
    while (!g_carpeta.empty() && (g_carpeta.back() == L'\\' || g_carpeta.back() == L'/')) g_carpeta.pop_back();

    wchar_t exe[MAX_PATH * 4]{};
    GetModuleFileNameW(nullptr, exe, ARRAYSIZE(exe));
    g_carpetaExe = exe;
    g_carpetaExe.erase(g_carpetaExe.find_last_of(L'\\'));
    return Reproducir(instancia);
}
