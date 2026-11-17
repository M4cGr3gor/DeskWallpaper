using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Pestaña de una fuente web: la página tal cual en un navegador integrado, abierta con
/// la búsqueda y el tamaño elegidos. Nunca se lee su contenido; solo se añade «Descargar con DeskWallpaper» al menú
/// del clic derecho sobre una imagen, y las descargas normales van a la carpeta de descargas.
/// </summary>
public sealed class PestanaWeb : UserControl
{
    private const string SinFiltro = "Sin filtro de tamaño";

    private readonly FuenteBusqueda _fuente;
    private readonly Func<Task<CoreWebView2Environment>> _entorno;
    private readonly Func<string> _carpetaDescargas;
    private readonly ComboBox _selector = new() { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _aviso = new()
    {
        Text = "Escribe qué buscas y pulsa Buscar.",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };
    private readonly Grid _zonaNavegador = new();
    private WebView2? _web;
    private Task? _iniciando;
    private string _texto = "";
    private IReadOnlyList<(string Etiqueta, SizePx Tamano)> _resoluciones = [];
    private bool _rehaciendoSelector;
    private readonly Button _atras = BotonNavegador("", "Atrás");
    private readonly Button _adelante = BotonNavegador("", "Adelante");
    private readonly Button _recargar = BotonNavegador("", "Recargar");

    /// <summary>Opciones del menú del navegador que no tienen sentido aquí.</summary>
    private static readonly HashSet<string> MenuSobrante = new(StringComparer.OrdinalIgnoreCase)
    {
        "saveImageAs", "saveLinkAs", "saveAs", "openLinkInNewWindow", "inspectElement", "print", "createQrCode", "share", "webCapture",
    };

    /// <summary>"Descargar con DeskWallpaper" sobre una imagen: su dirección (http, https o data).</summary>
    public event EventHandler<Uri>? DescargarImagen;

    /// <summary>Una descarga normal del navegador ha terminado en la carpeta de descargas.</summary>
    public event EventHandler<string>? DescargaTerminada;

    /// <summary>Una descarga normal del navegador no se ha podido hacer (carpeta inaccesible, corte…).</summary>
    public event EventHandler<string>? DescargaFallida;

    public PestanaWeb(FuenteBusqueda fuente, Func<Task<CoreWebView2Environment>> entorno, Func<string> carpetaDescargas)
    {
        _fuente = fuente;
        _entorno = entorno;
        _carpetaDescargas = carpetaDescargas;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_selector, $"Tamaño en {fuente.Nombre}");
        _selector.Visibility = ConTamano(fuente);
        _selector.SelectionChanged += (_, _) => { if (!_rehaciendoSelector) Navegar(); };
        _atras.Click += (_, _) => { if (_web?.CoreWebView2 is { CanGoBack: true } c) c.GoBack(); };
        _adelante.Click += (_, _) => { if (_web?.CoreWebView2 is { CanGoForward: true } c) c.GoForward(); };
        _recargar.Click += (_, _) => _web?.CoreWebView2?.Reload();

        var barra = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(4, 8, 0, 8),
            Children =
            {
                _atras,
                _adelante,
                _recargar,
                new TextBlock { Text = "Tamaño:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Visibility = ConTamano(fuente) },
                _selector,
                new TextBlock
                {
                    Text = fuente.Contenido == ContenidoFuente.Videos
                        ? "Descarga el vídeo con el botón de la página: irá a tu carpeta de descargas."
                        : "Abre la imagen en grande y haz clic derecho › «Descargar con DeskWallpaper».",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0),
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            },
        };
        _zonaNavegador.Children.Add(_aviso);
        var raiz = new Grid();
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_zonaNavegador, 1);
        raiz.Children.Add(barra);
        raiz.Children.Add(_zonaNavegador);
        Content = raiz;
    }

    public string NombreFuente => _fuente.Nombre;

    /// <summary>El selector de tamaño solo sirve si la dirección de la fuente lleva el tamaño (las webs de vídeo no).</summary>
    private static Visibility ConTamano(FuenteBusqueda f) =>
        f.PlantillaExacta.Contains("{ancho}", StringComparison.Ordinal) || f.PlantillaExacta.Contains("{alto}", StringComparison.Ordinal)
        || f.PlantillaExacta.Contains("{proporcion}", StringComparison.Ordinal)
            ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Resolución elegida ahora en el selector (null = sin filtro).</summary>
    public SizePx? Seleccionada =>
        _selector.SelectedIndex >= 0 && _selector.SelectedIndex < _resoluciones.Count ? _resoluciones[_selector.SelectedIndex].Tamano : null;

    /// <summary>Nueva búsqueda: rehace el selector con las resoluciones marcadas y abre la página con la primera.</summary>
    public void Buscar(string texto, IReadOnlyList<(string Etiqueta, SizePx Tamano)> resoluciones)
    {
        _texto = texto;
        _resoluciones = resoluciones;
        // Mientras se rehace el selector no se navega (si no, iría primero a "Sin filtro" y luego al tamaño).
        _rehaciendoSelector = true;
        _selector.Items.Clear();
        foreach (var (etiqueta, _) in resoluciones) _selector.Items.Add(etiqueta);
        _selector.Items.Add(SinFiltro);
        _selector.SelectedIndex = 0;
        _rehaciendoSelector = false;
        Navegar();
    }

    /// <summary>Cierra el navegador (sus procesos) al quitar la pestaña.</summary>
    public void Cerrar()
    {
        try { _web?.Close(); }
        catch (Exception) { /* ya estaba cerrado */ }
    }

    private static Button BotonNavegador(string glifo, string nombre)
    {
        var b = new Button { Content = new FontIcon { Glyph = glifo, FontSize = 14 }, Padding = new Thickness(8, 6, 8, 6), IsEnabled = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, nombre);
        ToolTipService.SetToolTip(b, nombre);
        return b;
    }

    /// <summary>Crea el navegador la primera vez que se abre la pestaña (las que no se abren no consumen).</summary>
    public Task ActivarAsync()
    {
        // Si falló (p. ej. WebView2 no estaba disponible en ese momento), se vuelve a intentar al abrirla otra vez.
        if (_iniciando is { IsFaulted: true })
        {
            if (_web is not null) _zonaNavegador.Children.Remove(_web);
            _web = null;
            _iniciando = null;
        }
        return _iniciando ??= IniciarAsync();
    }

    private async Task IniciarAsync()
    {
        // Oculto hasta la primera navegación: si no, su fondo blanco tapa "Escribe qué buscas…".
        _web = new WebView2 { Visibility = Visibility.Collapsed };
        _zonaNavegador.Children.Add(_web);
        var web = _web;
        await web.EnsureCoreWebView2Async(await _entorno());
        if (web.CoreWebView2 is not { } core) return;   // se ha cerrado mientras arrancaba
        core.Settings.AreDevToolsEnabled = false; // sin «Inspeccionar» ni F12
        _recargar.IsEnabled = true;
        // Lo que abriría una ventana suelta (target=_blank, «Visitar» en Google) se abre en la misma pestaña.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            core.Navigate(e.Uri);
        };
        core.HistoryChanged += (_, _) =>
        {
            _atras.IsEnabled = core.CanGoBack;
            _adelante.IsEnabled = core.CanGoForward;
        };
        core.ContextMenuRequested += (_, e) =>
        {
            // Fuera lo que no tiene sentido aquí: «Guardar imagen como» (abría el diálogo de Windows y bajaba a
            // Descargas), abrir en ventana nueva, «Inspeccionar» y los submenús como «Más herramientas».
            // Por posición: los objetos del menú se envuelven de nuevo en cada acceso y Remove(objeto) no los encuentra.
            for (var i = e.MenuItems.Count - 1; i >= 0; i--)
            {
                var item = e.MenuItems[i];
                if (MenuSobrante.Contains(item.Name) || item.Kind == CoreWebView2ContextMenuItemKind.Submenu) e.MenuItems.RemoveAt(i);
            }
            LimpiarSeparadores(e.MenuItems);
            var destino = e.ContextMenuTarget;
            if (destino.Kind != CoreWebView2ContextMenuTargetKind.Image || !destino.HasSourceUri) return;
            if (!Uri.TryCreate(destino.SourceUri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "data")) return;
            var opcion = core.Environment.CreateContextMenuItem("Descargar con DeskWallpaper", null, CoreWebView2ContextMenuItemKind.Command);
            opcion.CustomItemSelected += (_, _) => DispatcherQueue.TryEnqueue(() => DescargarImagen?.Invoke(this, uri));
            e.MenuItems.Insert(0, opcion);
            if (e.MenuItems.Count > 1 && e.MenuItems[1].Kind != CoreWebView2ContextMenuItemKind.Separator)
                e.MenuItems.Insert(1, core.Environment.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
        };
        core.DownloadStarting += (_, e) =>
        {
            // Sin el diálogo de Windows: directamente a la carpeta de descargas, sin sobrescribir. Si la
            // carpeta no se puede usar, se cancela y se avisa (nunca va a otra carpeta por su cuenta).
            e.Handled = true;
            try
            {
                var carpeta = _carpetaDescargas();
                Directory.CreateDirectory(carpeta);
                var nombre = Path.GetFileName(e.ResultFilePath);
                e.ResultFilePath = Path.Combine(carpeta, DescargadorImagenes.NombreLibre(carpeta, nombre));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                e.Cancel = true;
                DispatcherQueue.TryEnqueue(() => DescargaFallida?.Invoke(this, "No se puede escribir en la carpeta de descargas: revísala en Ajustes."));
                return;
            }
            var operacion = e.DownloadOperation;
            operacion.StateChanged += (_, _) =>
            {
                if (operacion.State == CoreWebView2DownloadState.Completed)
                    DispatcherQueue.TryEnqueue(() => DescargaTerminada?.Invoke(this, operacion.ResultFilePath));
                else if (operacion.State == CoreWebView2DownloadState.Interrupted)
                    DispatcherQueue.TryEnqueue(() => DescargaFallida?.Invoke(this, $"La descarga se ha interrumpido ({operacion.InterruptReason})."));
            };
        };
        Navegar();
    }

    /// <summary>Sin separadores al principio, al final ni dos seguidos (tras quitar opciones).</summary>
    private static void LimpiarSeparadores(IList<CoreWebView2ContextMenuItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var esSeparador = items[i].Kind == CoreWebView2ContextMenuItemKind.Separator;
            var anteriorSeparador = i == 0 || items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator;
            if (esSeparador && (anteriorSeparador || i == items.Count - 1)) items.RemoveAt(i);
        }
    }

    private void Navegar()
    {
        if (_texto.Length == 0 || _web?.CoreWebView2 is not { } core) return;
        _aviso.Visibility = Visibility.Collapsed;
        var plantilla = Seleccionada is null
            ? (string.IsNullOrWhiteSpace(_fuente.PlantillaSinResolucion) ? PlantillaUrl.SinResolucion(_fuente.PlantillaExacta) : _fuente.PlantillaSinResolucion)
            : _fuente.PlantillaExacta;
        try
        {
            var uri = PlantillaUrl.Rellenar(plantilla, _texto, Seleccionada, 1);
            if (uri.Scheme is not ("http" or "https")) throw new UriFormatException();
            _web.Visibility = Visibility.Visible;
            core.Navigate(uri.AbsoluteUri);
        }
        catch (UriFormatException)
        {
            _web.Visibility = Visibility.Collapsed;
            _aviso.Text = $"La dirección de «{_fuente.Nombre}» no es válida: revísala en Ajustes › Búsqueda online.";
            _aviso.Visibility = Visibility.Visible;
        }
    }
}
