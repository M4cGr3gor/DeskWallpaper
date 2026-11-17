// Reproducción de una zona: un IMFMediaEngine en modo «servidor de fotogramas» y, por cada monitor, una ventana con su
// cadena de presentación a la que se copia el trozo con el procesador de vídeo de la GPU (TransferVideoFrame).

#include "Reproductor.h"

#include <atomic>
#include <algorithm>

namespace
{
constexpr wchar_t ClasePantalla[] = L"DeskWallpaper.Animado.Fondo";

LONG Ancho(const RECT& r) { return r.right - r.left; }
LONG Alto(const RECT& r) { return r.bottom - r.top; }

LRESULT CALLBACK ProcPantalla(HWND w, UINT m, WPARAM wp, LPARAM lp)
{
    switch (m)
    {
    case WM_ERASEBKGND: return 1;
    case WM_PAINT:
        ValidateRect(w, nullptr);
        return 0;
    case WM_NCDESTROY:
        // Si Explorer se cierra se lleva por delante las ventanas hijas: se recrean al recolocar.
        if (auto* p = reinterpret_cast<Pantalla*>(GetWindowLongPtrW(w, GWLP_USERDATA)))
        {
            p->cadena.Reset();
            p->ventana = nullptr;
        }
        break;
    }
    return DefWindowProcW(w, m, wp, lp);
}
}  // namespace

void RegistrarClasePantalla(HINSTANCE instancia)
{
    WNDCLASSEXW clase{};
    clase.cbSize = sizeof clase;
    clase.hInstance = instancia;
    clase.lpfnWndProc = ProcPantalla;
    clase.lpszClassName = ClasePantalla;
    clase.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    RegisterClassExW(&clase);
}

// ── Avisos del reproductor (llegan en un hilo de Media Foundation) ─────────────

class Notificador final : public IMFMediaEngineNotify
{
    LONG referencias_ = 1;

public:
    std::atomic<bool> error{false};
    std::atomic<bool> metadatos{false};

    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override
    {
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IMFMediaEngineNotify))
        {
            *ppv = static_cast<IMFMediaEngineNotify*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&referencias_); }
    STDMETHODIMP_(ULONG) Release() override
    {
        const ULONG n = InterlockedDecrement(&referencias_);
        if (n == 0) delete this;
        return n;
    }
    STDMETHODIMP EventNotify(DWORD evento, DWORD_PTR parametro1, DWORD parametro2) override
    {
        if (evento == MF_MEDIA_ENGINE_EVENT_ERROR)
        {
            Registrar(L"Error del reproductor: %u (0x%08X)", static_cast<unsigned>(parametro1), parametro2);
            error = true;
        }
        else if (evento == MF_MEDIA_ENGINE_EVENT_LOADEDMETADATA)
            metadatos = true;
        return S_OK;
    }
};

// ── Zona ─────────────────────────────────────────────────────────────────────

Zona::Zona(ID3D11Device* dispositivo, const ZonaCfg& cfg, int topeFps)
    : video_(cfg.video), topeFps_(topeFps), dispositivo_(dispositivo)
{
    for (const auto& p : cfg.pantallas)
    {
        auto pantalla = std::make_unique<Pantalla>();
        pantalla->cfg = p;
        pantallas_.push_back(std::move(pantalla));
    }
    LARGE_INTEGER f;
    QueryPerformanceFrequency(&f);
    frecuencia_ = f.QuadPart;

    if (GetFileAttributesW(video_.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        Registrar(L"No existe el vídeo «%ls»: esa zona se queda con el fondo de reserva", video_.c_str());
        return;
    }
    UINT ficha = 0;
    ComPtr<IMFMediaEngineClassFactory> fabrica;
    ComPtr<IMFAttributes> atributos;
    notificador_.Attach(new Notificador());
    if (FAILED(MFCreateDXGIDeviceManager(&ficha, &gestor_)) || FAILED(gestor_->ResetDevice(dispositivo, ficha)) ||
        FAILED(CoCreateInstance(CLSID_MFMediaEngineClassFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&fabrica))) ||
        FAILED(MFCreateAttributes(&atributos, 3)) ||
        FAILED(atributos->SetUnknown(MF_MEDIA_ENGINE_DXGI_MANAGER, gestor_.Get())) ||
        FAILED(atributos->SetUnknown(MF_MEDIA_ENGINE_CALLBACK, notificador_.Get())) ||
        FAILED(atributos->SetUINT32(MF_MEDIA_ENGINE_VIDEO_OUTPUT_FORMAT, DXGI_FORMAT_B8G8R8A8_UNORM)) ||
        // Sin MF_MEDIA_ENGINE_PLAYBACK_HWND: modo «servidor de fotogramas», el que permite repartir el vídeo.
        FAILED(fabrica->CreateInstance(0, atributos.Get(), &motor_)))
    {
        Registrar(L"No se puede crear el reproductor para «%ls»", video_.c_str());
        return;
    }
    motor_->SetMuted(TRUE);
    motor_->SetLoop(TRUE);
    motor_->SetAutoPlay(TRUE);
    BSTR fuente = SysAllocString(video_.c_str());
    const HRESULT hr = motor_->SetSource(fuente);
    SysFreeString(fuente);
    if (FAILED(hr))
    {
        Registrar(L"No se puede abrir «%ls» (0x%08X)", video_.c_str(), static_cast<unsigned>(hr));
        return;
    }
    temporizador_ = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    valida_ = temporizador_ != nullptr;
    if (valida_) Arrancar();
}

