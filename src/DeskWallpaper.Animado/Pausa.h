#pragma once
// Qué monitores están tapados por una ventana maximizada o a pantalla completa.

#include "Comun.h"

#include <vector>

/// Rectángulos (rcMonitor, píxeles físicos) de los monitores tapados.
std::vector<RECT> MonitoresTapados();
