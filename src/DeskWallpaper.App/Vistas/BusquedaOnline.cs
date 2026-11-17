using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Vista «Buscar online»: una búsqueda con casillas de resolución, y debajo la pestaña
/// «Resultados» (fuentes con API) y una pestaña por fuente web. Ocupa todo el centro de la ventana.
/// </summary>
public sealed class BusquedaOnline : UserControl
{
    private readonly TextBox _texto = new() { PlaceholderText = "¿Qué fondo buscas? (por ejemplo, montañas nevadas)", Width = 380 };
    private readonly StackPanel _casillas = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TabView _pestanas = new()
    {
        IsAddTabButtonVisible = false,
        TabWidthMode = TabViewWidthMode.SizeToContent,
        CloseButtonOverlayMode = TabViewCloseButtonOverlayMode.Auto,
        // Sin esto el contenido de cada pestaña solo recibe el alto que pide: el navegador se quedaba en 0 px.
        VerticalAlignment = VerticalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        // Sin el globo automático de WinUI con el atajo de cerrar pestaña ("Ctrl+F4").
        KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden,
    };
    private readonly TabViewItem _pestanaResultados;
    private IReadOnlyList<(string Etiqueta, SizePx Tamano)> _resoluciones = [];

    public ResultadosOnline Resultados { get; } = new();

    /// <summary>Se pulsa «Buscar» (o Intro): el texto y las resoluciones marcadas.</summary>
    public event EventHandler<(string Texto, IReadOnlyList<SizePx> Resoluciones)>? Buscar;

    /// <summary>Cambian el texto buscado o las casillas: hay que recordarlo.</summary>
    public event EventHandler<EstadoBusqueda>? EstadoCambiado;

    /// <summary>Contenido de la pestaña elegida (para crear el navegador de una pestaña web solo cuando se abre).</summary>
    public event EventHandler<UIElement?>? PestanaCambiada;

    public BusquedaOnline()
    {
        var buscar = new Button { Content = "Buscar", Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 100 };
        buscar.Click += (_, _) => Lanzar();
        _texto.KeyDown += (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            Lanzar();
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_texto, "Texto de la búsqueda online");

        _pestanaResultados = new TabViewItem
        {
            Header = Cabecera(Symbol.ViewAll, "Resultados"),
            IsClosable = false,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new Border { Padding = new Thickness(4, 8, 4, 0), Child = Resultados },
        };
        _pestanas.TabItems.Add(_pestanaResultados);
        _pestanas.SelectionChanged += (_, _) =>
            PestanaCambiada?.Invoke(this, (_pestanas.SelectedItem as TabViewItem)?.Content as UIElement);

        var barra = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                _texto,
                buscar,
                new TextBlock { Text = "Resoluciones:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) },
                _casillas,
            },
        };
        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_pestanas, 1);
        raiz.Children.Add(barra);
        raiz.Children.Add(_pestanas);
        Content = raiz;
    }

    /// <summary>Texto de la búsqueda actual.</summary>
    public string Texto => _texto.Text.Trim();

    /// <summary>Resoluciones marcadas ahora, con su etiqueta (para las pestañas web).</summary>
    public IReadOnlyList<(string Etiqueta, SizePx Tamano)> Marcadas =>
        [.. _resoluciones.Where((_, i) => _casillas.Children[i] is CheckBox { IsChecked: true })];

    /// <summary>Pone las casillas de las zonas actuales (marcadas salvo las que el usuario desmarcó) y el último texto.</summary>
    public void Configurar(IReadOnlyList<(string Etiqueta, SizePx Tamano)> resoluciones, EstadoBusqueda estado)
    {
        _resoluciones = resoluciones;
        if (_texto.Text.Length == 0) _texto.Text = estado.Texto;
        var desmarcadas = estado.Desmarcadas ?? [];
        _casillas.Children.Clear();
        foreach (var (etiqueta, tamano) in resoluciones)
        {
            var casilla = new CheckBox { Content = etiqueta, IsChecked = !desmarcadas.Contains(Clave(tamano)), MinWidth = 0 };
            casilla.Checked += (_, _) => Avisar();
            casilla.Unchecked += (_, _) => Avisar();
            _casillas.Children.Add(casilla);
        }
    }

    /// <summary>Sustituye las pestañas web (las de las fuentes web activas), detrás de «Resultados».</summary>
    public void PonerPestanasWeb(IEnumerable<(string Titulo, UIElement Contenido)> pestanas)
    {
        while (_pestanas.TabItems.Count > 1) _pestanas.TabItems.RemoveAt(1);
        foreach (var (titulo, contenido) in pestanas)
            _pestanas.TabItems.Add(new TabViewItem
            {
                Header = Cabecera(Symbol.Globe, titulo),
                IsClosable = false,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = contenido,
            });
        _pestanas.SelectedItem = _pestanaResultados;
    }

    public static string Clave(SizePx t) => $"{t.Ancho}x{t.Alto}";

    /// <summary>
    /// Icono y texto centrados en vertical (con IconSource, TabViewItem dejaba el texto más alto que el icono).
    /// </summary>
    private static StackPanel Cabecera(Symbol icono, string texto) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 8,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new SymbolIcon(icono) { VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5), RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform { ScaleX = 0.8, ScaleY = 0.8 } },
            new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center },
        },
    };

    private EstadoBusqueda Estado() =>
        new(Texto, [.. _resoluciones.Where((_, i) => _casillas.Children[i] is CheckBox { IsChecked: false }).Select(r => Clave(r.Tamano))]);

    private void Avisar() => EstadoCambiado?.Invoke(this, Estado());

    private void Lanzar()
    {
        if (Texto.Length == 0)
        {
            Resultados.Avisar("Escribe qué buscas y pulsa Buscar.");
            return;
        }
        Avisar();
        Buscar?.Invoke(this, (Texto, [.. Marcadas.Select(m => m.Tamano)]));
    }
}
