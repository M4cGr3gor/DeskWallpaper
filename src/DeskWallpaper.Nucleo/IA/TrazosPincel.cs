namespace DeskWallpaper.Nucleo;

/// <summary>Un tramo de un trazo del pincel, en píxeles de la imagen. Un punto tiene origen igual a destino.</summary>
public readonly record struct SegmentoTrazo(double X0, double Y0, double X1, double Y1, double Radio);

/// <summary>
/// Trazos del pincel todavía sin aplicar (aún no se ha pulsado «Borrar»). Se pueden quitar de uno en uno
/// (Ctrl+Z) o todos a la vez, y la máscara se rehace con los que quedan.
/// </summary>
public sealed class TrazosPincel(int ancho, int alto)
{
    private readonly List<List<SegmentoTrazo>> _trazos = [];
    private List<SegmentoTrazo>? _actual;
    private double _ultimoX, _ultimoY, _radio;

    public Mascara Mascara { get; } = new(ancho, alto);

    /// <summary>Trazos terminados y el que se está pintando.</summary>
    public int Cantidad => _trazos.Count;

    public IReadOnlyList<IReadOnlyList<SegmentoTrazo>> Trazos => _trazos;

    /// <summary>Empieza un trazo con un punto (al pulsar el ratón).</summary>
    public void Empezar(double x, double y, double radio)
    {
        _actual = [];
        _trazos.Add(_actual);
        _radio = radio;
        Anadir(new SegmentoTrazo(x, y, x, y, radio));
        (_ultimoX, _ultimoY) = (x, y);
    }

    /// <summary>Sigue el trazo hasta (x, y) (al mover el ratón con el botón pulsado).</summary>
    public void Seguir(double x, double y)
    {
        if (_actual is null) return;
        Anadir(new SegmentoTrazo(_ultimoX, _ultimoY, x, y, _radio));
        (_ultimoX, _ultimoY) = (x, y);
    }

    public void Terminar() => _actual = null;

    /// <summary>Quita el último trazo. False si no había ninguno.</summary>
    public bool QuitarUltimo()
    {
        if (_trazos.Count == 0) return false;
        _trazos.RemoveAt(_trazos.Count - 1);
        _actual = null;
        Mascara.Limpiar();
        foreach (var s in _trazos.SelectMany(t => t)) Mascara.PintarTrazo(s.X0, s.Y0, s.X1, s.Y1, s.Radio);
        return true;
    }

    public void Limpiar()
    {
        _trazos.Clear();
        _actual = null;
        Mascara.Limpiar();
    }

    private void Anadir(SegmentoTrazo s)
    {
        _actual!.Add(s);
        Mascara.PintarTrazo(s.X0, s.Y0, s.X1, s.Y1, s.Radio);
    }
}
