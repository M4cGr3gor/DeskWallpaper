// Lectura y escritura de animado.json con el analizador JSON de Windows (C++/WinRT, sin bibliotecas externas).

#include "Comun.h"
#include "Config.h"

#include <winrt/base.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Foundation.Collections.h>

#include <vector>

using namespace winrt::Windows::Data::Json;

namespace
{
std::wstring Ruta(const std::wstring& carpeta) { return carpeta + L"\\animado.json"; }

bool LeerTexto(const std::wstring& ruta, std::wstring& texto)
{
    HANDLE h = CreateFileW(ruta.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
                           OPEN_EXISTING, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    LARGE_INTEGER tamano{};
    GetFileSizeEx(h, &tamano);
    std::vector<char> bytes(static_cast<size_t>(tamano.QuadPart));
    DWORD leidos = 0;
    const BOOL ok = bytes.empty() || ReadFile(h, bytes.data(), static_cast<DWORD>(bytes.size()), &leidos, nullptr);
    CloseHandle(h);
    if (!ok) return false;
    size_t inicio = (leidos >= 3 && static_cast<unsigned char>(bytes[0]) == 0xEF) ? 3 : 0;   // por si lleva BOM
    const int n = MultiByteToWideChar(CP_UTF8, 0, bytes.data() + inicio, static_cast<int>(leidos - inicio), nullptr, 0);
    texto.assign(static_cast<size_t>(n), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, bytes.data() + inicio, static_cast<int>(leidos - inicio), texto.data(), n);
    return true;
}

/// Escritura atómica: .tmp y sustitución, como hace la app.
bool EscribirTexto(const std::wstring& ruta, const std::wstring& texto)
{
    const int n = WideCharToMultiByte(CP_UTF8, 0, texto.c_str(), static_cast<int>(texto.size()), nullptr, 0, nullptr, nullptr);
    std::vector<char> bytes(static_cast<size_t>(n));
    WideCharToMultiByte(CP_UTF8, 0, texto.c_str(), static_cast<int>(texto.size()), bytes.data(), n, nullptr, nullptr);
    const std::wstring temporal = ruta + L".tmp";
    HANDLE h = CreateFileW(temporal.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, 0, nullptr);
    if (h == INVALID_HANDLE_VALUE) return false;
    DWORD escritos = 0;
    const BOOL ok = WriteFile(h, bytes.data(), static_cast<DWORD>(bytes.size()), &escritos, nullptr);
    CloseHandle(h);
    return ok && MoveFileExW(temporal.c_str(), ruta.c_str(), MOVEFILE_REPLACE_EXISTING);
}

bool Modificar(const std::wstring& carpeta, bool sinZonas, bool pausa)
{
    std::wstring texto;
    JsonObject o{nullptr};
    if (!LeerTexto(Ruta(carpeta), texto) || !JsonObject::TryParse(texto, o)) o = JsonObject();
    if (sinZonas) o.SetNamedValue(L"zonas", JsonArray());
    o.SetNamedValue(L"pausaManual", JsonValue::CreateBooleanValue(pausa));
    return EscribirTexto(Ruta(carpeta), std::wstring(o.Stringify()));
}
}  // namespace

bool LeerConfig(const std::wstring& carpetaDatos, Config& c)
{
    c = {};
    std::wstring texto;
    if (!LeerTexto(Ruta(carpetaDatos), texto)) return false;
    try
    {
        JsonObject o{nullptr};
        if (!JsonObject::TryParse(texto, o)) return false;
        c.topeFps = static_cast<int>(o.GetNamedNumber(L"topeFps", 30));
        c.pausarMaximizadas = o.GetNamedBoolean(L"pausarMaximizadas", true);
        c.pausaManual = o.GetNamedBoolean(L"pausaManual", false);
        for (const auto& vz : o.GetNamedArray(L"zonas", JsonArray()))
        {
            const JsonObject z = vz.GetObject();
            ZonaCfg zona;
            zona.video = std::wstring(z.GetNamedString(L"video"));
            for (const auto& vp : z.GetNamedArray(L"pantallas", JsonArray()))
            {
                const JsonObject p = vp.GetObject();
                PantallaCfg pantalla;
                pantalla.monitorId = std::wstring(p.GetNamedString(L"monitorId"));
                pantalla.u0 = static_cast<float>(p.GetNamedNumber(L"u0"));
                pantalla.v0 = static_cast<float>(p.GetNamedNumber(L"v0"));
                pantalla.u1 = static_cast<float>(p.GetNamedNumber(L"u1"));
                pantalla.v1 = static_cast<float>(p.GetNamedNumber(L"v1"));
                zona.pantallas.push_back(pantalla);
            }
            if (!zona.video.empty() && !zona.pantallas.empty()) c.zonas.push_back(std::move(zona));
        }
        return true;
    }
    catch (const winrt::hresult_error& e)
    {
        Registrar(L"animado.json dañado: %ls", e.message().c_str());
        c = {};
        return false;
    }
}

bool GuardarPausaManual(const std::wstring& carpetaDatos, bool pausa)
{
    try { return Modificar(carpetaDatos, false, pausa); }
    catch (const winrt::hresult_error&) { return false; }
}

bool GuardarSinZonas(const std::wstring& carpetaDatos)
{
    try { return Modificar(carpetaDatos, true, false); }
    catch (const winrt::hresult_error&) { return false; }
}
