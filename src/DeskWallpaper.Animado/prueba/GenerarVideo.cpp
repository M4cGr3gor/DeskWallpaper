// Genera un vídeo de prueba (H.264 en MP4) para comprobar el reparto entre monitores sin descargar nada.
// Uso: GenerarVideoPrueba.exe <salida.mp4> [ancho=3840] [alto=1080] [segundos=20] [fps=30]
//
// Fondo de colores que se desplaza, líneas verticales cada 240 px con su coordenada, «IZQUIERDA» y «DERECHA» en
// cada mitad, un círculo que cruza todo el ancho y el número de fotograma justo en el centro (partido entre los dos
// monitores): si se ve continuo, el reparto es correcto.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <cmath>
#include <cstdio>
#include <cwchar>
#include <string>

using Microsoft::WRL::ComPtr;

#define COMPROBAR(x)                                                                              \
    do                                                                                            \
    {                                                                                             \
        const HRESULT hr_ = (x);                                                                  \
        if (FAILED(hr_))                                                                          \
        {                                                                                         \
            fwprintf(stderr, L"Error 0x%08X en %hs\n", static_cast<unsigned>(hr_), #x);          \
            return 1;                                                                             \
        }                                                                                         \
    } while (0)

namespace
{
D2D1_COLOR_F ColorHsv(float h, float s, float v)
{
    h = std::fmod(h, 1.0f) * 6.0f;
    const int i = static_cast<int>(h);
    const float f = h - static_cast<float>(i), p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
    switch (i % 6)
    {
    case 0: return {v, t, p, 1};
    case 1: return {q, v, p, 1};
    case 2: return {p, v, t, 1};
    case 3: return {p, q, v, 1};
    case 4: return {t, p, v, 1};
    default: return {v, p, q, 1};
    }
}
}  // namespace

// Todo el trabajo aquí: sus objetos COM se liberan al salir, antes de apagar Media Foundation y COM.
int Generar(int argc, wchar_t** argv)
{
    if (argc < 2)
    {
        fwprintf(stderr, L"Uso: GenerarVideoPrueba <salida.mp4> [ancho] [alto] [segundos] [fps]\n");
        return 2;
    }
    const UINT32 ancho = argc > 2 ? static_cast<UINT32>(_wtoi(argv[2])) : 3840;
    const UINT32 alto = argc > 3 ? static_cast<UINT32>(_wtoi(argv[3])) : 1080;
    const UINT32 segundos = argc > 4 ? static_cast<UINT32>(_wtoi(argv[4])) : 20;
    const UINT32 Fps = argc > 5 ? static_cast<UINT32>(_wtoi(argv[5])) : 30;
    const UINT32 fotogramas = segundos * Fps;
    const LONGLONG duracion = 10'000'000LL / Fps;

    // ── Codificador ──
    ComPtr<IMFAttributes> opciones;
    COMPROBAR(MFCreateAttributes(&opciones, 1));
    COMPROBAR(opciones->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE));
    ComPtr<IMFSinkWriter> escritor;
    COMPROBAR(MFCreateSinkWriterFromURL(argv[1], nullptr, opciones.Get(), &escritor));

    ComPtr<IMFMediaType> salida;
    COMPROBAR(MFCreateMediaType(&salida));
    COMPROBAR(salida->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
    COMPROBAR(salida->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264));
    COMPROBAR(salida->SetUINT32(MF_MT_AVG_BITRATE, 20'000'000));
    COMPROBAR(salida->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
    COMPROBAR(MFSetAttributeSize(salida.Get(), MF_MT_FRAME_SIZE, ancho, alto));
    COMPROBAR(MFSetAttributeRatio(salida.Get(), MF_MT_FRAME_RATE, Fps, 1));
    COMPROBAR(MFSetAttributeRatio(salida.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
    DWORD flujo = 0;
    COMPROBAR(escritor->AddStream(salida.Get(), &flujo));

    ComPtr<IMFMediaType> entrada;
    COMPROBAR(MFCreateMediaType(&entrada));
    COMPROBAR(entrada->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
    COMPROBAR(entrada->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32));
    COMPROBAR(entrada->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
    COMPROBAR(entrada->SetUINT32(MF_MT_DEFAULT_STRIDE, ancho * 4));   // de arriba abajo
    COMPROBAR(MFSetAttributeSize(entrada.Get(), MF_MT_FRAME_SIZE, ancho, alto));
    COMPROBAR(MFSetAttributeRatio(entrada.Get(), MF_MT_FRAME_RATE, Fps, 1));
    COMPROBAR(MFSetAttributeRatio(entrada.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
    COMPROBAR(escritor->SetInputMediaType(flujo, entrada.Get(), nullptr));
    COMPROBAR(escritor->BeginWriting());

    // ── Dibujo (Direct2D sobre un mapa de bits de WIC) ──
    ComPtr<IWICImagingFactory> wic;
    COMPROBAR(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic)));
    ComPtr<IWICBitmap> lienzo;
    COMPROBAR(wic->CreateBitmap(ancho, alto, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, &lienzo));
    ComPtr<ID2D1Factory> d2d;
    COMPROBAR(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.GetAddressOf()));
    ComPtr<ID2D1RenderTarget> rt;
    COMPROBAR(d2d->CreateWicBitmapRenderTarget(lienzo.Get(), D2D1::RenderTargetProperties(), &rt));
    ComPtr<IDWriteFactory> dw;
    COMPROBAR(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
                                  reinterpret_cast<IUnknown**>(dw.GetAddressOf())));
    ComPtr<IDWriteTextFormat> grande, pequena;
    COMPROBAR(dw->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_BOLD, DWRITE_FONT_STYLE_NORMAL,
                                   DWRITE_FONT_STRETCH_NORMAL, 120.0f, L"es-es", &grande));
    COMPROBAR(dw->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD, DWRITE_FONT_STYLE_NORMAL,
                                   DWRITE_FONT_STRETCH_NORMAL, 36.0f, L"es-es", &pequena));
    grande->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
    pequena->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
    ComPtr<ID2D1SolidColorBrush> pincel;
    COMPROBAR(rt->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::White), &pincel));

    const float an = static_cast<float>(ancho), al = static_cast<float>(alto);
    for (UINT32 i = 0; i < fotogramas; ++i)
    {
        const float t = static_cast<float>(i) / Fps;
        rt->BeginDraw();
        for (UINT32 x = 0; x < ancho; x += 32)
        {
            pincel->SetColor(ColorHsv(static_cast<float>(x) / an + t * 0.05f, 0.55f, 0.55f));
            rt->FillRectangle(D2D1::RectF(static_cast<float>(x), 0, static_cast<float>(x + 32), al), pincel.Get());
        }
        pincel->SetColor(D2D1::ColorF(D2D1::ColorF::White, 0.8f));
        for (UINT32 x = 240; x < ancho; x += 240)
        {
            const float fx = static_cast<float>(x);
            rt->DrawLine({fx, 0}, {fx, al}, pincel.Get(), 3.0f);
            wchar_t etiqueta[16];
            swprintf_s(etiqueta, L"%u", x);
            rt->DrawText(etiqueta, static_cast<UINT32>(wcslen(etiqueta)), pequena.Get(),
                         D2D1::RectF(fx - 100, al - 70, fx + 100, al - 20), pincel.Get());
        }
        pincel->SetColor(D2D1::ColorF(D2D1::ColorF::White));
        rt->DrawText(L"IZQUIERDA", 9, grande.Get(), D2D1::RectF(0, al * 0.30f, an / 2, al * 0.30f + 170), pincel.Get());
        rt->DrawText(L"DERECHA", 7, grande.Get(), D2D1::RectF(an / 2, al * 0.30f, an, al * 0.30f + 170), pincel.Get());
        wchar_t numero[48];
        swprintf_s(numero, L"fotograma %u", i);
        rt->DrawText(numero, static_cast<UINT32>(wcslen(numero)), grande.Get(),
                     D2D1::RectF(an / 2 - 900, al * 0.62f, an / 2 + 900, al * 0.62f + 170), pincel.Get());
        // Círculo que cruza todo el ancho en 6 s.
        const float cx = std::fmod(t * an / 6.0f, an);
        pincel->SetColor(D2D1::ColorF(D2D1::ColorF::Yellow));
        rt->FillEllipse(D2D1::Ellipse({cx, al * 0.15f}, 90, 90), pincel.Get());
        COMPROBAR(rt->EndDraw());

        ComPtr<IMFMediaBuffer> buffer;
        COMPROBAR(MFCreateMemoryBuffer(ancho * alto * 4, &buffer));
        {
            WICRect zona{0, 0, static_cast<INT>(ancho), static_cast<INT>(alto)};
            ComPtr<IWICBitmapLock> bloqueo;
            COMPROBAR(lienzo->Lock(&zona, WICBitmapLockRead, &bloqueo));
            UINT bytes = 0, paso = 0;
            BYTE* origen = nullptr;
            COMPROBAR(bloqueo->GetDataPointer(&bytes, &origen));
            COMPROBAR(bloqueo->GetStride(&paso));
            BYTE* destino = nullptr;
            COMPROBAR(buffer->Lock(&destino, nullptr, nullptr));
            MFCopyImage(destino, static_cast<LONG>(ancho * 4), origen, static_cast<LONG>(paso), ancho * 4, alto);
            buffer->Unlock();
        }
        COMPROBAR(buffer->SetCurrentLength(ancho * alto * 4));
        ComPtr<IMFSample> muestra;
        COMPROBAR(MFCreateSample(&muestra));
        COMPROBAR(muestra->AddBuffer(buffer.Get()));
        COMPROBAR(muestra->SetSampleTime(static_cast<LONGLONG>(i) * duracion));
        COMPROBAR(muestra->SetSampleDuration(duracion));
        COMPROBAR(escritor->WriteSample(flujo, muestra.Get()));
    }
    COMPROBAR(escritor->Finalize());
    wprintf(L"OK: %ls (%ux%u, %u s)\n", argv[1], ancho, alto, segundos);
    return 0;
}

int wmain(int argc, wchar_t** argv)
{
    if (FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)) || FAILED(MFStartup(MF_VERSION))) return 1;
    const int codigo = Generar(argc, argv);
    MFShutdown();
    CoUninitialize();
    return codigo;
}
