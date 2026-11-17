// Registro del ayudante en <carpeta de datos>\animado.log, con tope: al pasar de 1 MB se renombra a animado.log.1.

#include "Comun.h"

#include <cstdarg>
#include <cstdio>
#include <share.h>

namespace
{
FILE* g_archivo = nullptr;
constexpr LONGLONG Tope = 1024 * 1024;
}  // namespace

void AbrirRegistro(const std::wstring& carpetaDatos)
{
    const std::wstring ruta = carpetaDatos + L"\\animado.log";
    WIN32_FILE_ATTRIBUTE_DATA datos{};
    if (GetFileAttributesExW(ruta.c_str(), GetFileExInfoStandard, &datos))
    {
        const LONGLONG tamano = (static_cast<LONGLONG>(datos.nFileSizeHigh) << 32) | datos.nFileSizeLow;
        if (tamano > Tope) MoveFileExW(ruta.c_str(), (ruta + L".1").c_str(), MOVEFILE_REPLACE_EXISTING);
    }
    // Se deja leer mientras el ayudante sigue en marcha (_wfopen_s lo abriría sin compartir).
    g_archivo = _wfsopen(ruta.c_str(), L"a, ccs=UTF-8", _SH_DENYWR);
}

void Registrar(const wchar_t* formato, ...)
{
    if (!g_archivo) return;
    SYSTEMTIME t;
    GetLocalTime(&t);
    fwprintf(g_archivo, L"[%04d-%02d-%02d %02d:%02d:%02d.%03d] ", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond,
             t.wMilliseconds);
    va_list argumentos;
    va_start(argumentos, formato);
    vfwprintf(g_archivo, formato, argumentos);
    va_end(argumentos);
    fputwc(L'\n', g_archivo);
    fflush(g_archivo);
}

void CerrarRegistro()
{
    if (g_archivo) fclose(g_archivo);
    g_archivo = nullptr;
}
