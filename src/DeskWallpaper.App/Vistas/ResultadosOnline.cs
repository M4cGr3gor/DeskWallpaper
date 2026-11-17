using System.Collections.ObjectModel;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace DeskWallpaper.App.Vistas;

/// <summary>Un resultado tal como se pinta en la cuadrícula.</summary>
public sealed class ElementoResultado(ResultadoEncajado r, string? para)
{
    public ResultadoEncajado Resultado { get; } = r;
    public string MiniaturaUrl => Resultado.Resultado.Miniatura.AbsoluteUri;

    /// <summary>
    /// "5120×1440 · mayor · para 2+1 · Wallhaven": resolución, encaje, casilla y origen. Un vídeo empieza por su
    /// duración ("▶ 0:15 · 3840×2160 · recorta · para 2+1 · Pexels (vídeos)").
    /// </summary>
    public string Pie
    {
        get
        {
            var r = Resultado.Resultado;
            var t = r.Tamano;
            var video = r.Duracion is { } d ? $"▶ {(int)d.TotalMinutes}:{d.Seconds:00} · " : r.Archivos is not null ? "▶ " : "";
            var encaje = Resultado.Encaje switch
            {
                Encaje.Exacta => " · exacta",
                Encaje.Mayor => " · mayor",
                Encaje.Recorta => " · recorta",
                _ => "",
            };
            var casilla = para is null ? "" : $" · para {para}";
            return $"{video}{t.Ancho}×{t.Alto}{encaje}{casilla} · {r.Fuente}";
        }
    }
}

