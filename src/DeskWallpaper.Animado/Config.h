#pragma once
// animado.json: lo escribe la app (ConfigAnimado en C#); el ayudante solo cambia la pausa manual o lo deja sin zonas.

#include <string>
#include <vector>

struct PantallaCfg
{
    std::wstring monitorId;          // el de IDesktopWallpaper
    float u0 = 0, v0 = 0, u1 = 1, v1 = 1;
};

struct ZonaCfg
{
    std::wstring video;
    std::vector<PantallaCfg> pantallas;
};

struct Config
{
    int topeFps = 30;                // 0: como el vídeo
    bool pausarMaximizadas = true;
    bool pausaManual = false;
    std::vector<ZonaCfg> zonas;
};

/// Lee <carpeta>\animado.json. False (y la configuración vacía) si no existe o está dañado.
bool LeerConfig(const std::wstring& carpetaDatos, Config& c);

/// Cambia solo la pausa manual, conservando lo demás.
bool GuardarPausaManual(const std::wstring& carpetaDatos, bool pausa);

/// Deja el archivo sin zonas y sin pausa manual («Quitar fondo animado»).
bool GuardarSinZonas(const std::wstring& carpetaDatos);
