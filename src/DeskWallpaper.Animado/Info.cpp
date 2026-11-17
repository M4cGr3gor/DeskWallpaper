// Modo «--info» del ayudante: fotograma y datos de un vídeo para la biblioteca de fondos animados
//. Usa Media Foundation igual que la reproducción, así que lo que se puede leer aquí
// se puede reproducir como fondo.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "Info.h"

#include <windows.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace
{
constexpr LONGLONG UnSegundo = 10'000'000;   // en unidades de 100 ns

void Escribir(DWORD flujo, const std::string& texto)
{
    DWORD escritos = 0;
    WriteFile(GetStdHandle(flujo), texto.data(), static_cast<DWORD>(texto.size()), &escritos, nullptr);
}

struct Fotograma
{
    UINT32 ancho = 0, alto = 0;
    LONGLONG duracion = 0;          // 100 ns
    std::vector<BYTE> bgrx;         // de arriba abajo, ancho * 4 por fila
};

/// Rectángulo visible: la apertura mínima si el vídeo la declara (H.264 codifica 1088 filas para 1080).
void Visible(IMFMediaType* tipo, UINT32 anchoTotal, UINT32 altoTotal, LONG& x, LONG& y, UINT32& ancho, UINT32& alto)
{
    MFVideoArea area{};
    if (SUCCEEDED(tipo->GetBlob(MF_MT_MINIMUM_DISPLAY_APERTURE, reinterpret_cast<UINT8*>(&area), sizeof area, nullptr)) &&
        area.Area.cx > 0 && area.Area.cy > 0)
    {
        x = area.OffsetX.value;
        y = area.OffsetY.value;
        ancho = static_cast<UINT32>(area.Area.cx);
        alto = static_cast<UINT32>(area.Area.cy);
        return;
    }
    x = y = 0;
    ancho = anchoTotal;
    alto = altoTotal;
}

HRESULT Leer(const wchar_t* video, Fotograma& f)
{
    ComPtr<IMFAttributes> opciones;
    HRESULT hr = MFCreateAttributes(&opciones, 1);
    if (FAILED(hr)) return hr;
    opciones->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE);   // convierte a RGB32 por nosotros

    ComPtr<IMFSourceReader> lector;
    if (FAILED(hr = MFCreateSourceReaderFromURL(video, opciones.Get(), &lector))) return hr;
    const DWORD flujo = static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    lector->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
    if (FAILED(hr = lector->SetStreamSelection(flujo, TRUE))) return hr;

    ComPtr<IMFMediaType> pedido;
    if (FAILED(hr = MFCreateMediaType(&pedido))) return hr;
    pedido->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    pedido->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32);
    if (FAILED(hr = lector->SetCurrentMediaType(flujo, nullptr, pedido.Get()))) return hr;
    ComPtr<IMFMediaType> tipo;
    if (FAILED(hr = lector->GetCurrentMediaType(flujo, &tipo))) return hr;
    UINT32 anchoTotal = 0, altoTotal = 0;
    if (FAILED(hr = MFGetAttributeSize(tipo.Get(), MF_MT_FRAME_SIZE, &anchoTotal, &altoTotal))) return hr;
    LONG x0 = 0, y0 = 0;
    Visible(tipo.Get(), anchoTotal, altoTotal, x0, y0, f.ancho, f.alto);
    if (f.ancho == 0 || f.alto == 0) return MF_E_INVALIDMEDIATYPE;
    INT32 pasoTipo = 0;
    if (FAILED(tipo->GetUINT32(MF_MT_DEFAULT_STRIDE, reinterpret_cast<UINT32*>(&pasoTipo)))) pasoTipo = static_cast<INT32>(anchoTotal * 4);

    PROPVARIANT valor;
    PropVariantInit(&valor);
    if (SUCCEEDED(lector->GetPresentationAttribute(static_cast<DWORD>(MF_SOURCE_READER_MEDIASOURCE), MF_PD_DURATION, &valor)))
        f.duracion = static_cast<LONGLONG>(valor.uhVal.QuadPart);
    PropVariantClear(&valor);
    if (f.duracion <= 0) f.duracion = DuracionRecorriendo(video);

    // Al segundo 1 (lo habitual es que el primer fotograma sea un fundido a negro); si dura menos, el primero.
    const LONGLONG objetivo = f.duracion > UnSegundo ? UnSegundo : 0;
    if (objetivo > 0)
    {
        PROPVARIANT posicion;
        PropVariantInit(&posicion);
        posicion.vt = VT_I8;
        posicion.hVal.QuadPart = objetivo;
        lector->SetCurrentPosition(GUID_NULL, posicion);   // va al fotograma clave anterior; se avanza abajo
    }

    ComPtr<IMFSample> elegido;
    for (int i = 0; i < 900; ++i)
    {
        DWORD banderas = 0;
        LONGLONG instante = 0;
        ComPtr<IMFSample> muestra;
        if (FAILED(hr = lector->ReadSample(flujo, 0, nullptr, &banderas, &instante, &muestra))) return hr;
        if (muestra) elegido = muestra;
        if ((banderas & MF_SOURCE_READERF_ENDOFSTREAM) || (muestra && instante + UnSegundo / 100 >= objetivo)) break;
    }
    if (!elegido) return MF_E_END_OF_STREAM;

    ComPtr<IMFMediaBuffer> buffer;
    if (FAILED(hr = elegido->ConvertToContiguousBuffer(&buffer))) return hr;
    f.bgrx.resize(static_cast<size_t>(f.ancho) * f.alto * 4);
    ComPtr<IMF2DBuffer> buffer2d;
    BYTE* fila0 = nullptr;
    LONG paso = 0;
    BYTE* datos = nullptr;
    DWORD longitud = 0;
    const bool es2d = SUCCEEDED(buffer.As(&buffer2d)) && SUCCEEDED(buffer2d->Lock2D(&fila0, &paso));
    if (!es2d)
    {
        if (FAILED(hr = buffer->Lock(&datos, nullptr, &longitud))) return hr;
        paso = pasoTipo;
        // Con paso negativo (de abajo arriba) la primera fila está al final del búfer.
        fila0 = paso < 0 ? datos + static_cast<size_t>(-paso) * (altoTotal - 1) : datos;
    }
    for (UINT32 y = 0; y < f.alto; ++y)
        memcpy(f.bgrx.data() + static_cast<size_t>(y) * f.ancho * 4,
               fila0 + static_cast<ptrdiff_t>(y0 + static_cast<LONG>(y)) * paso + static_cast<ptrdiff_t>(x0) * 4,
               static_cast<size_t>(f.ancho) * 4);
    if (es2d) buffer2d->Unlock2D();
    else buffer->Unlock();
    return S_OK;
}

