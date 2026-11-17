// Modo «--reducir» del ayudante: copia reducida de un vídeo a la resolución de los monitores donde se ve (opción
// «Reducir los vídeos más grandes que sus monitores»). Lee con el lector de Media Foundation, que redimensiona con su
// procesado de vídeo avanzado, y escribe con el codificador H.264 de Windows (el de la tarjeta si lo hay).

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "Reducir.h"

#include "Info.h"

#include <codecapi.h>
#include <mfapi.h>
#include <mferror.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>

#include <algorithm>
#include <cstdio>
#include <string>

using Microsoft::WRL::ComPtr;

namespace
{
void Escribir(DWORD flujo, const std::string& texto)
{
    DWORD escritos = 0;
    WriteFile(GetStdHandle(flujo), texto.data(), static_cast<DWORD>(texto.size()), &escritos, nullptr);
}

#define PROBAR(expr, que)                                                                                              \
    do                                                                                                                 \
    {                                                                                                                  \
        const HRESULT hr_ = (expr);                                                                                    \
        if (FAILED(hr_))                                                                                               \
        {                                                                                                              \
            char m_[160];                                                                                              \
            sprintf_s(m_, "%s (0x%08lX)\n", que, static_cast<unsigned long>(hr_));                                    \
            Escribir(STD_ERROR_HANDLE, m_);                                                                            \
            return hr_;                                                                                                \
        }                                                                                                              \
    } while (false)

/// Bits por segundo para una calidad alta a ese tamaño: 0,12 bits por píxel y fotograma, entre 2 y 40 Mbit/s.
UINT32 Tasa(UINT32 ancho, UINT32 alto, double fps)
{
    const double bits = 0.12 * ancho * alto * (fps > 0 ? fps : 30);
    return static_cast<UINT32>(std::clamp(bits, 2'000'000.0, 40'000'000.0));
}

LONGLONG Duracion(IMFSourceReader* lector)
{
    PROPVARIANT valor;
    PropVariantInit(&valor);
    LONGLONG total = 0;
    if (SUCCEEDED(lector->GetPresentationAttribute(static_cast<DWORD>(MF_SOURCE_READER_MEDIASOURCE), MF_PD_DURATION, &valor)))
        total = static_cast<LONGLONG>(valor.uhVal.QuadPart);
    PropVariantClear(&valor);
    return total;
}

HRESULT Copiar(const wchar_t* entrada, const wchar_t* salida, UINT32 ancho, UINT32 alto, LONGLONG duracionConocida)
{
    ComPtr<IMFAttributes> opcionesLector;
    PROBAR(MFCreateAttributes(&opcionesLector, 1), "Sin atributos del lector");
    PROBAR(opcionesLector->SetUINT32(MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, TRUE), "Sin procesado de vídeo");
    ComPtr<IMFSourceReader> lector;
    PROBAR(MFCreateSourceReaderFromURL(entrada, opcionesLector.Get(), &lector), "No se puede abrir el vídeo");
    PROBAR(lector->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE), "Sin flujos");
    PROBAR(lector->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), TRUE), "Sin vídeo");

    ComPtr<IMFMediaType> nativo;
    PROBAR(lector->GetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, &nativo), "Sin formato");
    UINT32 num = 30, den = 1;
    MFGetAttributeRatio(nativo.Get(), MF_MT_FRAME_RATE, &num, &den);
    if (num == 0 || den == 0)
    {
        num = 30;
        den = 1;
    }

    // Lo que sale del lector: NV12 del tamaño pedido (el procesado avanzado redimensiona y convierte el color).
    ComPtr<IMFMediaType> nv12;
    PROBAR(MFCreateMediaType(&nv12), "Sin tipo");
    nv12->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    nv12->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_NV12);
    MFSetAttributeSize(nv12.Get(), MF_MT_FRAME_SIZE, ancho, alto);
    MFSetAttributeRatio(nv12.Get(), MF_MT_FRAME_RATE, num, den);
    MFSetAttributeRatio(nv12.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    nv12->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    PROBAR(lector->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), nullptr, nv12.Get()),
           "No se puede redimensionar el vídeo");
    // La duración para el progreso: la que pasa la app (la sabe por --info); si no, la que declara el vídeo y, si no la
    // declara (MP4 fragmentado, como uno de Pexels), recorriendo sus muestras.
    LONGLONG total = duracionConocida > 0 ? duracionConocida : Duracion(lector.Get());
    if (total <= 0) total = DuracionRecorriendo(entrada);

    ComPtr<IMFAttributes> opcionesEscritor;
    PROBAR(MFCreateAttributes(&opcionesEscritor, 2), "Sin atributos del escritor");
    opcionesEscritor->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, TRUE);
    opcionesEscritor->SetGUID(MF_TRANSCODE_CONTAINERTYPE, MFTranscodeContainerType_MPEG4);
    ComPtr<IMFSinkWriter> escritor;
    PROBAR(MFCreateSinkWriterFromURL(salida, nullptr, opcionesEscritor.Get(), &escritor), "No se puede crear el MP4");

    ComPtr<IMFMediaType> h264;
    PROBAR(MFCreateMediaType(&h264), "Sin tipo");
    h264->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video);
    h264->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264);
    h264->SetUINT32(MF_MT_AVG_BITRATE, Tasa(ancho, alto, static_cast<double>(num) / den));
    h264->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive);
    h264->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_High);
    MFSetAttributeSize(h264.Get(), MF_MT_FRAME_SIZE, ancho, alto);
    MFSetAttributeRatio(h264.Get(), MF_MT_FRAME_RATE, num, den);
    MFSetAttributeRatio(h264.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
    DWORD flujo = 0;
    PROBAR(escritor->AddStream(h264.Get(), &flujo), "El codificador H.264 no acepta ese tamaño");
    PROBAR(escritor->SetInputMediaType(flujo, nv12.Get(), nullptr), "El codificador no acepta NV12");
    PROBAR(escritor->BeginWriting(), "No se puede empezar a escribir");

    LONGLONG inicio = -1;
    int ultimo = -1;
    for (;;)
    {
        DWORD indice = 0, banderas = 0;
        LONGLONG instante = 0;
        ComPtr<IMFSample> muestra;
        PROBAR(lector->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM), 0, &indice, &banderas, &instante,
                                  &muestra),
               "Error al leer el vídeo");
        if (banderas & MF_SOURCE_READERF_ERROR) PROBAR(E_FAIL, "Error al leer el vídeo");
        if (banderas & MF_SOURCE_READERF_ENDOFSTREAM) break;
        if (!muestra) continue;
        if (inicio < 0)
        {
            inicio = instante;   // el vídeo nuevo empieza en 0
        }
        PROBAR(muestra->SetSampleTime(instante - inicio), "Muestra sin tiempo");
        PROBAR(escritor->WriteSample(flujo, muestra.Get()), "Error al codificar");
        if (total > 0)
        {
            const int ahora = static_cast<int>(std::clamp(100.0 * static_cast<double>(instante - inicio) / total, 0.0, 99.0));
            if (ahora != ultimo)
            {
                ultimo = ahora;
                char linea[32];
                sprintf_s(linea, "progreso %.2f\n", ahora / 100.0);
                Escribir(STD_OUTPUT_HANDLE, linea);
            }
        }
    }
    if (inicio < 0) PROBAR(E_FAIL, "El vídeo no tiene fotogramas");
    PROBAR(escritor->Finalize(), "No se puede cerrar el MP4");
    return S_OK;
}
}  // namespace

int EjecutarReducir(const wchar_t* entrada, const wchar_t* salida, UINT32 ancho, UINT32 alto, LONGLONG duracionMs)
{
    if (ancho < 2 || alto < 2 || ancho % 2 || alto % 2)
    {
        Escribir(STD_ERROR_HANDLE, "Tamaño no válido (debe ser par)\n");
        return 2;
    }
    if (FAILED(CoInitializeEx(nullptr, COINIT_MULTITHREADED))) return 3;
    HRESULT hr = MFStartup(MF_VERSION);
    if (SUCCEEDED(hr))
    {
        hr = Copiar(entrada, salida, ancho, alto, duracionMs * 10'000);   // los objetos COM se sueltan aquí, antes de MFShutdown
        MFShutdown();
    }
    CoUninitialize();
    if (FAILED(hr))
    {
        DeleteFileW(salida);
        return 3;
    }
    Escribir(STD_OUTPUT_HANDLE, "listo\n");
    return 0;
}
