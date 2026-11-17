#pragma once
// Una zona con vídeo: un decodificador (Media Foundation, por hardware) y una ventana por monitor detrás de los iconos,
// cada una con su trozo del vídeo.

#include "Comun.h"
#include "Config.h"
#include "Escritorio.h"

#include <d3d11.h>
#include <dxgi1_2.h>
#include <mfapi.h>
#include <mfmediaengine.h>
#include <shobjidl_core.h>

#include <memory>
#include <vector>

struct Pantalla
{
    PantallaCfg cfg;
    RECT destino{};                   // píxeles físicos del escritorio virtual (de IDesktopWallpaper)
    HWND ventana = nullptr;
    ComPtr<IDXGISwapChain1> cadena;
    bool tapada = false;
};

class Notificador;

class Zona
{
public:
    Zona(ID3D11Device* dispositivo, const ZonaCfg& cfg, int topeFps);
    ~Zona();
    Zona(const Zona&) = delete;
    Zona& operator=(const Zona&) = delete;

    /// False si el vídeo no existe o no se puede reproducir: la zona no dibuja nada (queda el fondo de reserva).
    bool Valida() const { return valida_; }

    /// Coloca o recoloca sus ventanas. Un monitor que ya no existe se deja de dibujar. False si algún monitor que sí
    /// existe no ha quedado colocado (hay que reintentar).
    bool Montar(const Escritorio& e, IDesktopWallpaper* fondos);
    void Desmontar();

    HANDLE Temporizador() const { return temporizador_; }

    /// En cada vuelta del temporizador: si hay un fotograma nuevo (y el tope de fps lo permite), lo presenta.
    void Tic();

    /// Opción A: un monitor tapado no se dibuja; la zona se para del todo si están tapados todos o hay pausa global.
    void Pausa(const std::vector<RECT>& tapados, bool pausaGlobal);

private:
    void Arrancar();
    void Presentar(Pantalla& p);
    void CalcularPeriodo();
    static HRESULT CrearCadena(ID3D11Device* dispositivo, Pantalla& p);

    std::wstring video_;
    int topeFps_;
    ComPtr<ID3D11Device> dispositivo_;
    ComPtr<IMFDXGIDeviceManager> gestor_;
    ComPtr<IMFMediaEngine> motor_;
    ComPtr<Notificador> notificador_;
    std::vector<std::unique_ptr<Pantalla>> pantallas_;
    HANDLE temporizador_ = nullptr;
    LONG periodoMs_ = 16;
    bool periodoCalculado_ = false;
    bool valida_ = false;
    bool pausada_ = false;
    LONGLONG ultimaPresentacion_ = 0;
    LONGLONG frecuencia_ = 1;
};

/// Clase de ventana de las pantallas (se registra una vez al empezar).
void RegistrarClasePantalla(HINSTANCE instancia);