Zona::~Zona()
{
    Desmontar();
    if (motor_) motor_->Shutdown();
    motor_.Reset();
    gestor_.Reset();
    if (temporizador_) CloseHandle(temporizador_);
}

void Zona::Arrancar()
{
    LARGE_INTEGER inicio{};
    inicio.QuadPart = -1;
    SetWaitableTimer(temporizador_, &inicio, periodoMs_, nullptr, nullptr, FALSE);
}

void Zona::CalcularPeriodo()
{
    periodoCalculado_ = true;
    double fps = 0;
    ComPtr<IMFMediaEngineEx> ex;
    DWORD flujos = 0;
    if (SUCCEEDED(motor_.As(&ex)) && SUCCEEDED(ex->GetNumberOfStreams(&flujos)))
        for (DWORD i = 0; i < flujos && fps == 0; ++i)
        {
            PROPVARIANT v;
            PropVariantInit(&v);
            if (SUCCEEDED(ex->GetStreamAttribute(i, MF_MT_FRAME_RATE, &v)) && v.vt == VT_UI8)
            {
                const UINT32 num = static_cast<UINT32>(v.uhVal.QuadPart >> 32), den = static_cast<UINT32>(v.uhVal.QuadPart);
                if (num && den) fps = static_cast<double>(num) / den;
            }
            PropVariantClear(&v);
        }
    // Se presenta como mucho cada «intervalo» (el del vídeo, o el del tope si es mayor) y se mira a la mitad de ese
    // intervalo: así no se pierde ninguno que toque presentar y, con un vídeo de 60 y tope de 30, se despierta la mitad.
    double intervalo = fps > 0 ? 1000.0 / fps : 1000.0 / 30;
    if (topeFps_ > 0) intervalo = std::max(intervalo, 1000.0 / topeFps_);
    periodoMs_ = std::max<LONG>(4, static_cast<LONG>(intervalo / 2));
    DWORD ancho = 0, alto = 0;
    motor_->GetNativeVideoSize(&ancho, &alto);
    Registrar(L"Vídeo abierto: «%ls» %ux%u a %.2f fps · tope %d · comprobación cada %ld ms", video_.c_str(), ancho, alto,
              fps, topeFps_, periodoMs_);
    if (!pausada_) Arrancar();
}

HRESULT Zona::CrearCadena(ID3D11Device* dispositivo, Pantalla& p)
{
    ComPtr<IDXGIDevice> dxgi;
    COMPROBAR(dispositivo->QueryInterface(IID_PPV_ARGS(&dxgi)));
    ComPtr<IDXGIAdapter> adaptador;
    COMPROBAR(dxgi->GetAdapter(&adaptador));
    ComPtr<IDXGIFactory2> fabrica;
    COMPROBAR(adaptador->GetParent(IID_PPV_ARGS(&fabrica)));
    DXGI_SWAP_CHAIN_DESC1 d{};
    d.Width = static_cast<UINT>(Ancho(p.destino));
    d.Height = static_cast<UINT>(Alto(p.destino));
    d.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    d.SampleDesc.Count = 1;
    d.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    d.BufferCount = 2;
    d.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;   // comprobado en la fase A con la estructura nueva
    d.Scaling = DXGI_SCALING_STRETCH;
    d.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    p.cadena.Reset();
    COMPROBAR(fabrica->CreateSwapChainForHwnd(dispositivo, p.ventana, &d, nullptr, nullptr, &p.cadena));
    fabrica->MakeWindowAssociation(p.ventana, DXGI_MWA_NO_ALT_ENTER | DXGI_MWA_NO_WINDOW_CHANGES);
    return S_OK;
}

