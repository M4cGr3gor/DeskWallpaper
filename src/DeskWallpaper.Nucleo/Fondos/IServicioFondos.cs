namespace DeskWallpaper.Nucleo;

/// <summary>Valores de DESKTOP_WALLPAPER_POSITION.</summary>
public enum PosicionFondo
{
    Centrar = 0,
    Mosaico = 1,
    Ajustar = 2,
    Encajar = 3,
    Rellenar = 4,
    Extender = 5,
}

/// <summary>Acceso al fondo de escritorio de cada monitor. La implementación real usa IDesktopWallpaper.</summary>
public interface IServicioFondos
{
    IReadOnlyList<string> MonitorIds();

    /// <summary>Ruta del fondo actual del monitor, o null/vacío si no hay.</summary>
    string? ObtenerFondo(string monitorId);

    /// <summary>
    /// Pone la imagen del monitor. Una ruta vacía NO deja el monitor sin imagen: Windows le vuelve a
    /// poner su imagen general a los pocos segundos. Para eso está
    /// <see cref="QuitarImagenes"/>.
    /// </summary>
    void PonerFondo(string monitorId, string ruta);

    /// <summary>Modo "color sólido" de Windows: quita la imagen de todos los monitores a la vez.</summary>
    void QuitarImagenes();

    PosicionFondo ObtenerPosicion();

    void PonerPosicion(PosicionFondo posicion);

    /// <summary>Color (COLORREF) que Windows muestra en los monitores sin imagen.</summary>
    uint ObtenerColorFondo();

    void PonerColorFondo(uint color);
}
