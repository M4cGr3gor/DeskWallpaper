using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Una línea al pie de la ventana con el último mensaje (aplicado, error, aviso…). Ocupa poco: el
/// texto largo se corta y se ve entero en el tooltip.
/// </summary>
public sealed class BarraEstado : UserControl
{
    private readonly FontIcon _icono = new() { FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _texto = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
    };
    private readonly Grid _raiz;
    private readonly HyperlinkButton _accion = new() { Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private Action? _alPulsar;

    public BarraEstado()
    {
        var cerrar = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 10 },
            Padding = new Thickness(6),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cerrar, "Cerrar mensaje");
        cerrar.Click += (_, _) => Ocultar();

        _raiz = new Grid { ColumnSpacing = 8, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        _raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // El texto se ajusta a lo que ocupa (hasta el ancho disponible) y la acción va justo detrás.
        _raiz.ColumnDefinitions[1].Width = GridLength.Auto;
        _texto.MaxWidth = 900;
        Grid.SetColumn(_texto, 1);
        Grid.SetColumn(_accion, 2);
        Grid.SetColumn(cerrar, 3);
        _accion.Click += (_, _) => _alPulsar?.Invoke();
        _raiz.Children.Add(_icono);
        _raiz.Children.Add(_texto);
        _raiz.Children.Add(_accion);
        _raiz.Children.Add(cerrar);
        Content = _raiz;
    }

    /// <summary>Texto completo del mensaje visible ("" si no hay).</summary>
    public string Mensaje { get; private set; } = "";

    /// <param name="accion">Enlace opcional junto al mensaje (p. ej. "Abrir carpeta").</param>
    public void Mostrar(InfoBarSeverity gravedad, string titulo, string mensaje, (string Texto, Action Accion)? accion = null)
    {
        _alPulsar = accion?.Accion;
        _accion.Content = accion?.Texto;
        _accion.Visibility = accion is null ? Visibility.Collapsed : Visibility.Visible;
        var (glifo, color) = gravedad switch
        {
            InfoBarSeverity.Success => ("", "SystemFillColorSuccessBrush"),
            InfoBarSeverity.Warning => ("", "SystemFillColorCautionBrush"),
            InfoBarSeverity.Error => ("", "SystemFillColorCriticalBrush"),
            _ => ("", "SystemFillColorAttentionBrush"),
        };
        _icono.Glyph = glifo;
        _icono.Foreground = (Brush)Application.Current.Resources[color];
        Mensaje = string.IsNullOrWhiteSpace(mensaje) ? titulo : $"{titulo} {mensaje}";
        _texto.Inlines.Clear();
        _texto.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = titulo, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(mensaje))
            _texto.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "  " + mensaje.ReplaceLineEndings(" · ") });
        ToolTipService.SetToolTip(_texto, Mensaje);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_texto, Mensaje);
        _raiz.Visibility = Visibility.Visible;
    }

    /// <summary>Añade una nota al mensaje visible (p. ej. que no se pudo guardar en el historial).</summary>
    public void Anadir(string nota)
    {
        if (_raiz.Visibility != Visibility.Visible) return;
        Mensaje = $"{Mensaje} {nota}";
        _texto.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " + nota });
        ToolTipService.SetToolTip(_texto, Mensaje);
    }

    public void Ocultar()
    {
        Mensaje = "";
        _raiz.Visibility = Visibility.Collapsed;
    }
}
