using System.Runtime.InteropServices.WindowsRuntime;
using DeskWallpaper.App.Nativo;
using DeskWallpaper.App.Vistas;
using DeskWallpaper.Nucleo;
using DeskWallpaper.Windows;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace DeskWallpaper.App;

/// <summary>Ventana principal: lienzo con los monitores, panel de la zona seleccionada y aplicar.</summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Lado mayor de la copia reducida que dibuja el lienzo.</summary>
    private const int LadoReducida = 1600;

    private readonly string _carpetaDatos;
    private readonly Configuracion _cfg;
    private readonly SubclaseVentana _subclase;
    /// <summary>Trata cada vídeo como su fotograma de referencia.</summary>
    private readonly RenderizadorConVideos _renderizador;

    /// <summary>El fondo animado: animado.json, el ayudante y su arranque con Windows.</summary>
    private readonly ServicioAnimado _animado;

    /// <summary>Copias de los vídeos del tamaño de sus monitores (opción de «Reproducción…»).</summary>
    private readonly VideosReducidos _videosReducidos;

    /// <summary>La preparación de vídeos en curso, para cortarla si se cierra la ventana.</summary>
    private CancellationTokenSource? _ctsReduccion;

    /// <summary>El ayudante en C++ va junto al programa.</summary>
    private static string RutaAyudante => Path.Combine(AppContext.BaseDirectory, "DeskWallpaper.Animado.exe");
    private readonly DispatcherTimer _guardadoDiferido = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Historial _historial;
    private readonly FranjaHistorial _franja = new();
    private bool _ocupado;
    private bool _cerrando;
    private PruebaFondos? _pruebaEnCurso;

    private Disposicion _detectada = new([]);
    private Disposicion _disposicion = new([]);
    private LienzoFisico _lienzo = LienzoFisico.DesdeDisposicion(new Disposicion([]), new Dictionary<string, double>());
    private IReadOnlyList<Zona> _zonas = [];
    private string _firma = "";
    private string? _zonaSeleccionada;

    /// <summary>Imagen elegida para cada zona, por Id de zona.</summary>
    private readonly Dictionary<string, AsignacionZona> _asignaciones = [];

    /// <summary>Copias reducidas ya cargadas, por ruta de imagen.</summary>
    private readonly Dictionary<string, (BitmapImage Imagen, SizePx Original)> _reducidas = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _cargando = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fallidas = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Recursos", "DeskWallpaper.ico"));

        _carpetaDatos = CarpetaDatos.Resolver(Environment.GetCommandLineArgs(),
            () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        _cfg = AlmacenConfiguracion.Cargar(_carpetaDatos);
        _renderizador = new RenderizadorConVideos(new RenderizadorSkia(),
            new FotogramasVideo(_carpetaDatos, new LectorVideoAyudante(RutaAyudante)));
        var porDefecto = string.Equals(Path.GetFullPath(_carpetaDatos).TrimEnd('\\'),
            Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWallpaper")).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
        _videosReducidos = new VideosReducidos(_carpetaDatos, _renderizador.Fotogramas, new ReductorVideoAyudante(RutaAyudante));
        var id = ServicioAnimado.Id(_carpetaDatos);
        _animado = new ServicioAnimado(_carpetaDatos, porDefecto, RutaAyudante,
            new ControlAyudante(RutaAyudante, _carpetaDatos, id), new TareaArranque(id));
        // Si el usuario ha movido la carpeta del programa, el arranque con Windows apuntaría a la ruta vieja.
        try { _animado.CorregirArranque(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        _historial = new Historial(_carpetaDatos);
        BorrarCacheObsoleta();

        _franja.Elegida += (_, e) => CargarDesdeHistorial(e);
        _franja.Quitar += async (_, e) => await Protegido(async () => { _historial.Borrar(e.Id); await RefrescarHistorialAsync(); });

        Detectar();
        RestaurarVentana();

        Lienzo.ElegirImagen += async (_, zona) => await Protegido(() => ElegirImagenAsync(zona));
        Lienzo.ArchivoSoltado += (_, a) => Asignar(a.Zona, a.Ruta);
        Lienzo.ZonaSeleccionada += (_, zona) => { _zonaSeleccionada = zona?.Id; Refrescar(); };
        Lienzo.EncuadreCambiado += (_, c) => CambiarEncuadre(c.Zona, c.Encuadre, c.Final);
        Lienzo.EnlaceCambiado += (_, c) => CambiarEnlace(c.A, c.B, c.Unir);

        Panel.ZoomCambiado += (_, zoom) => ConZonaSeleccionada((zona, a, original) =>
            CambiarEncuadre(zona, EncuadreInteractivo.Zoom(original, _lienzo.Envolvente(zona), a.Encuadre, zoom / a.Encuadre.Zoom), final: false, redibujar: true));
        ConfigurarDivisores();
        Panel.ReducirCambiado += (_, reducir) =>
        {
            _cfg.Reproduccion = _cfg.Reproduccion with { ReducirVideos = reducir };
            ProgramarGuardado();
            Refrescar();
        };
        Panel.CentrarPulsado += (_, _) => ConZonaSeleccionada((zona, _, _) => CambiarEncuadre(zona, new Encuadre(), final: true, redibujar: true));
        Panel.CambiarPulsado += async (_, _) =>
        {
            if (_zonas.FirstOrDefault(z => z.Id == _zonaSeleccionada) is { } zona) await Protegido(() => ElegirImagenAsync(zona));
        };
        Panel.QuitarPulsado += (_, _) =>
        {
            if (_zonaSeleccionada is null) return;
            _asignaciones.Remove(_zonaSeleccionada);
            GuardarLienzo();
            Refrescar();
        };

        Panel.AmpliarPulsado += (_, _) => AmpliarZonaSeleccionada();

        var ajustes = new Button { Content = new FontIcon { Glyph = "", FontSize = 16 }, Padding = new Thickness(10, 8, 10, 8) };
        ToolTipService.SetToolTip(ajustes, "Ajustes");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ajustes, "Ajustes");
        ajustes.Click += async (_, _) => await Protegido(AbrirAjustesAsync);
        BarraSuperior.Children.Add(ajustes);

        _guardadoDiferido.Tick += (_, _) => { _guardadoDiferido.Stop(); GuardarLienzo(); };
        AppWindow.Closing += (_, e) =>
        {
            // A mitad de generar o poner los fondos no se puede cerrar: quedarían a medias. Se cierra al terminar.
            if (_ocupado && _pruebaEnCurso is null && !_cerrando)
            {
                e.Cancel = true;
                _cerrarAlTerminar = true;
                _ctsReduccion?.Cancel();   // si se está preparando un vídeo, no hace falta esperar a que acabe
                MostrarResultado(InfoBarSeverity.Informational, "Se cerrará al terminar", "Espera a que se terminen de poner los fondos.");
                return;
            }
            AlCerrar();
        };
        _subclase = new SubclaseVentana(WinRT.Interop.WindowNative.GetWindowHandle(this),
            () => DispatcherQueue.TryEnqueue(() => _ = Protegido(() => { Detectar(); Refrescar(); return Task.CompletedTask; })),
            InstanciaUnica.MensajeTraer(id), () => DispatcherQueue.TryEnqueue(TraerAlFrente));
        Closed += (_, _) => _subclase.Dispose();

        Refrescar();
        _ = Protegido(RefrescarHistorialAsync);
        InicializarBiblioteca();
        InicializarBusqueda();
    }

    private Task RefrescarHistorialAsync() => _franja.MostrarAsync(_historial.Listar(), DateTime.Now);

    /// <summary>Guarda en el historial una combinación que se acaba de aplicar o mantener.</summary>
    private async Task RegistrarEnHistorialAsync(Combinacion c, Disposicion d, LienzoFisico l)
    {
        try
        {
            var miniatura = await Task.Run(() => _renderizador.ComponerMiniatura(c, d, l, 320));
            _historial.Registrar(c, miniatura, DateTime.Now);
            await RefrescarHistorialAsync();
        }
        catch (Exception ex)
        {
            // Los fondos ya están puestos: solo se avisa de que no se ha guardado en el historial.
            Estado.Anadir($"(No se ha podido guardar en el historial: {ex.Message})");
        }
    }

    /// <summary>Carga en el lienzo una combinación del historial, adaptando las zonas si hace falta.</summary>
    private void CargarDesdeHistorial(EntradaHistorial entrada)
    {
        var c = entrada.Combinacion;
        var validas = c.Zonas.Where(z => z.MonitorIds.All(id => _disposicion.Buscar(id) is not null)).ToList();

        // Las zonas de la combinación (por ejemplo, los 3 monitores unidos) mandan; el resto, sueltos.
        var cubiertos = validas.SelectMany(z => z.MonitorIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var grupos = validas.Select(z => z.MonitorIds)
            .Concat(_disposicion.Monitores.Where(m => !cubiertos.Contains(m.Id)).Select(m => (IReadOnlyList<string>)[m.Id]))
            .ToList();
        var nuevas = EditorZonas.ParaDisposicion(_disposicion, grupos);
        if (!nuevas.SequenceEqual(_zonas))
        {
            _zonas = nuevas;
            _cfg.ZonasPorFirma[_firma] = nuevas.Select(z => z.MonitorIds.ToList()).ToList();
        }

        _asignaciones.Clear();
        _fallidas.Clear(); // una imagen que falló antes puede volver a existir: se reintenta
        // También las imágenes que ya no existen: así la zona queda marcada con su error y "Aplicar"
        // falla entero (todo o nada) en vez de poner solo el resto.
        foreach (var z in validas)
            if (BuscarZona(z.MonitorIds) is { } zona)
                _asignaciones[zona.Id] = z with { MonitorIds = zona.MonitorIds };
        _zonaSeleccionada = null;

        var avisos = new List<string>();
        if (c.FirmaDisposicion != _firma) avisos.Add("Esta combinación es de otra disposición de monitores.");
        var faltan = Historial.ImagenesQueFaltan(c);
        if (faltan.Count > 0) avisos.Add("Faltan imágenes: " + string.Join(", ", faltan.Select(Path.GetFileName)) + ".");
        if (avisos.Count > 0) MostrarResultado(InfoBarSeverity.Warning, avisos[0], string.Join(" ", avisos.Skip(1)));
        else Estado.Ocultar();

        GuardarLienzo();
        Refrescar();
    }

    /// <summary>Otra copia del programa con la misma carpeta de datos pide traer esta ventana al frente.</summary>
    private void TraerAlFrente()
    {
        if (_cerrando) return;
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        Activate();
    }

    /// <summary>Encima de las demás ventanas y con el foco (sin restaurar ni mover).</summary>
    private void PonerDelante()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr ventana);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr ventana);

    /// <summary>Lo que llega sin capturar a <see cref="App"/>: se avisa en la barra de resultado.</summary>
    public void AvisarErrorInesperado(Exception ex)
    {
        if (_cerrando) return;
        try { MostrarResultado(InfoBarSeverity.Error, "Ha ocurrido un error", ex.Message); }
        catch (Exception) { /* ya está en error.log */ }
    }

    /// <summary>Ningún error inesperado debe cerrar la aplicación: se muestra en la barra de resultado.</summary>
    private async Task Protegido(Func<Task> accion)
    {
        try
        {
            await accion();
        }
        catch (Exception ex)
        {
            MostrarResultado(InfoBarSeverity.Error, "Ha ocurrido un error", ex.Message);
        }
    }

    /// <summary>La caché de vistas previas de la fase 1 ya no se usa: el lienzo dibuja en vivo.</summary>
    private void BorrarCacheObsoleta()
    {
        try { Directory.Delete(Path.Combine(_carpetaDatos, "cache", "previas"), true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Lee la disposición actual de Windows y actualiza zonas, lienzo y aviso de cambio.</summary>
    private void Detectar()
    {
        _detectada = DetectorMonitores.Detectar();
        _disposicion = _detectada.ConCorrecciones(_cfg.TamanosCorregidos);
        _firma = FirmaDisposicion.Calcular(_disposicion);
        _zonas = EditorZonas.ParaDisposicion(_disposicion,
            _cfg.ZonasPorFirma.TryGetValue(_firma, out var guardadas) ? guardadas : null);
        _lienzo = LienzoFisico.DesdeDisposicion(_disposicion, _cfg.MarcosMm);
        if (_cfg.UltimaFirma is not null && _cfg.UltimaFirma != _firma)
            MostrarResultado(InfoBarSeverity.Warning, "La disposición de los monitores ha cambiado desde la última vez.", "");

        // Las imágenes ya elegidas siguen asignadas si su zona sigue existiendo con los mismos monitores.
        foreach (var (idZona, asignacion) in _asignaciones.ToList())
        {
            _asignaciones.Remove(idZona);
            if (BuscarZona(asignacion.MonitorIds) is { } zona) _asignaciones[zona.Id] = asignacion;
        }

        // Y las guardadas de monitores que vuelven a estar conectados reaparecen.
        RecuperarLienzoPendiente();
        if (_zonas.All(z => z.Id != _zonaSeleccionada)) _zonaSeleccionada = null;
    }

    private Zona? BuscarZona(IReadOnlyList<string> monitorIds) =>
        _zonas.FirstOrDefault(z => z.MonitorIds.Count == monitorIds.Count
            && z.MonitorIds.All(id => monitorIds.Contains(id, StringComparer.OrdinalIgnoreCase)));

    private void RecuperarLienzoPendiente()
    {
        if (_cfg.LienzoPendiente is null) return;
        foreach (var asignacion in _cfg.LienzoPendiente.Zonas)
            if (BuscarZona(asignacion.MonitorIds) is { } zona && !_asignaciones.ContainsKey(zona.Id))
                _asignaciones[zona.Id] = asignacion with { MonitorIds = zona.MonitorIds };
    }

    /// <summary>
    /// Guarda el lienzo (aplicado o no) para seguir donde se dejó, sin perder las imágenes elegidas
    /// para monitores que ahora no están conectados.
    /// </summary>
    private void GuardarLienzo()
    {
        _guardadoDiferido.Stop();
        _cfg.LienzoPendiente = Combinacion.FusionarPendiente(_cfg.LienzoPendiente, CombinacionActual(), _disposicion);
        try
        {
            AlmacenConfiguracion.Guardar(_carpetaDatos, _cfg);
            _avisadoGuardado = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Disco lleno, o un antivirus o una copia de seguridad tiene el archivo abierto: se reintenta en un rato.
            if (_cerrando) return;
            _guardadoDiferido.Start();
            if (_avisadoGuardado) return;
            _avisadoGuardado = true;
            MostrarResultado(InfoBarSeverity.Warning, "No se ha podido guardar la configuración",
                             $"Se volverá a intentar en unos segundos. {ex.Message}");
        }
    }

    /// <summary>Ya se ha avisado de que no se puede guardar: no se repite el aviso en cada reintento.</summary>
    private bool _avisadoGuardado;

    private Combinacion CombinacionActual() =>
        new(_firma, _zonas.Where(z => _asignaciones.ContainsKey(z.Id)).Select(z => _asignaciones[z.Id]).ToList());

    private async Task ElegirImagenAsync(Zona zona)
    {
        var selector = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var ext in FormatosImagen.Extensiones.Concat(FormatosVideo.Extensiones)) selector.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(selector, WinRT.Interop.WindowNative.GetWindowHandle(this));

        var archivo = await selector.PickSingleFileAsync();
        if (archivo is not null) Asignar(zona, archivo.Path);
    }

    private void Asignar(Zona zona, string ruta)
    {
        _asignaciones[zona.Id] = new AsignacionZona(zona.MonitorIds, ruta, new Encuadre());
        _zonaSeleccionada = zona.Id;
        _fallidas.Remove(ruta);
        GuardarLienzo();
        Estado.Ocultar();
        Refrescar();
    }

    private void CambiarEncuadre(Zona zona, Encuadre encuadre, bool final, bool redibujar = false)
    {
        if (!_asignaciones.TryGetValue(zona.Id, out var a)) return;
        _asignaciones[zona.Id] = a with { Encuadre = encuadre };
        // Durante el arrastre el lienzo ya se ha movido solo; al terminar se recalculan avisos y panel.
        if (final || redibujar) Refrescar();
        _guardadoDiferido.Stop();
        _guardadoDiferido.Start();
    }

    private void ConZonaSeleccionada(Action<Zona, AsignacionZona, SizePx> accion)
    {
        if (_zonas.FirstOrDefault(z => z.Id == _zonaSeleccionada) is { } zona
            && _asignaciones.TryGetValue(zona.Id, out var a)
            && _reducidas.TryGetValue(a.RutaImagen, out var r))
            accion(zona, a, r.Original);
    }

    /// <summary>Une o separa monitores contiguos y conserva las imágenes que tenga sentido conservar.</summary>
    private void CambiarEnlace(string a, string b, bool unir)
    {
        var nuevas = unir
            ? EditorZonas.Unir(_disposicion, _zonas, a, b)
            : _zonas.FirstOrDefault(z => z.MonitorIds.Contains(a, StringComparer.OrdinalIgnoreCase)) is { } zona
                ? EditorZonas.Separar(_disposicion, _zonas, zona.Id)
                : null;
        if (nuevas is null) return;

        var antiguas = _zonas;
        var anteriores = new Dictionary<string, AsignacionZona>(_asignaciones);
        _asignaciones.Clear();
        foreach (var z in nuevas)
        {
            if (antiguas.FirstOrDefault(o => MismosMonitores(o, z)) is { } igual && anteriores.TryGetValue(igual.Id, out var conservada))
            {
                _asignaciones[z.Id] = conservada with { MonitorIds = z.MonitorIds };
            }
            else if (z.MonitorIds.Count > 1)
            {
                // Unión: hereda la imagen de la zona más a la izquierda de las que se unen.
                var fuente = antiguas
                    .Where(o => o.MonitorIds.All(id => z.MonitorIds.Contains(id, StringComparer.OrdinalIgnoreCase)) && anteriores.ContainsKey(o.Id))
                    .OrderBy(o => _disposicion.Buscar(o.MonitorIds[0])?.Escritorio.X ?? 0)
                    .FirstOrDefault();
                if (fuente is not null)
                    _asignaciones[z.Id] = new AsignacionZona(z.MonitorIds, anteriores[fuente.Id].RutaImagen, new Encuadre());
            }
        }

        _zonas = nuevas;
        _cfg.ZonasPorFirma[_firma] = nuevas.Select(z => z.MonitorIds.ToList()).ToList();
        if (_zonas.All(z => z.Id != _zonaSeleccionada)) _zonaSeleccionada = null;
        GuardarLienzo();
        Refrescar();
    }

    /// <summary>
    /// «Limpiar»: quita todas las imágenes del lienzo y deja cada monitor suelto.
    /// La barra de estado ofrece «Deshacer» por si ha sido sin querer.
    /// </summary>
    private void Limpiar_Click(object sender, RoutedEventArgs e)
    {
        var zonasAntes = _zonas;
        var asignacionesAntes = new Dictionary<string, AsignacionZona>(_asignaciones);
        var seleccionAntes = _zonaSeleccionada;

        var firmaAntes = _firma;
        _asignaciones.Clear();
        PonerZonas(EditorZonas.ParaDisposicion(_disposicion, _disposicion.Monitores.Select(m => (IReadOnlyList<string>)[m.Id]).ToList()));
        _zonaSeleccionada = null;
        GuardarLienzo();
        Refrescar();

        MostrarResultado(InfoBarSeverity.Informational, "Lienzo limpio", "Se han quitado las imágenes y los enlaces.", ("Deshacer", () =>
        {
            // Solo si la disposición sigue siendo la misma: si no, esas zonas ya no tienen sentido.
            if (_firma != firmaAntes)
            {
                MostrarResultado(InfoBarSeverity.Informational, "No se puede deshacer", "Los monitores han cambiado desde que se limpió el lienzo.");
                return;
            }
            PonerZonas(zonasAntes);
            _asignaciones.Clear();
            foreach (var (id, a) in asignacionesAntes) _asignaciones[id] = a;
            _zonaSeleccionada = seleccionAntes;
            GuardarLienzo();
            Refrescar();
            Estado.Ocultar();
        }));
    }

    /// <summary>Cambia las zonas y recuerda cómo quedan para esta disposición de monitores.</summary>
    private void PonerZonas(IReadOnlyList<Zona> zonas)
    {
        _zonas = zonas;
        _cfg.ZonasPorFirma[_firma] = zonas.Select(z => z.MonitorIds.ToList()).ToList();
    }

    private static bool MismosMonitores(Zona x, Zona y) =>
        x.MonitorIds.Count == y.MonitorIds.Count && x.MonitorIds.All(id => y.MonitorIds.Contains(id, StringComparer.OrdinalIgnoreCase));

    /// <summary>Anchos de la navegación y del panel: los guardados, y se guardan al cambiarlos.</summary>
    private void ConfigurarDivisores()
    {
        DivisorNavegacion.Columna = ColumnaNavegacion;
        DivisorNavegacion.Minimo = Configuracion.AnchoNavegacionMinimo;
        DivisorNavegacion.Maximo = Configuracion.AnchoNavegacionMaximo;
        DivisorNavegacion.PorDefecto = Configuracion.AnchoNavegacionPorDefecto;
        DivisorNavegacion.Poner(_cfg.AnchoNavegacion);
        DivisorNavegacion.AnchoCambiado += (_, ancho) => { _cfg.AnchoNavegacion = ancho; ProgramarGuardado(); };

        DivisorPanel.Columna = ColumnaPanel;
        DivisorPanel.PanelALaDerecha = true;
        DivisorPanel.Minimo = Configuracion.AnchoPanelMinimo;
        DivisorPanel.Maximo = Configuracion.AnchoPanelMaximo;
        DivisorPanel.PorDefecto = Configuracion.AnchoPanelPorDefecto;
        DivisorPanel.Poner(_cfg.AnchoPanel);
        DivisorPanel.AnchoCambiado += (_, ancho) => { _cfg.AnchoPanel = ancho; ProgramarGuardado(); };
    }

    /// <summary>El botón ⟳ de la biblioteca: vuelve a revisar las carpetas y dice si no había nada nuevo.</summary>
    private async void Refrescar_Click(object sender, RoutedEventArgs e) => await Protegido(async () =>
    {
        BotonRefrescar.IsEnabled = false;
        try
        {
            ConfigurarVigilantes();   // por si una carpeta se ha conectado (disco externo, red)
            TextoProgreso.Text = "Revisando las carpetas…";
            await EscanearAsync();
            if (TextoProgreso.Text is "" or "Revisando las carpetas…") TextoProgreso.Text = "Sin novedades";
        }
        finally
        {
            BotonRefrescar.IsEnabled = true;
        }
    });

    /// <summary>El tamaño de la copia reducida que usaría el vídeo de la zona (null si ya cabe en sus monitores).</summary>
    private SizePx? TamanoReducido(Zona zona, AsignacionZona asignacion, SizePx original)
    {
        try
        {
            var c = GeneradorAnimado.Desde(new Combinacion("x", [asignacion with { MonitorIds = zona.MonitorIds }]),
                _disposicion, _lienzo, _ => original, _cfg.Reproduccion);
            return c.Zonas.Count == 1 ? VideosReducidos.TamanoReducido(original, c.Zonas[0], _disposicion) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;   // solo es informativo: el lienzo se sigue dibujando
        }
    }

    /// <summary>Redibuja lienzo y panel con lo que haya cargado; pide en segundo plano las imágenes que falten.</summary>
    private void Refrescar()
    {
        var imagenes = new Dictionary<string, ImagenZona>();
        var avisos = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var zona in _zonas)
        {
            if (!_asignaciones.TryGetValue(zona.Id, out var a)) continue;
            if (_reducidas.TryGetValue(a.RutaImagen, out var r))
            {
                imagenes[zona.Id] = new ImagenZona(r.Imagen, r.Original, a.Encuadre, FormatosVideo.EsVideo(a.RutaImagen));
                foreach (var p in PlanRecorte.Calcular(r.Original, zona.MonitorIds, _disposicion, _lienzo, a.Encuadre))
                    if (p.Factor > AplicadorFondos.UmbralAviso) avisos[p.MonitorId] = p.Factor;
            }
            else
            {
                _ = Protegido(() => CargarReducidaAsync(a.RutaImagen));
            }
        }

        Lienzo.Mostrar(_disposicion, _lienzo, _zonas, imagenes, avisos, _zonaSeleccionada);
        BotonLimpiar.IsEnabled = _asignaciones.Count > 0 || _zonas.Any(z => z.MonitorIds.Count > 1);

        var seleccionada = _zonas.FirstOrDefault(z => z.Id == _zonaSeleccionada);
        AsignacionZona? asignada = seleccionada is not null && _asignaciones.TryGetValue(seleccionada.Id, out var s) ? s : null;
        SizePx? original = asignada is not null && _reducidas.TryGetValue(asignada.RutaImagen, out var red) ? red.Original : null;
        double? factor = seleccionada?.MonitorIds.Where(avisos.ContainsKey).Select(id => avisos[id]).DefaultIfEmpty().Max() is > 0 and var f ? f : null;
        var esVideo = asignada is not null && FormatosVideo.EsVideo(asignada.RutaImagen);
        Panel.Mostrar(seleccionada, _disposicion, asignada, original, factor, esVideo, _cfg.Reproduccion.ReducirVideos,
            esVideo && original is { } tam ? TamanoReducido(seleccionada!, asignada!, tam) : null);

        // Memoria acotada: solo se guardan las copias reducidas que el lienzo está usando (unos 7 MB cada una).
        var enUso = _asignaciones.Values.Select(a => a.RutaImagen).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ruta in _reducidas.Keys.Where(r => !enUso.Contains(r)).ToList())
            _reducidas.Remove(ruta);
    }

    private async Task CargarReducidaAsync(string ruta)
    {
        if (_reducidas.ContainsKey(ruta) || _fallidas.Contains(ruta) || !_cargando.Add(ruta)) return;
        try
        {
            var (png, original) = await Task.Run(() => _renderizador.Reducida(ruta, LadoReducida));
            _reducidas[ruta] = (await ImagenesPng.CargarAsync(png), original);
        }
        catch (Exception ex)
        {
            _fallidas.Add(ruta);
            if (!_cerrando) MostrarResultado(InfoBarSeverity.Error, "No se puede usar una de las imágenes", ex.Message);
        }
        finally
        {
            _cargando.Remove(ruta);
        }
        Refrescar();
    }

    private async Task AbrirAjustesAsync()
    {
        var dialogo = new DialogoAjustes(_cfg, _detectada, _disposicion, _zonas, _cacheMiniaturas,
                                         WinRT.Interop.WindowNative.GetWindowHandle(this), _buscador, ModelosIA_) { XamlRoot = Content.XamlRoot };
        var guardar = await dialogo.MostrarAsync() == ContentDialogResult.Primary;
        if (dialogo.CacheVaciada) _cuadricula.RecargarMiniaturas();
        if (!guardar) return;
        AlmacenConfiguracion.Guardar(_carpetaDatos, _cfg);
        Detectar();
        Refrescar();
        if (dialogo.CarpetasCambiadas) ConfigurarVigilantes();
        if (dialogo.FuentesCambiadas) ReconstruirPestanasWeb();
        // Escanea si cambiaron las carpetas y, en todo caso, aplica el tope nuevo de la caché.
        if (dialogo.CarpetasCambiadas) await EscanearAsync();
        else await Task.Run(() => _cacheMiniaturas.Recortar((long)_cfg.TopeMiniaturasMb * 1024 * 1024));
    }

    private async void Probar_Click(object sender, RoutedEventArgs e) => await Protegido(ProbarAsync);

    /// <summary>"Probar 15 s": aplica, minimiza, pregunta y mantiene o vuelve atrás.</summary>
    private async Task ProbarAsync()
    {
        var combinacion = CombinacionActual();
        if (combinacion.Zonas.Count == 0)
        {
            MostrarResultado(InfoBarSeverity.Informational, "Elige al menos una imagen", "");
            return;
        }

        if (!Ocupar()) return;
        ServicioFondosWindows? servicio = null;
        try
        {
            var disposicion = _disposicion;
            var lienzo = _lienzo;
            var avisos = new List<string>();
            var animado = await Task.Run(() => GeneradorAnimado.Desde(combinacion, disposicion, lienzo, _renderizador.LeerTamano, _cfg.Reproduccion));
            if (await ReducirVideosAsync(animado, disposicion, avisos) is not { } preparado)
            {
                MostrarResultado(InfoBarSeverity.Informational, "No se ha probado nada.", "Has cancelado la preparación del vídeo.");
                return;
            }
            animado = preparado;
            // El servicio COM se crea y se usa siempre desde hilos MTA del grupo de subprocesos.
            PruebaFondos? creada = null;
            ResultadoAplicacion r;
            try
            {
                r = await Task.Run(() =>
                {
                    servicio = new ServicioFondosWindows();
                    creada = new PruebaFondos(servicio, new AplicadorFondos(servicio, _renderizador, _carpetaDatos), _animado);
                    return creada.Empezar(combinacion, disposicion, lienzo, animado);
                });
            }
            catch
            {
                // Falló con los fondos ya cambiados (p. ej. no arranca el ayudante): se vuelve a lo de antes.
                if (creada is { Anterior: not null } c) await Task.Run(c.Volver);
                throw;
            }
            var prueba = creada!;
            if (!r.Exito)
            {
                MostrarResultado(InfoBarSeverity.Error, "No se han podido probar los fondos", string.Join("\n", r.Errores));
                return;
            }

            // Se ha pedido cerrar mientras se preparaba: no se pregunta, se vuelve a lo anterior y se cierra.
            if (_cerrarAlTerminar)
            {
                await Task.Run(prueba.Volver);
                return;
            }

            // A partir de aquí hay fondos sin confirmar: pase lo que pase (error, cierre de la ventana),
            // hay que volver a los anteriores salvo que el usuario pulse "Mantener".
            _pruebaEnCurso = prueba;
            bool mantener;
            var minimizadas = false;
            try
            {
                var (monitor, escala) = MonitorDeLaVentana();
                (AppWindow.Presenter as OverlappedPresenter)?.Minimize();
                // Todas las ventanas fuera (como Win+M) para ver bien el escritorio; la ventanita de la
                // prueba se crea después, así que queda a la vista.
                minimizadas = Escritorio.MinimizarTodo();
                if (minimizadas) await Task.Delay(300); // que termine la animación antes de abrirla
                mantener = await VentanaPrueba.PreguntarAsync(monitor, escala,
                    Path.Combine(AppContext.BaseDirectory, "Recursos", "DeskWallpaper.ico"));
            }
            catch
            {
                if (prueba.Anterior is not null) await Task.Run(prueba.Volver);
                throw;
            }
            finally
            {
                _pruebaEnCurso = null;
                if (minimizadas) Escritorio.DeshacerMinimizarTodo();
            }

            if (_cerrando) return; // al cerrar la ventana principal ya se volvió a los fondos anteriores
            // Windows devuelve las demás ventanas con una animación que acaba después de activar la app y las deja
            // encima: se espera a que terminen y luego la app se pone delante.
            if (minimizadas) await Task.Delay(500);
            if (_cerrando) return;
            (AppWindow.Presenter as OverlappedPresenter)?.Restore();
            Activate();
            if (minimizadas)
            {
                await Task.Delay(500);
                if (_cerrando) return;
                PonerDelante();   // por si alguna ventana tardó más en volver
            }

            if (mantener)
            {
                prueba.Mantener();
                _cfg.UltimaFirma = _firma;
                GuardarLienzo();
                MostrarResultado(InfoBarSeverity.Success, "Fondos mantenidos.", "");
                await RegistrarEnHistorialAsync(combinacion, disposicion, lienzo);
            }
            else if (await Task.Run(prueba.Volver))
            {
                MostrarResultado(InfoBarSeverity.Informational, "Has vuelto a los fondos anteriores.", "");
            }
            else
            {
                MostrarResultado(InfoBarSeverity.Error, "No se han podido restaurar del todo los fondos anteriores",
                    "Revisa el escritorio y vuelve a aplicar la combinación que quieras.");
            }
        }
        finally
        {
            if (servicio is not null) await Task.Run(servicio.Dispose);
            Liberar();
        }
    }

    /// <summary>
    /// Aplicar y Probar no pueden solaparse: la limpieza de la caché de uno no conoce los archivos
    /// que protege el otro. False si ya hay una operación en curso.
    /// </summary>
    private bool Ocupar()
    {
        if (_ocupado) return false;
        _ocupado = true;
        BotonProbar.IsEnabled = BotonAplicar.IsEnabled = false;
        return true;
    }

    private void Liberar()
    {
        _ocupado = false;
        if (_cerrando) return;
        if (_cerrarAlTerminar)
        {
            Close();
            return;
        }
        BotonProbar.IsEnabled = BotonAplicar.IsEnabled = true;
    }

    /// <summary>Se ha pedido cerrar mientras se aplicaba: se cierra en cuanto termine.</summary>
    private bool _cerrarAlTerminar;

    /// <summary>Al cerrar la ventana principal con una prueba sin confirmar, se vuelve a lo anterior.</summary>
    private void AlCerrar()
    {
        _cerrando = true;
        if (_pruebaEnCurso is { Anterior: not null } prueba)
        {
            Task.Run(prueba.Volver).GetAwaiter().GetResult();
            VentanaPrueba.CerrarAbierta();
        }
        CerrarBiblioteca();
        CerrarBusqueda();
        if (_guardadoDiferido.IsEnabled) GuardarLienzo();
        GuardarVentana(); // guarda también el estado de la biblioteca (está en _cfg)
    }

    /// <summary>Monitor (px físicos) que contiene el centro de la ventana principal, y su escala.</summary>
    private (global::Windows.Graphics.RectInt32 Rect, double Escala) MonitorDeLaVentana()
    {
        var cx = AppWindow.Position.X + AppWindow.Size.Width / 2;
        var cy = AppWindow.Position.Y + AppWindow.Size.Height / 2;
        var m = _disposicion.Monitores.FirstOrDefault(m => cx >= m.Escritorio.X && cx < m.Escritorio.Derecha && cy >= m.Escritorio.Y && cy < m.Escritorio.Abajo)
                ?? _disposicion.Monitores.FirstOrDefault(m => m.Principal)
                ?? _disposicion.Monitores[0];
        return (new global::Windows.Graphics.RectInt32(m.Escritorio.X, m.Escritorio.Y, m.Escritorio.Ancho, m.Escritorio.Alto), m.Escala);
    }

    private async void Aplicar_Click(object sender, RoutedEventArgs e) => await Protegido(AplicarAsync);

    private async Task AplicarAsync()
    {
        var combinacion = CombinacionActual();
        if (combinacion.Zonas.Count == 0)
        {
            MostrarResultado(InfoBarSeverity.Informational, "Elige al menos una imagen", "");
            return;
        }

        if (!Ocupar()) return;
        try
        {
            var disposicion = _disposicion;
            var lienzo = _lienzo;
            string? errorAnimado = null;
            var avisos = new List<string>();
            ConfigAnimado? animado = null;
            try { animado = await Task.Run(() => GeneradorAnimado.Desde(combinacion, disposicion, lienzo, _renderizador.LeerTamano, _cfg.Reproduccion)); }
            catch (Exception ex) { errorAnimado = ex.Message; }   // el aplicador dirá qué vídeo no se puede leer
            if (animado is not null)
            {
                if (await ReducirVideosAsync(animado, disposicion, avisos) is not { } preparado)
                {
                    MostrarResultado(InfoBarSeverity.Informational, "No se ha aplicado nada.", "Has cancelado la preparación del vídeo.");
                    return;
                }
                animado = preparado;
            }
            var r = await Task.Run(() =>
            {
                using var servicio = new ServicioFondosWindows();
                var resultado = new AplicadorFondos(servicio, _renderizador, _carpetaDatos).Aplicar(combinacion, disposicion, lienzo);
                if (!resultado.Exito || animado is null) return resultado;
                // Con los fondos de reserva ya puestos: aplicar sustituye todo el fondo animado (sin vídeos, se quita).
                try { _animado.Activar(animado); }
                catch (Exception ex) { errorAnimado = ex.Message; }
                try { _videosReducidos.Recortar(animado.Zonas.Select(z => z.Video)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* se recortará la próxima vez */ }
                return resultado;
            });

            if (r.Exito)
            {
                _cfg.UltimaFirma = _firma;
                GuardarLienzo(); // lo que haya ahora en el lienzo, incluido lo elegido mientras se aplicaba
                if (errorAnimado is not null)
                    MostrarResultado(InfoBarSeverity.Warning, "No se ha podido iniciar el fondo animado",
                        $"Queda puesto un fotograma fijo. {errorAnimado}");
                else if (avisos.Count > 0)
                    MostrarResultado(InfoBarSeverity.Warning, "Fondos aplicados.", string.Join("\n", avisos));
                else
                    MostrarResultado(InfoBarSeverity.Success, "Fondos aplicados.",
                        "Mira el escritorio: si no te convence, cambia las imágenes y vuelve a aplicar.");
                await RegistrarEnHistorialAsync(combinacion, disposicion, lienzo);
            }
            else
            {
                MostrarResultado(InfoBarSeverity.Error, "No se han aplicado los fondos", string.Join("\n", r.Errores));
            }
        }
        finally
        {
            Liberar();
        }
    }

    /// <summary>
    /// Con la opción «Reducir los vídeos más grandes que sus monitores», cambia cada vídeo grande por una copia de su
    /// tamaño (con diálogo de progreso si hay que hacerla). Null si el usuario cancela.
    /// </summary>
    private async Task<ConfigAnimado?> ReducirVideosAsync(ConfigAnimado c, Disposicion d, List<string> avisos)
    {
        if (!_cfg.Reproduccion.ReducirVideos || c.Zonas.Count == 0) return c;
        return await DialogoReduccion.PrepararAsync(Content.XamlRoot, _videosReducidos, c, d, avisos, cts => _ctsReduccion = cts);
    }

    private void MostrarResultado(InfoBarSeverity gravedad, string titulo, string mensaje, (string Texto, Action Accion)? accion = null)
    {
        Estado.Mostrar(gravedad, titulo, mensaje, accion);
    }

    private void RestaurarVentana()
    {
        var v = _cfg.Ventana;
        var dentro = v is not null && _disposicion.Monitores.Any(m =>
            v.X + v.Ancho / 2 >= m.Escritorio.X && v.X + v.Ancho / 2 < m.Escritorio.Derecha &&
            v.Y + v.Alto / 2 >= m.Escritorio.Y && v.Y + v.Alto / 2 < m.Escritorio.Abajo);

        if (dentro)
        {
            AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(v!.X, v.Y, v.Ancho, v.Alto));
            // Maximizar aquí no sirve: al mostrarse, la ventana vuelve al tamaño normal. Se hace en Mostrar.
            _maximizarAlMostrar = v.Maximizada;
        }
        else
        {
            AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1400, 900));
        }
    }

    private bool _maximizarAlMostrar;

    /// <summary>Muestra la ventana en el estado en que se cerró (maximizada si lo estaba).</summary>
    public void Mostrar()
    {
        Activate();
        if (_maximizarAlMostrar && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
    }

    private void GuardarVentana()
    {
        var estado = (AppWindow.Presenter as OverlappedPresenter)?.State;
        _cfg.Ventana = EstadoVentana.AlCerrar(_cfg.Ventana,
            AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height,
            maximizada: estado == OverlappedPresenterState.Maximized,
            minimizada: estado == OverlappedPresenterState.Minimized);
        AlmacenConfiguracion.Guardar(_carpetaDatos, _cfg);
    }
}