HRESULT GuardarJpeg(const wchar_t* ruta, const Fotograma& f)
{
    ComPtr<IWICImagingFactory> wic;
    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&wic));
    if (FAILED(hr)) return hr;
    ComPtr<IWICStream> flujo;
    if (FAILED(hr = wic->CreateStream(&flujo))) return hr;
    if (FAILED(hr = flujo->InitializeFromFilename(ruta, GENERIC_WRITE))) return hr;
    ComPtr<IWICBitmapEncoder> codificador;
    if (FAILED(hr = wic->CreateEncoder(GUID_ContainerFormatJpeg, nullptr, &codificador))) return hr;
    if (FAILED(hr = codificador->Initialize(flujo.Get(), WICBitmapEncoderNoCache))) return hr;
    ComPtr<IWICBitmapFrameEncode> marco;
    ComPtr<IPropertyBag2> propiedades;
    if (FAILED(hr = codificador->CreateNewFrame(&marco, &propiedades))) return hr;
    PROPBAG2 nombre{};
    nombre.pstrName = const_cast<LPOLESTR>(L"ImageQuality");
    VARIANT calidad;
    VariantInit(&calidad);
    calidad.vt = VT_R4;
    calidad.fltVal = 0.9f;
    propiedades->Write(1, &nombre, &calidad);
    if (FAILED(hr = marco->Initialize(propiedades.Get()))) return hr;
    if (FAILED(hr = marco->SetSize(f.ancho, f.alto))) return hr;
    // El JPEG no admite 32 bits: SetPixelFormat lo cambia a 24. Con WriteSource, WIC convierte desde los BGRX.
    WICPixelFormatGUID formato = GUID_WICPixelFormat24bppBGR;
    if (FAILED(hr = marco->SetPixelFormat(&formato))) return hr;
    ComPtr<IWICBitmap> origen;
    if (FAILED(hr = wic->CreateBitmapFromMemory(f.ancho, f.alto, GUID_WICPixelFormat32bppBGR, f.ancho * 4,
                                                static_cast<UINT>(f.bgrx.size()), const_cast<BYTE*>(f.bgrx.data()), &origen)))
        return hr;
    if (FAILED(hr = marco->WriteSource(origen.Get(), nullptr))) return hr;
    if (FAILED(hr = marco->Commit())) return hr;
    return codificador->Commit();
}

// Todo el trabajo aquí: los objetos COM se liberan al salir, antes de apagar Media Foundation y COM
//.
HRESULT Trabajar(const wchar_t* video, const wchar_t* salidaJpg, Fotograma& f)
{
    const HRESULT hr = Leer(video, f);
    if (FAILED(hr)) return hr;
    return GuardarJpeg(salidaJpg, f);
}
}  // namespace

long long DuracionRecorriendo(const wchar_t* video)
{
    ComPtr<IMFSourceReader> lector;
    if (FAILED(MFCreateSourceReaderFromURL(video, nullptr, &lector))) return 0;
    const DWORD flujo = static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    lector->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE);
    if (FAILED(lector->SetStreamSelection(flujo, TRUE))) return 0;
    LONGLONG inicio = -1, fin = 0;
    for (;;)
    {
        DWORD indice = 0, banderas = 0;
        LONGLONG instante = 0;
        ComPtr<IMFSample> muestra;
        if (FAILED(lector->ReadSample(flujo, 0, &indice, &banderas, &instante, &muestra))) break;
        if (banderas & (MF_SOURCE_READERF_ENDOFSTREAM | MF_SOURCE_READERF_ERROR)) break;
        if (!muestra) continue;
        LONGLONG duracion = 0;
        muestra->GetSampleDuration(&duracion);
        if (inicio < 0 || instante < inicio) inicio = instante;
        fin = std::max(fin, instante + duracion);
    }
    return inicio < 0 ? 0 : fin - inicio;
}

int EjecutarInfo(const wchar_t* video, const wchar_t* salidaJpg)
{
    if (FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED))) return 3;
    if (FAILED(MFStartup(MF_VERSION)))
    {
        CoUninitialize();
        return 3;
    }
    Fotograma f;
    const HRESULT hr = Trabajar(video, salidaJpg, f);
    MFShutdown();
    CoUninitialize();
    if (FAILED(hr))
    {
        DeleteFileW(salidaJpg);
        char texto[96];
        sprintf_s(texto, "No se puede leer el vídeo (0x%08X)\n", static_cast<unsigned>(hr));
        Escribir(STD_ERROR_HANDLE, texto);
        return 3;
    }
    char json[128];
    sprintf_s(json, "{\"ancho\":%u,\"alto\":%u,\"duracionMs\":%lld}\n", f.ancho, f.alto, f.duracion / 10'000);
    Escribir(STD_OUTPUT_HANDLE, json);
    return 0;
}
