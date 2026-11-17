using System.Security.Cryptography;
using System.Text;

namespace DeskWallpaper.Nucleo;

/// <summary>El proceso del ayudante: arrancarlo, pedirle que recargue <c>animado.json</c> o que se cierre.</summary>
public interface IControlAyudante
{
    bool EnMarcha();
    void Arrancar();
    void Recargar();
    void Cerrar();
}

/// <summary>La entrada del ayudante en el arranque de Windows (HKCU\…\Run, valor «DeskWallpaper.Animado»).</summary>
public interface IRegistroArranque
{
    string? Leer();
    void Poner(string comando);
    void Quitar();
}

/// <summary>Lo que había antes de una prueba de 15 s, para volver a ello.</summary>
public sealed record EstadoAnimado(ConfigAnimado? Config, bool EnMarcha);

/// <summary>
/// Activa o quita el fondo animado. Solo la carpeta de datos por defecto
/// toca el arranque con Windows: un ayudante de pruebas (<c>--data</c>) nunca pisa el del usuario (6.8).
/// </summary>
public sealed class ServicioAnimado(string carpetaDatos, bool carpetaPorDefecto, string rutaAyudante,
                                    IControlAyudante control, IRegistroArranque arranque)
{
    /// <summary>
    /// Identifica la carpeta de datos en el nombre de la ventana de control y del mutex del ayudante: 8 hexadecimales del
    /// SHA-256 de la ruta completa, sin barra final y en minúsculas.
    /// </summary>
    public static string Id(string carpetaDatos)
    {
        var normal = Path.GetFullPath(carpetaDatos).TrimEnd('\\').ToLowerInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normal)))[..8];
    }

    public string ComandoArranque => $"\"{rutaAyudante}\" --datos \"{carpetaDatos}\" --id {Id(carpetaDatos)}";

    public EstadoAnimado Capturar() => new(AlmacenAnimado.Leer(carpetaDatos), control.EnMarcha());

    /// <summary>Escribe la configuración entera (aplicar sustituye todo el fondo animado) y pone el ayudante a juego.</summary>
    public void Activar(ConfigAnimado c)
    {
        AlmacenAnimado.Guardar(carpetaDatos, c);
        if (c.Zonas.Count > 0)
        {
            if (carpetaPorDefecto) arranque.Poner(ComandoArranque);
            if (control.EnMarcha()) control.Recargar();
            else control.Arrancar();
        }
        else
        {
            control.Cerrar();
            if (carpetaPorDefecto) arranque.Quitar();
        }
    }

    /// <summary>Vuelve a lo capturado antes de una prueba.</summary>
    public void Restaurar(EstadoAnimado e)
    {
        if (e.Config is { } c)
        {
            Activar(c);
            return;
        }
        AlmacenAnimado.Borrar(carpetaDatos);
        control.Cerrar();
        if (carpetaPorDefecto) arranque.Quitar();
    }

    /// <summary>Al abrir la app: si hay un vídeo aplicado y el arranque apunta a otra ruta (carpeta movida), se corrige.</summary>
    public void CorregirArranque()
    {
        if (!carpetaPorDefecto) return;
        if (AlmacenAnimado.Leer(carpetaDatos) is not { Zonas.Count: > 0 }) return;
        if (arranque.Leer() != ComandoArranque) arranque.Poner(ComandoArranque);
    }
}
