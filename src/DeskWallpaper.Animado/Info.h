#pragma once

// Modo «--info»: escribe un fotograma del vídeo en JPEG (al segundo 1, o al principio si dura menos) y saca por la
// salida estándar una línea JSON con su ancho, alto y duración en milisegundos. Sin ventanas.
// Devuelve 0 si ha ido bien y 3 si el vídeo no se puede leer (el motivo va a la salida de errores).
int EjecutarInfo(const wchar_t* video, const wchar_t* salidaJpg);

// Duración (en unidades de 100 ns) recorriendo las muestras comprimidas del vídeo, sin decodificarlas. Para los MP4
// que no la declaran (fragmentados, como muchos de los que se descargan): Media Foundation la da como 0. 0 si falla.
long long DuracionRecorriendo(const wchar_t* video);