/// <summary>
/// Pestaña «Resultados»: miniaturas de todas las fuentes con API, juntas. Pide más
/// páginas al acercarse al final; doble clic para verla; se pueden marcar varias y descargarlas a la vez.
/// </summary>
public sealed class ResultadosOnline : UserControl
{
    private readonly ObservableCollection<ElementoResultado> _elementos = [];
    private readonly GridView _vista;
    private readonly TextBlock _aviso = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        Text = "Escribe qué buscas y pulsa Buscar.",
    };
    private readonly TextBlock _cuenta = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _descargar = new() { Content = "Descargar seleccionadas", IsEnabled = false };
    private bool _hayMas;
    private bool _pidiendo;
    private int _anadidasEnTanda;
    private int _tandasVaciasSeguidas;
    private ScrollViewer? _desplazador;
    private IReadOnlyDictionary<SizePx, string> _casillas = new Dictionary<SizePx, string>();

    /// <summary>Tras tantas tandas seguidas sin nada que encaje, se deja de pedir más solo.</summary>
    private const int MaximoTandasVacias = 3;

    public event EventHandler? PedirMas;
    public event EventHandler<ResultadoEncajado>? Ver;
    public event EventHandler<IReadOnlyList<ResultadoEncajado>>? Descargar;

    public ResultadosOnline()
    {
        _vista = new GridView
        {
            ItemsSource = _elementos,
            SelectionMode = ListViewSelectionMode.Extended,
            IsItemClickEnabled = false,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <Grid Width="300" RowSpacing="4" Margin="2">
                    <Grid.RowDefinitions><RowDefinition Height="120" /><RowDefinition Height="Auto" /></Grid.RowDefinitions>
                    <Border CornerRadius="4" Background="{ThemeResource CardBackgroundFillColorSecondaryBrush}">
                      <Image Source="{Binding MiniaturaUrl}" Stretch="UniformToFill" />
                    </Border>
                    <TextBlock Grid.Row="1" Text="{Binding Pie}" FontSize="12" TextTrimming="CharacterEllipsis"
                               Foreground="{ThemeResource TextFillColorSecondaryBrush}" />
                  </Grid>
                </DataTemplate>
                """),
        };
        _vista.SelectionChanged += (_, _) =>
        {
            var n = _vista.SelectedItems.Count;
            _descargar.IsEnabled = n > 0;
            _descargar.Content = n > 1 ? $"Descargar seleccionadas ({n})" : "Descargar seleccionadas";
        };
        _vista.DoubleTapped += (_, e) =>
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is ElementoResultado el) Ver?.Invoke(this, el.Resultado);
        };
        _vista.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue || args.Item is not ElementoResultado el) return;
            args.ItemContainer.ContextFlyout = Menu(el);
        };
        _vista.Loaded += (_, _) =>
        {
            if (_desplazador is null && Buscar<ScrollViewer>(_vista) is { } sv)
            {
                _desplazador = sv;
                sv.ViewChanged += (_, _) => ComprobarFinal();
            }
        };
        // Si la primera tanda no llena la vista no hay barra de desplazamiento: también se comprueba al cambiar de tamaño.
        _vista.SizeChanged += (_, _) => ComprobarFinal();
        _descargar.Click += (_, _) => Descargar?.Invoke(this, Seleccion());

        var cabecera = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 8) };
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cabecera.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_descargar, 1);
        cabecera.Children.Add(_cuenta);
        cabecera.Children.Add(_descargar);

        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_vista, 1);
        Grid.SetRow(_aviso, 1);
        raiz.Children.Add(cabecera);
        raiz.Children.Add(_vista);
        raiz.Children.Add(_aviso);
        Content = raiz;
    }

    /// <summary>Empieza una búsqueda nueva: vacía la lista.</summary>
    /// <param name="casillas">Nombre corto de cada casilla de resolución ("2+1"), para el pie de cada miniatura.</param>
    public void Empezar(IReadOnlyDictionary<SizePx, string> casillas)
    {
        _casillas = casillas;
        _tandasVaciasSeguidas = 0;
        _anadidasEnTanda = 0;
        _elementos.Clear();
        _hayMas = false;
        _pidiendo = true;
        Avisar("Buscando…");
    }

    /// <summary>Añade los resultados que acaban de llegar (de cualquier fuente).</summary>
    public void Anadir(IReadOnlyList<ResultadoEncajado> resultados)
    {
        foreach (var r in resultados)
            _elementos.Add(new ElementoResultado(r, r.Pedida is { } p && _casillas.TryGetValue(p, out var c) ? c : null));
        _anadidasEnTanda += resultados.Count;
        _cuenta.Text = _elementos.Count == 1 ? "1 resultado" : $"{_elementos.Count} resultados";
        if (_elementos.Count > 0) _aviso.Visibility = Visibility.Collapsed;
    }

    /// <summary>Termina una tanda (la primera o "cargar más").</summary>
    public void Fin(bool hayMas)
    {
        _hayMas = hayMas;
        _pidiendo = false;
        _tandasVaciasSeguidas = _anadidasEnTanda == 0 ? _tandasVaciasSeguidas + 1 : 0;
        _anadidasEnTanda = 0;
        _cuenta.Text = _elementos.Count == 1 ? "1 resultado" : _elementos.Count > 0 ? $"{_elementos.Count} resultados" : "";
        if (_elementos.Count == 0 && !hayMas) Avisar("Sin resultados. Prueba con otras palabras o desmarca alguna resolución.");
        // Si lo que ha llegado no llena la vista (o no encajaba nada) se pide la siguiente página sin esperar al desplazamiento.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ComprobarFinal);
    }

    /// <summary>Mensaje en el centro mientras no hay resultados.</summary>
    public void Avisar(string texto)
    {
        _aviso.Text = texto;
        _aviso.Visibility = _elementos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_elementos.Count == 0) _cuenta.Text = "";
    }

    private IReadOnlyList<ResultadoEncajado> Seleccion() =>
        [.. _vista.SelectedItems.OfType<ElementoResultado>().Select(e => e.Resultado)];

    /// <summary>
    /// Al pasar del 80 % del desplazamiento, o si no hay nada que desplazar, se piden más (una vez por tanda y
    /// como mucho <see cref="MaximoTandasVacias"/> tandas seguidas sin resultados).
    /// </summary>
    private void ComprobarFinal()
    {
        if (!_hayMas || _pidiendo || _tandasVaciasSeguidas >= MaximoTandasVacias) return;
        if (_desplazador is { } sv && sv.ScrollableHeight > 0 && sv.VerticalOffset < sv.ScrollableHeight * 0.8) return;
        _pidiendo = true;
        _cuenta.Text = $"{_elementos.Count} resultados · cargando más…";
        PedirMas?.Invoke(this, EventArgs.Empty);
    }

    private MenuFlyout Menu(ElementoResultado el)
    {
        MenuFlyoutItem Opcion(string texto, string glifo, Action accion)
        {
            var o = new MenuFlyoutItem { Text = texto, Icon = new FontIcon { Glyph = glifo } };
            o.Click += (_, _) => accion();
            return o;
        }
        var menu = new MenuFlyout();
        menu.Items.Add(Opcion("Ver a pantalla completa", "", () => Ver?.Invoke(this, el.Resultado)));
        menu.Items.Add(Opcion("Descargar", "", () =>
        {
            var seleccion = Seleccion();
            Descargar?.Invoke(this, seleccion.Contains(el.Resultado) ? seleccion : [el.Resultado]);
        }));
        if (el.Resultado.Resultado.Ficha is { } ficha)
            menu.Items.Add(Opcion("Abrir la página de origen", "", () =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ficha.AbsoluteUri) { UseShellExecute = true })?.Dispose()));
        return menu;
    }

    private static T? Buscar<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) return t;
            if (Buscar<T>(hijo) is { } encontrado) return encontrado;
        }
        return null;
    }
}
