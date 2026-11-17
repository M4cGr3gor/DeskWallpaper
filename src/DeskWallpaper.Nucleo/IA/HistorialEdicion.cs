namespace DeskWallpaper.Nucleo;

public enum TipoPaso { Borrado, Ampliado, Limpiado }

/// <summary>Un paso de la edición y la imagen que deja. <see cref="Factor"/>: 1,5, 2, 3 o 4 al ampliar; 1 al borrar.</summary>
public sealed record PasoEdicion(TipoPaso Tipo, ImagenRgba Imagen, double Factor = 1);

/// <summary>
/// Deshacer y rehacer de la ventana de edición. Las imágenes ocupan mucho
/// (una panorámica de 7680×2160 son 66 MB), así que hay un tope de memoria: al pasarlo se descartan los
/// pasos más antiguos, aunque siempre queda el último. El original se conserva siempre para la cortina.
/// </summary>
public sealed class HistorialEdicion(ImagenRgba original, long limiteBytes = 1L << 30)
{
    private readonly List<PasoEdicion> _pasos = [];
    private int _aplicados;          // pasos de _pasos aplicados (0 = la base)
    private int _guardado;           // valor de _aplicados al guardar; -1 si ese estado ya no se puede alcanzar
    private ImagenRgba _base = original;
    private bool _baseConRetoque;
    private double _baseFactor = 1;

    public ImagenRgba Original { get; } = original;

    public ImagenRgba Actual => _aplicados == 0 ? _base : _pasos[_aplicados - 1].Imagen;

    public bool PuedeDeshacer => _aplicados > 0;
    public bool PuedeRehacer => _aplicados < _pasos.Count;
    public bool HayCambiosSinGuardar => _aplicados != _guardado;

    /// <summary>Si la imagen actual tiene algún borrado o limpieza (para el nombre «(editada)»).</summary>
    public bool HayRetoque => _baseConRetoque || _pasos.Take(_aplicados).Any(p => p.Tipo != TipoPaso.Ampliado);

    /// <summary>Cuánto se ha ampliado la imagen actual respecto al original.</summary>
    public double FactorTotal => _pasos.Take(_aplicados).Aggregate(_baseFactor, (f, p) => f * p.Factor);

    /// <summary>Aplica un paso nuevo; lo que se podía rehacer se pierde.</summary>
    public void Anadir(PasoEdicion paso)
    {
        if (_aplicados < _pasos.Count)
        {
            _pasos.RemoveRange(_aplicados, _pasos.Count - _aplicados);
            if (_guardado > _aplicados) _guardado = -1;
        }
        _pasos.Add(paso);
        _aplicados++;
        Recortar();
    }

    public void Deshacer()
    {
        if (PuedeDeshacer) _aplicados--;
    }

    public void Rehacer()
    {
        if (PuedeRehacer) _aplicados++;
    }

    public void MarcarGuardado() => _guardado = _aplicados;

    private void Recortar()
    {
        long Ocupado() => _pasos.Sum(p => p.Imagen.Bytes) + (ReferenceEquals(_base, Original) ? 0 : _base.Bytes);

        while (_pasos.Count > 1 && _aplicados > 1 && Ocupado() > limiteBytes)
        {
            var primero = _pasos[0];
            _base = primero.Imagen;
            _baseConRetoque |= primero.Tipo != TipoPaso.Ampliado;
            _baseFactor *= primero.Factor;
            _pasos.RemoveAt(0);
            _aplicados--;
            _guardado = _guardado > 0 ? _guardado - 1 : -1;
        }
    }
}