bool Zona::Montar(const Escritorio& e, IDesktopWallpaper* fondos)
{
    if (!valida_) return true;
    bool todo = true;
    for (auto& p : pantallas_)
    {
        if (p->ventana && !IsWindow(p->ventana)) p->ventana = nullptr;
        RECT r{};
        if (!fondos || FAILED(fondos->GetMonitorRECT(p->cfg.monitorId.c_str(), &r)) || IsRectEmpty(&r))
        {
            if (p->ventana)
            {
                Registrar(L"Monitor no disponible: se deja de dibujar en él");
                DestroyWindow(p->ventana);
            }
            continue;
        }
        if (p->ventana && !EqualRect(&r, &p->destino))
        {
            // Monitor movido o con otra resolución: ventana nueva. Una cadena «flip» nueva sobre la misma ventana puede
            // fallar mientras la anterior no se ha destruido del todo (destrucción diferida de DXGI) y dejarla en blanco.
            Registrar(L"El monitor ha cambiado de sitio o de tamaño: ventana nueva");
            DestroyWindow(p->ventana);   // WM_NCDESTROY suelta la cadena y deja ventana a nullptr
        }
        p->destino = r;
        if (!p->ventana)
        {
            p->cadena.Reset();
            p->ventana = CreateWindowExW(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW, ClasePantalla, L"", WS_POPUP, r.left, r.top,
                                         Ancho(r), Alto(r), nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
            if (!p->ventana)
            {
                todo = false;
                continue;
            }
            SetWindowLongPtrW(p->ventana, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(p.get()));
        }
        if (!Colocar(p->ventana, r, e))
        {
            Registrar(L"No se puede colocar detrás de los iconos (error %lu)", GetLastError());
            todo = false;
            continue;
        }
        if (!p->cadena && FAILED(CrearCadena(dispositivo_.Get(), *p))) todo = false;
        Presentar(*p);   // el fotograma actual, aunque esté en pausa
    }
    return todo;
}

void Zona::Desmontar()
{
    for (auto& p : pantallas_)
        if (p->ventana) DestroyWindow(p->ventana);   // WM_NCDESTROY suelta la cadena
}

void Zona::Presentar(Pantalla& p)
{
    if (!p.cadena || !motor_) return;
    ComPtr<ID3D11Texture2D> buffer;
    if (FAILED(p.cadena->GetBuffer(0, IID_PPV_ARGS(&buffer)))) return;
    RECT destino{0, 0, Ancho(p.destino), Alto(p.destino)};
    const MFVideoNormalizedRect origen{p.cfg.u0, p.cfg.v0, p.cfg.u1, p.cfg.v1};
    const MFARGB negro{0, 0, 0, 255};
    // Recorta y escala el trozo de este monitor directamente en su búfer, con el procesador de vídeo de la GPU.
    if (SUCCEEDED(motor_->TransferVideoFrame(buffer.Get(), &origen, &destino, &negro))) p.cadena->Present(0, 0);
}

void Zona::Tic()
{
    if (!valida_) return;
    if (notificador_->error)
    {
        Registrar(L"No se puede reproducir «%ls»: esa zona se queda con el fondo de reserva", video_.c_str());
        valida_ = false;
        CancelWaitableTimer(temporizador_);
        Desmontar();
        return;
    }
    if (!periodoCalculado_ && notificador_->metadatos) CalcularPeriodo();

    LONGLONG instante = 0;
    if (motor_->OnVideoStreamTick(&instante) != S_OK) return;   // no hay fotograma nuevo
    LARGE_INTEGER ahora;
    QueryPerformanceCounter(&ahora);
    if (topeFps_ > 0)
    {
        // Con margen de 2 ms para no perder el que toca por el temblor del temporizador.
        const double minimo = 1.0 / topeFps_ - 0.002;
        if (static_cast<double>(ahora.QuadPart - ultimaPresentacion_) / frecuencia_ < minimo) return;
    }
    ultimaPresentacion_ = ahora.QuadPart;
    for (auto& p : pantallas_)
        if (!p->tapada) Presentar(*p);
}

void Zona::Pausa(const std::vector<RECT>& tapados, bool pausaGlobal)
{
    if (!valida_) return;
    bool alguna = false, todas = true;
    for (auto& p : pantallas_)
    {
        if (!p->ventana) continue;
        alguna = true;
        bool tapada = false;
        for (const RECT& t : tapados)
        {
            RECT comun;
            if (IntersectRect(&comun, &t, &p->destino)) tapada = true;
        }
        if (tapada != p->tapada) Registrar(L"Monitor %ls: %ls", p->cfg.monitorId.c_str(), tapada ? L"tapado" : L"a la vista");
        p->tapada = tapada;
        todas = todas && tapada;
    }
    const bool pausar = pausaGlobal || !alguna || todas;
    if (pausar == pausada_) return;
    pausada_ = pausar;
    if (pausar)
    {
        motor_->Pause();
        CancelWaitableTimer(temporizador_);
        Registrar(L"Pausa de «%ls»", video_.c_str());
    }
    else
    {
        motor_->Play();
        Arrancar();
        Registrar(L"Reanuda «%ls»", video_.c_str());
    }
}
