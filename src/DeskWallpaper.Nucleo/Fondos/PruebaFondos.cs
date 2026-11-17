namespace DeskWallpaper.Nucleo;

/// <summary>
/// Fondo de cada monitor (ruta vacía = sin imagen, se ve el color), posición y color de fondo,
/// tal como estaban en un momento dado.
/// </summary>
public sealed record EstadoFondos(PosicionFondo Posicion, IReadOnlyDictionary<string, string> Fondos, uint Color);

/// <summary>
/// "Probar 15 s": aplica de verdad recordando lo que había, y luego se mantiene o se vuelve atrás.
/// </summary>
public sealed class PruebaFondos(IServicioFondos servicio, AplicadorFondos aplicador, ServicioAnimado? animado = null)
{
    /// <summary>Estado al que volver; null si no hay prueba en curso.</summary>
    public EstadoFondos? Anterior { get; private set; }

    /// <summary>Estado del fondo animado al que volver; null si no hay prueba en curso o no se usa.</summary>
    public EstadoAnimado? AnteriorAnimado { get; private set; }

    /// <summary>
    /// Guarda el estado actual y aplica. Los archivos del estado anterior se protegen de la limpieza
    /// de la caché: si fueran nuestros y se borrasen, "Volver" dejaría los monitores en negro.
    /// </summary>
    /// <param name="animadoNuevo">El fondo animado a poner durante la prueba (sin zonas: ninguno); null si no se toca.</param>
    public ResultadoAplicacion Empezar(Combinacion c, Disposicion d, LienzoFisico l, ConfigAnimado? animadoNuevo = null)
    {
        var estado = new EstadoFondos(servicio.ObtenerPosicion(),
            servicio.MonitorIds().ToDictionary(id => id, id => servicio.ObtenerFondo(id) ?? "", StringComparer.OrdinalIgnoreCase),
            servicio.ObtenerColorFondo());
        // Se apunta antes de aplicar: si algo lanzara con los fondos ya cambiados, todavía se puede volver.
        Anterior = estado;
        AnteriorAnimado = animado?.Capturar();
        var r = aplicador.Aplicar(c, d, l, rutasProtegidas: estado.Fondos.Values.Where(v => v.Length > 0).ToList());
        if (!r.Exito)
        {
            Anterior = null;
            AnteriorAnimado = null;
            return r;
        }
        // Con los fondos de reserva ya puestos; si esto lanza, Anterior sigue apuntado y se puede volver.
        if (animadoNuevo is not null) animado?.Activar(animadoNuevo);
        return r;
    }

    /// <summary>Se queda con lo nuevo.</summary>
    public void Mantener()
    {
        Anterior = null;
        AnteriorAnimado = null;
    }

    /// <summary>
    /// Restaura posición, color y fondos anteriores. Si ningún monitor tenía imagen (modo "color
    /// sólido"), lo vuelve a poner. Un monitor suelto sin imagen junto a otros con imagen no es un
    /// estado que Windows mantenga: se restaura el resto y se devuelve false para avisar.
    /// True si, al releerlo, todo coincide.
    /// </summary>
    public bool Volver()
    {
        if (Anterior is not { } e) return false;
        Anterior = null;
        var bien = VolverFondos(e);
        if (AnteriorAnimado is { } a)
        {
            AnteriorAnimado = null;
            try { animado!.Restaurar(a); }
            catch (Exception) { bien = false; }
        }
        return bien;
    }

    private bool VolverFondos(EstadoFondos e)
    {
        try
        {
            servicio.PonerColorFondo(e.Color);
            servicio.PonerPosicion(e.Posicion);
            var colorSolido = e.Fondos.Count > 0 && e.Fondos.Values.All(f => f.Length == 0);
            if (colorSolido)
                servicio.QuitarImagenes();
            else
                foreach (var (id, ruta) in e.Fondos.Where(f => f.Value.Length > 0))
                    servicio.PonerFondo(id, ruta);

            var todoRestaurable = colorSolido || e.Fondos.Values.All(f => f.Length > 0);
            return todoRestaurable
                && servicio.ObtenerPosicion() == e.Posicion
                && servicio.ObtenerColorFondo() == e.Color
                && e.Fondos.All(f => string.Equals(servicio.ObtenerFondo(f.Key) ?? "", f.Value, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
