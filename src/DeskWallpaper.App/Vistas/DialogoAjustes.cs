using System.Globalization;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Globalization.NumberFormatting;
using Windows.Storage.Pickers;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Ajustes: carpetas de la biblioteca, diagonal real de cada pantalla, marco
/// entre pantallas unidas con el patrón de alineación, y caché de miniaturas.
/// </summary>
public sealed class DialogoAjustes : ContentDialog
{
    private readonly Configuracion _cfg;
    private readonly Disposicion _detectada;
    private readonly List<(DatosMonitor Monitor, NumberBox Caja)> _diagonales = [];
    /// <summary>Una caja por borde compartido; su valor se guarda en todas las claves del grupo.</summary>
    private readonly List<(IReadOnlyList<string> Claves, NumberBox Caja)> _marcos = [];
    private readonly List<CarpetaVigilada> _carpetas;
    private readonly StackPanel _listaCarpetas = new() { Spacing = 6 };
    private readonly CacheMiniaturas _cache;

    /// <summary>Lo que ocupa la caché: se mide al abrir y al vaciar, no cada vez que cambia el tope.</summary>
    private long _bytesCache;
    private readonly IntPtr _ventana;
    private readonly TextBlock _ocupacion = new() { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) };
    private readonly NumberBox _tope;
    private static readonly CultureInfo Espanol = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Las carpetas han cambiado al guardar (hay que volver a escanear y vigilar).</summary>
    public bool CarpetasCambiadas { get; private set; }

    /// <summary>Se ha vaciado la caché (aunque luego se cancele el diálogo).</summary>
    public bool CacheVaciada { get; private set; }

    /// <summary>Las fuentes de la búsqueda online han cambiado al guardar (hay que rehacer las pestañas web).</summary>
    public bool FuentesCambiadas { get; private set; }

    private readonly EditorFuentes _editorFuentes;
    private readonly TextBlock _carpetaDescargas = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private string _rutaDescargas;

    /// <param name="detectada">Disposición tal como la da Windows, sin las correcciones del usuario.</param>
    /// <param name="corregida">Disposición con las correcciones actuales.</param>
    /// <param name="ventana">HWND de la ventana principal, para el selector de carpetas.</param>
    public DialogoAjustes(Configuracion cfg, Disposicion detectada, Disposicion corregida, IReadOnlyList<Zona> zonas,
                          CacheMiniaturas cache, IntPtr ventana, BuscadorOnline buscador, GestorModelos modelos)
    {
        _cfg = cfg;
        _rutaDescargas = cfg.CarpetaDescargas;
        _editorFuentes = new EditorFuentes(cfg.FuentesBusqueda ?? [], buscador);
        _detectada = detectada;
        _cache = cache;
        _ventana = ventana;
        _carpetas = [.. cfg.CarpetasVigiladas];
        Resources["ContentDialogMaxWidth"] = 820.0; // el ancho por defecto (548) corta los botones de cada fila
        Title = "Ajustes";
        PrimaryButtonText = "Guardar";
        CloseButtonText = "Cancelar";
        DefaultButton = ContentDialogButton.Primary;

        // Margen derecho: que la barra de desplazamiento no quede pegada a los botones.
        var panel = new StackPanel { Spacing = 10, MinWidth = 700, Padding = new Thickness(0, 0, 20, 0) };
        panel.Children.Add(Cabecera("Carpetas de la biblioteca"));
        panel.Children.Add(Nota("Se analizan al abrir el programa y mientras está abierto. Las imágenes nunca se mueven ni se modifican."));
        panel.Children.Add(_listaCarpetas);
        var anadir = new Button { Content = "Añadir carpeta…" };
        anadir.Click += async (_, _) => await AnadirCarpetaAsync();
        panel.Children.Add(anadir);
        PintarCarpetas();

        panel.Children.Add(Cabecera("Tamaño de las pantallas"));
        panel.Children.Add(Nota("Si el tamaño que informa una pantalla no es el real, corrígelo con su diagonal."));
        foreach (var m in corregida.Monitores)
        {
            var caja = Numero(TamanoPantalla.DiagonalPulgadas(m.TamanoFisico), 5, 100, 0.1, "Diagonal (pulgadas)");
            var restablecer = new Button { Content = "Restablecer", VerticalAlignment = VerticalAlignment.Bottom };
            restablecer.Click += (_, _) => caja.Value = DiagonalDetectada(m.Id);
            panel.Children.Add(Fila($"{m.Numero} · {m.Nombre} · {m.Escritorio.Ancho}×{m.Escritorio.Alto}",
                ConFlechas(caja, "Diagonal (pulgadas)"), restablecer));
            _diagonales.Add((m, caja));
        }

        var grupos = EditorZonas.GruposMarco(corregida, zonas);
        panel.Children.Add(Cabecera("Marcos entre pantallas"));
        if (grupos.Count == 0)
            panel.Children.Add(Nota("No hay pantallas unidas en una misma zona."));
        else
            panel.Children.Add(Nota("Hueco de plástico entre dos pantallas unidas, en milímetros."));

        foreach (var g in grupos)
        {
            // Un monitor que toca a varios por el mismo borde (el 3 bajo el 2 y el 1) tiene un solo marco:
            // «Entre 2 + 1 y 3». Con uno solo, como siempre: «Entre 1 y 2».
            var caja = Numero(EditorZonas.ValorDeGrupo(g, cfg.MarcosMm), 0, 100, 0.5, "Marco (mm)");
            var zona = zonas.First(z => z.MonitorIds.Contains(g.Monitor, StringComparer.OrdinalIgnoreCase));
            var comprobar = new Button { Content = "Comprobar alineación…", VerticalAlignment = VerticalAlignment.Bottom };
            comprobar.Click += (_, _) =>
            {
                // Con lo que hay escrito en el diálogo (aunque aún no se haya guardado): diagonales y otros huecos.
                var (disposicion, marcos) = Editado();
                VentanaPatronMarco.Mostrar(disposicion, zona, g.Claves, Valor(caja), marcos,
                                           mm => caja.Value = mm);
            };
            var numero = corregida.Buscar(g.Monitor)!.Numero;
            var otros = g.Otros.Select(o => corregida.Buscar(o)!.Numero).ToList();
            var texto = otros.Count > 1
                ? $"Entre {string.Join(" + ", otros)} y {numero}"
                : $"Entre {Math.Min(numero, otros[0])} y {Math.Max(numero, otros[0])}";
            panel.Children.Add(Fila(texto, ConFlechas(caja, "Marco (mm)"), comprobar));
            _marcos.Add((g.Claves, caja));
        }

        panel.Children.Add(Cabecera("Caché de miniaturas"));
        _tope = Numero(cfg.TopeMiniaturasMb, 50, 2000, 50, "Tope (MB)", decimales: 0);
        _tope.ValueChanged += (_, _) => PintarOcupacion();
        var vaciar = new Button { Content = "Vaciar caché", VerticalAlignment = VerticalAlignment.Bottom };
        panel.Children.Add(Fila(_ocupacion, ConFlechas(_tope, "Tope (MB)"), vaciar));
        var confirmar = new Button { Content = "Vaciar", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        var no = new Button { Content = "No" };
        var confirmacion = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed,
            Children = { new TextBlock { Text = "¿Vaciar? Se volverán a generar al verlas.", VerticalAlignment = VerticalAlignment.Center }, confirmar, no },
        };
        panel.Children.Add(confirmacion);
        vaciar.Click += (_, _) =>
        {
            confirmacion.Visibility = Visibility.Visible;
            vaciar.IsEnabled = false;
            // Es lo último del diálogo: que no quede por debajo de la zona visible.
            confirmacion.UpdateLayout();
            confirmacion.StartBringIntoView();
        };
        no.Click += (_, _) => { confirmacion.Visibility = Visibility.Collapsed; vaciar.IsEnabled = true; };
        confirmar.Click += async (_, _) =>
        {
            confirmacion.Visibility = Visibility.Collapsed;
            try { await Task.Run(_cache.Vaciar); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* lo que quede se recorta solo */ }
            CacheVaciada = true;
            vaciar.IsEnabled = true;
            _bytesCache = _cache.BytesOcupados();
            PintarOcupacion();
        };
        _bytesCache = _cache.BytesOcupados();
        PintarOcupacion();

        panel.Children.Add(Cabecera("Búsqueda online"));
        panel.Children.Add(Nota("Las imágenes y los vídeos descargados van a esta carpeta. No hace falta que sea una carpeta de la biblioteca."));
        var elegir = new Button { Content = "Elegir…" };
        elegir.Click += async (_, _) => await ElegirCarpetaDescargasAsync();
        var filaDescargas = new Grid { ColumnSpacing = 12 };
        filaDescargas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filaDescargas.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(elegir, 1);
        filaDescargas.Children.Add(_carpetaDescargas);
        filaDescargas.Children.Add(elegir);
        panel.Children.Add(filaDescargas);
        PintarCarpetaDescargas();
        panel.Children.Add(Nota("Fuentes: las de tipo API se juntan en «Resultados»; cada fuente web tiene su pestaña."));
        panel.Children.Add(_editorFuentes);

        panel.Children.Add(Cabecera("Modelos de IA"));
        panel.Children.Add(Nota("Para quitar marcas y ampliar imágenes. Se descargan la primera vez que se usan y funcionan sin conexión."));
        foreach (var m in ModelosIA.Todos)
        {
            var enlace = new HyperlinkButton { Content = "Página del modelo", NavigateUri = m.Pagina, Padding = new Thickness(4, 2, 4, 2) };
            var detalle = new TextBlock
            {
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap,
            };
            // Borrar los archivos del modelo: se vuelve a descargar al usarlo.
            var eliminar = new Button { Content = "Eliminar" };
            ToolTipService.SetToolTip(eliminar, $"Borrar los archivos de {m.Nombre} ({m.Megas} MB). Se vuelve a descargar al usarlo.");
            void Pintar(string? error = null)
            {
                var descargado = modelos.EstaDescargado(m);
                detalle.Text = error ?? $"{m.Megas} MB · {(descargado ? "Descargado" : "Se descarga al usarlo")} · {m.Licencia}";
                eliminar.IsEnabled = modelos.HayArchivos(m);
            }
            eliminar.Click += (_, _) =>
            {
                try { modelos.Eliminar(m); Pintar(); }
                catch (ExcepcionModelo ex) { Pintar(ex.Message); }
            };
            Pintar();
            var fila = new Grid { ColumnSpacing = 12 };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.Children.Add(new StackPanel { Children = { new TextBlock { Text = m.Nombre }, detalle } });
            Grid.SetColumn(enlace, 1);
            Grid.SetColumn(eliminar, 2);
            enlace.VerticalAlignment = VerticalAlignment.Center;
            eliminar.VerticalAlignment = VerticalAlignment.Center;
            fila.Children.Add(enlace);
            fila.Children.Add(eliminar);
            panel.Children.Add(fila);
        }
        panel.Children.Add(Nota("LaMa se entrenó con el conjunto de imágenes Places2 (CC-BY 4.0)."));

        Content = new ScrollViewer { Content = EnTarjetas(panel), MaxHeight = 560 };
        PrimaryButtonClick += (_, e) =>
        {
            // Una fuente sin nombre o repetida no se guarda: el diálogo sigue abierto con el aviso.
            if (_editorFuentes.Validar() is not null)
            {
                e.Cancel = true;
                return;
            }
            Guardar();
        };
    }

    /// <summary>Disposición y marcos tal como quedarían si se pulsara "Guardar" ahora.</summary>
    private (Disposicion Disposicion, Dictionary<string, double> Marcos) Editado()
    {
        var tamanos = new Dictionary<string, SizeMm>(StringComparer.OrdinalIgnoreCase);
        foreach (var (m, caja) in _diagonales)
        {
            var valor = Valor(caja);
            if (valor > 0 && Math.Abs(valor - DiagonalDetectada(m.Id)) > 0.05)
                tamanos[m.Id] = TamanoPantalla.DesdeDiagonal(valor, m.Escritorio.Ancho, m.Escritorio.Alto);
        }
        var marcos = new Dictionary<string, double>(_cfg.MarcosMm, StringComparer.OrdinalIgnoreCase);
        foreach (var (claves, caja) in _marcos)
            foreach (var clave in claves) marcos[clave] = Valor(caja);
        return (_detectada.ConCorrecciones(tamanos), marcos);
    }

    private double DiagonalDetectada(string id) =>
        _detectada.Buscar(id) is { } m ? TamanoPantalla.DiagonalPulgadas(m.TamanoFisico) : 0;

    private void PintarCarpetas()
    {
        _listaCarpetas.Children.Clear();
        if (_carpetas.Count == 0)
            _listaCarpetas.Children.Add(Nota("No hay carpetas: añade al menos una para llenar la biblioteca."));
        for (var i = 0; i < _carpetas.Count; i++)
        {
            var indice = i;
            var c = _carpetas[i];
            var existe = Directory.Exists(c.Ruta);
            var ruta = new TextBlock
            {
                Text = existe ? c.Ruta : $"{c.Ruta} (no disponible)",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            ToolTipService.SetToolTip(ruta, c.Ruta);
            if (!existe) ruta.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorDisabledBrush"];
            var sub = new CheckBox { Content = "Incluir subcarpetas", IsChecked = c.IncluirSubcarpetas, MinWidth = 0 };
            sub.Checked += (_, _) => _carpetas[indice] = _carpetas[indice] with { IncluirSubcarpetas = true };
            sub.Unchecked += (_, _) => _carpetas[indice] = _carpetas[indice] with { IncluirSubcarpetas = false };
            var quitar = new Button { Content = "Quitar" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(quitar, $"Quitar {c.Ruta}");
            quitar.Click += (_, _) => { _carpetas.RemoveAt(indice); PintarCarpetas(); };

            var fila = new Grid { ColumnSpacing = 12 };
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(sub, 1);
            Grid.SetColumn(quitar, 2);
            fila.Children.Add(ruta);
            fila.Children.Add(sub);
            fila.Children.Add(quitar);
            _listaCarpetas.Children.Add(fila);
        }
    }

    private async Task AnadirCarpetaAsync()
    {
        var selector = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        selector.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(selector, _ventana);
        var carpeta = await selector.PickSingleFolderAsync();
        if (carpeta is null || string.IsNullOrEmpty(carpeta.Path)) return;
        if (_carpetas.Any(c => string.Equals(Path.TrimEndingDirectorySeparator(c.Ruta), Path.TrimEndingDirectorySeparator(carpeta.Path),
                                             StringComparison.OrdinalIgnoreCase)))
            return;
        _carpetas.Add(new CarpetaVigilada(carpeta.Path, true));
        PintarCarpetas();
    }

    private void PintarOcupacion()
    {
        var mb = _bytesCache / 1048576.0;
        _ocupacion.Text = $"Ocupa {mb.ToString("0.0", Espanol)} MB de {Valor(_tope).ToString("0", Espanol)} MB";
    }

    private void PintarCarpetaDescargas()
    {
        var existe = Directory.Exists(_rutaDescargas);
        _carpetaDescargas.Text = existe ? _rutaDescargas : $"{_rutaDescargas} (no existe; se creará al descargar)";
        ToolTipService.SetToolTip(_carpetaDescargas, _rutaDescargas);
    }

    private async Task ElegirCarpetaDescargasAsync()
    {
        var selector = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        selector.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(selector, _ventana);
        var carpeta = await selector.PickSingleFolderAsync();
        if (carpeta is null || string.IsNullOrEmpty(carpeta.Path)) return;
        _rutaDescargas = carpeta.Path;
        PintarCarpetaDescargas();
    }

    private void Guardar()
    {
        var fuentes = _editorFuentes.Fuentes.ToList();
        FuentesCambiadas = !MismasFuentes(fuentes, _cfg.FuentesBusqueda ?? []);
        _cfg.FuentesBusqueda = fuentes;
        _cfg.CarpetaDescargas = _rutaDescargas;
        CarpetasCambiadas = !_carpetas.SequenceEqual(_cfg.CarpetasVigiladas);
        _cfg.CarpetasVigiladas = [.. _carpetas];
        _cfg.TopeMiniaturasMb = (int)Math.Clamp(Valor(_tope), 50, 2000);
        foreach (var (m, caja) in _diagonales)
        {
            var valor = Valor(caja);
            if (Math.Abs(valor - DiagonalDetectada(m.Id)) <= 0.05 || valor <= 0)
                _cfg.TamanosCorregidos.Remove(m.Id);
            else
                _cfg.TamanosCorregidos[m.Id] = TamanoPantalla.DesdeDiagonal(valor, m.Escritorio.Ancho, m.Escritorio.Alto);
        }
        foreach (var (claves, caja) in _marcos) EditorZonas.AplicarValorGrupo(_cfg.MarcosMm, claves, Valor(caja));
    }

    private static double Valor(NumberBox caja) => double.IsNaN(caja.Value) ? 0 : caja.Value;

    private static NumberBox Numero(double valor, double minimo, double maximo, double paso, string cabecera, int decimales = 1)
    {
        var caja = CrearNumero(valor, minimo, maximo, paso, decimales);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(caja, cabecera); // la cabecera visible va fuera
        return caja;
    }

    private static NumberBox CrearNumero(double valor, double minimo, double maximo, double paso, int decimales) => new()
    {
        Value = Math.Round(valor / paso) * paso,
        Minimum = minimo,
        Maximum = maximo,
        SmallChange = paso,
        LargeChange = paso * 10,
        Width = 100,
        // Sin flechas propias: el control no deja colocarlas a cada lado; las pone ConFlechas.
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
        NumberFormatter = new DecimalFormatter
        {
            FractionDigits = decimales,
            IntegerDigits = 1,
            NumberRounder = new IncrementNumberRounder { Increment = paso, RoundingAlgorithm = RoundingAlgorithm.RoundHalfUp },
        },
    };

    /// <summary>
    /// [⌄] [valor] [⌃]: bajar a la izquierda y subir a la derecha.
    /// Mantener pulsado repite; el teclado y la rueda siguen funcionando en la caja.
    /// </summary>
    private static StackPanel ConFlechas(NumberBox caja, string cabecera)
    {
        RepeatButton Flecha(string glifo, string nombre, int signo)
        {
            var b = new RepeatButton
            {
                Content = new FontIcon { Glyph = glifo, FontSize = 12 },
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Delay = 400,
                Interval = 80,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, nombre);
            ToolTipService.SetToolTip(b, nombre);
            b.Click += (_, _) =>
                caja.Value = Math.Clamp(Math.Round(((double.IsNaN(caja.Value) ? 0 : caja.Value) + signo * caja.SmallChange) / caja.SmallChange) * caja.SmallChange,
                                        caja.Minimum, caja.Maximum);
            return b;
        }

        var fila = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Flecha("", "Reducir", -1), caja, Flecha("", "Aumentar", +1) },
        };
        return new StackPanel
        {
            Spacing = 4,
            Children = { new TextBlock { Text = cabecera }, fila },
        };
    }

    private static Grid Fila(string etiqueta, FrameworkElement caja, FrameworkElement boton) =>
        Fila(new TextBlock { Text = etiqueta, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap },
             caja, boton);

    private static Grid Fila(FrameworkElement texto, FrameworkElement caja, FrameworkElement boton)
    {
        var fila = new Grid { ColumnSpacing = 12 };
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fila.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(caja, 1);
        Grid.SetColumn(boton, 2);
        fila.Children.Add(texto);
        fila.Children.Add(caja);
        fila.Children.Add(boton);
        return fila;
    }

    /// <summary>Comparación por contenido (las cabeceras son un diccionario: el record no las compara por valor).</summary>
    private static bool MismasFuentes(IReadOnlyList<FuenteBusqueda> a, IReadOnlyList<FuenteBusqueda> b) =>
        a.Count == b.Count && a.Zip(b).All(p =>
            p.First with { Cabeceras = p.Second.Cabeceras } == p.Second
            && p.First.Cabeceras.OrderBy(c => c.Key).SequenceEqual(p.Second.Cabeceras.OrderBy(c => c.Key)));

    /// <summary>Marca de los títulos de sección: cada uno abre una tarjeta nueva en <see cref="EnTarjetas"/>.</summary>
    private const string MarcaSeccion = "Seccion";

    private static TextBlock Cabecera(string texto) => new()
    {
        Text = texto,
        Tag = MarcaSeccion,
        Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        FontSize = 16,
    };

    /// <summary>
    /// Reparte el contenido en una tarjeta por sección (fondo, borde y esquinas redondeadas): sin esto las
    /// opciones de secciones distintas se mezclaban a la vista.
    /// </summary>
    private static StackPanel EnTarjetas(StackPanel panel)
    {
        var hijos = panel.Children.ToList();
        panel.Children.Clear();
        var tarjetas = new StackPanel { Spacing = 14, MinWidth = panel.MinWidth, Padding = panel.Padding };
        StackPanel? actual = null;
        foreach (var hijo in hijos)
        {
            if (actual is null || hijo is TextBlock { Tag: MarcaSeccion })
            {
                actual = new StackPanel { Spacing = 10 };
                tarjetas.Children.Add(new Border
                {
                    Padding = new Thickness(18, 14, 18, 16),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    Child = actual,
                });
            }
            actual.Children.Add(hijo);
        }
        return tarjetas;
    }

    private static TextBlock Nota(string texto) => new()
    {
        Text = texto,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };
}
