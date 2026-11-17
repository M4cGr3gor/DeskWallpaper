#pragma once
// Cabeceras y utilidades comunes del ayudante de fondos animados.

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <wrl/client.h>

#include <string>

using Microsoft::WRL::ComPtr;

// Registro en <carpeta de datos>\animado.log (Registro.cpp).
void AbrirRegistro(const std::wstring& carpetaDatos);
void Registrar(const wchar_t* formato, ...);
void CerrarRegistro();

#define COMPROBAR(x)                                                                                   \
    do                                                                                                 \
    {                                                                                                  \
        const HRESULT hr_ = (x);                                                                       \
        if (FAILED(hr_))                                                                               \
        {                                                                                              \
            Registrar(L"Error 0x%08X en %hs (línea %d)", static_cast<unsigned>(hr_), #x, __LINE__);   \
            return hr_;                                                                                \
        }                                                                                              \
    } while (0)

// Mensajes que acepta la ventana de control (los envía la app o el icono).
constexpr UINT MensajeRecargar = WM_APP + 1;
constexpr UINT MensajePausaManual = WM_APP + 2;
constexpr UINT MensajeBandeja = WM_APP + 10;
