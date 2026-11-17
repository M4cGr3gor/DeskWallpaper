#pragma once

#include <windows.h>

// Modo «--reducir»: copia el vídeo a otro MP4 (H.264, sin sonido) del tamaño pedido, para gastar menos memoria cuando
// el original es más grande que los monitores donde se ve. Sin ventanas. Va sacando por la salida estándar líneas
// «progreso 0.42» (sobre la duración en ms que se le pase, o la del vídeo si es 0) y al final «listo». Devuelve 0 si
// ha ido bien y 3 si no (el motivo va a la salida de errores; la salida a medias se borra). El original nunca se toca.
int EjecutarReducir(const wchar_t* entrada, const wchar_t* salida, UINT32 ancho, UINT32 alto, LONGLONG duracionMs);
