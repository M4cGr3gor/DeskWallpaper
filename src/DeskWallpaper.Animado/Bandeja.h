#pragma once
// Icono junto al reloj con su menú: Pausar/Reanudar, Abrir DeskWallpaper, Cerrar (vuelve al reiniciar) y
// Quitar fondo animado.

#include "Comun.h"

enum class OrdenBandeja
{
    Ninguna,
    Pausa,
    Abrir,
    Quitar,
    Cerrar,
};

/// Añade el icono (<carpetaExe>\Recursos\DeskWallpaper.ico, o el genérico si no está). Avisa con MensajeBandeja.
bool CrearIcono(HWND control, const std::wstring& carpetaExe);
void QuitarIcono(HWND control);

/// Muestra el menú junto al cursor y devuelve lo elegido.
OrdenBandeja MostrarMenu(HWND control, bool pausado);
