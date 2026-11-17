using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Asa para cambiar el ancho de un panel lateral. Es el hueco que ya separa los
/// paneles: al pasar por encima sale el cursor ↔ y una línea; se arrastra para cambiar el ancho de
/// <see cref="Columna"/> y con doble clic vuelve al de siempre.
/// </summary>
public sealed partial class Divisor : Grid
{
    private readonly Border _linea = new()
    {
        Width = 2,
        CornerRadius = new CornerRadius(1),
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 8, 0, 8),
        Opacity = 0,
    };
    private bool _arrastrando;
    private double _inicioX;
    private double _anchoInicial;

    /// <summary>La columna cuyo ancho se cambia.</summary>
    public ColumnDefinition? Columna { get; set; }

    /// <summary>True si el panel está a la derecha del asa: arrastrar a la izquierda lo ensancha.</summary>
    public bool PanelALaDerecha { get; set; }

    public double Minimo { get; set; } = 120;
    public double Maximo { get; set; } = 600;
    public double PorDefecto { get; set; } = 200;

    /// <summary>Al soltar o con doble clic: el ancho que ha quedado (para guardarlo).</summary>
    public event EventHandler<double>? AnchoCambiado;

    public Divisor()
    {
        Width = 16;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);   // si no, no recibe el ratón
        _linea.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        Children.Add(_linea);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        ToolTipService.SetToolTip(this, "Arrastra para cambiar el ancho; doble clic para volver al de siempre");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "Cambiar el ancho del panel");

        PointerEntered += (_, _) => _linea.Opacity = 0.6;
        PointerExited += (_, _) => { if (!_arrastrando) _linea.Opacity = 0; };
        PointerPressed += AlPulsar;
        PointerMoved += AlMover;
        PointerReleased += (_, e) => Terminar(e.Pointer);
        PointerCaptureLost += (_, _) => Terminar(null);
        DoubleTapped += (_, _) =>
        {
            if (Columna is null) return;
            Columna.Width = new GridLength(PorDefecto);
            AnchoCambiado?.Invoke(this, PorDefecto);
        };
    }

    /// <summary>Pone un ancho guardado (0 = el de siempre), dentro de los límites.</summary>
    public void Poner(double ancho)
    {
        if (Columna is not null) Columna.Width = new GridLength(ancho > 0 ? Math.Clamp(ancho, Minimo, Maximo) : PorDefecto);
    }

    private void AlPulsar(object sender, PointerRoutedEventArgs e)
    {
        if (Columna is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _arrastrando = CapturePointer(e.Pointer);
        _inicioX = e.GetCurrentPoint(null).Position.X;
        _anchoInicial = Columna.ActualWidth;
        _linea.Opacity = 1;
        e.Handled = true;
    }

    private void AlMover(object sender, PointerRoutedEventArgs e)
    {
        if (!_arrastrando || Columna is null) return;
        var dx = e.GetCurrentPoint(null).Position.X - _inicioX;
        Columna.Width = new GridLength(Math.Clamp(_anchoInicial + (PanelALaDerecha ? -dx : dx), Minimo, Maximo));
        e.Handled = true;
    }

    private void Terminar(Pointer? puntero)
    {
        if (!_arrastrando) return;
        _arrastrando = false;
        if (puntero is not null) ReleasePointerCapture(puntero);
        _linea.Opacity = 0;
        if (Columna is not null) AnchoCambiado?.Invoke(this, Columna.Width.Value);
    }
}
