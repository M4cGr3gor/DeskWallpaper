#pragma once
// Colocarse detrás de los iconos del escritorio.

#include "Comun.h"

struct Escritorio
{
    HWND progman = nullptr;
    HWND defView = nullptr;   // la ventana de los iconos
    HWND workerW = nullptr;   // la que dibuja el fondo de Windows
    bool nuevo = false;       // estructura de Windows 11 24H2 y posteriores
};

/// Busca Progman, los iconos y el WorkerW (pidiéndole a Progman que lo cree).
bool BuscarEscritorio(Escritorio& e);

/// Hace de <paramref name="ventana"/> una hija del escritorio, justo debajo de los iconos, en <paramref name="destino"/>
/// (píxeles físicos del escritorio virtual).
bool Colocar(HWND ventana, const RECT& destino, const Escritorio& e);
