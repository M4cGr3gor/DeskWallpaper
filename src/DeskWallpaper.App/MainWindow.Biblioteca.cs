using DeskWallpaper.App.Vistas;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App;

/// <summary>La biblioteca dentro de la ventana principal.</summary>
public sealed partial class MainWindow
{
    private const string SeccionHistorial = "Historial";

    private RepositorioBiblioteca _repo = null!;
    private CacheMiniaturas _cacheMiniaturas = null!;
    private EscanerBiblioteca _escaner = null!;
    private readonly CuadriculaBiblioteca _cuadricula = new();
    private IReadOnlyList<ImagenBiblioteca> _biblioteca = [];

    /// <summary>Lo que muestra ahora la cuadrícula (sección, filtros y orden): por donde pasa el visor con las flechas.</summary>
    private IReadOnlyList<ImagenBiblioteca> _listaVisible = [];
    private CancellationTokenSource? _ctsEscaneo;
    private Task? _escaneoActual;
    private readonly List<FileSystemWatcher> _vigilantes = [];
    private readonly DispatcherTimer _esperaEscaneo = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _esperaBusqueda = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _esperaGuardado = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _revision = new() { Interval = TimeSpan.FromMinutes(2) };
    private HashSet<string> _vigiladasDisponibles = new(StringComparer.OrdinalIgnoreCase);

    private bool EnAnimados => _cfg.PestanaActiva == nameof(Pestana.Animados);

    private Pestana PestanaActual => EnAnimados ? Pestana.Animados : Pestana.Imagenes;

    /// <summary>Lo que se ve en la pestaña activa: cada pestaña recuerda su sección, sus filtros y su tamaño de miniatura.</summary>
    private EstadoBiblioteca EstadoBib
    {
        get => EnAnimados ? _cfg.BibliotecaAnimados : _cfg.Biblioteca;
        set
        {
            if (EnAnimados) _cfg.BibliotecaAnimados = value;
            else _cfg.Biblioteca = value;
        }
    }

    /// <summary>Cambia entre «Imágenes» y «Fondos animados»: cada una vuelve a lo que tenía.</summary>
    private void CambiarPestana(Pestana p)
    {
        if (p == PestanaActual) return;
        _cfg.PestanaActiva = p.ToString();
        OcultarBusqueda();
        Buscador.Text = EstadoBib.Texto;
        DeslizadorMiniaturas.Value = EstadoBib.TamanoMiniatura;
        Subtitulo.Text = EnAnimados
            ? "Arrastra fondos animados a los monitores, encuádralos y pulsa Aplicar. Se pueden mezclar con imágenes."
            : "Arrastra imágenes de la biblioteca a los monitores, encuádralas y pulsa Aplicar.";
        RefrescarBiblioteca();
        ProgramarGuardado();
    }

    /// <summary>Las etiquetas de la pestaña activa, de la más usada a la menos.</summary>
    private List<(string Etiqueta, int Usos)> EtiquetasDePestana() =>
        _biblioteca.Where(i => i.EsVideo == EnAnimados)
            .SelectMany(i => i.Etiquetas.Select(e => (Etiqueta: e, i.Id)))
            .GroupBy(t => t.Etiqueta, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Etiqueta: g.Key, Usos: g.Select(t => t.Id).Distinct().Count()))
            .OrderByDescending(e => e.Usos).ThenBy(e => e.Etiqueta, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private async Task AbrirReproduccionAsync()
    {
        if (await DialogoReproduccion.PedirAsync(Content.XamlRoot, _cfg.Reproduccion) is not { } o) return;
        _cfg.Reproduccion = o;
        ProgramarGuardado();
        // Con un fondo animado puesto se aplica ya: mismas zonas y pausa manual, opciones nuevas. Reducir o no los
        // vídeos cuenta a partir del próximo «Aplicar».
        if (AlmacenAnimado.Leer(_carpetaDatos) is { Zonas.Count: > 0 } actual)
            await Task.Run(() => _animado.Activar(actual with { TopeFps = o.TopeFps, PausarMaximizadas = o.PausarMaximizadas }));
        MostrarResultado(InfoBarSeverity.Success, "Opciones de reproducción guardadas", "");
    }

    private void InicializarBiblioteca()
    {
        Pestanas.SelectedItem = EnAnimados ? PestanaAnimados : PestanaImagenes;
        if (EnAnimados)
            Subtitulo.Text = "Arrastra fondos animados a los monitores, encuádralos y pulsa Aplicar. Se pueden mezclar con imágenes.";
        Pestanas.SelectionChanged += (_, _) => CambiarPestana(Pestanas.SelectedItem == PestanaAnimados ? Pestana.Animados : Pestana.Imagenes);
        _repo = new RepositorioBiblioteca(_carpetaDatos);
        _cacheMiniaturas = new CacheMiniaturas(_carpetaDatos, _renderizador);
        _escaner = new EscanerBiblioteca(_repo, _renderizador, _carpetaDatos, _renderizador.Fotogramas);

        Nav.SeccionCambiada += (_, s) =>
        {
            if (s == Navegacion.BuscarOnline)
            {
                MostrarBusqueda();
                return;
            }
            if (s == Navegacion.Reproduccion)
            {
                _ = Protegido(AbrirReproduccionAsync);
                return;
            }
            OcultarBusqueda();
            CambiarEstado(EstadoBib with { Seccion = s });
        };
        // Una etiqueta o un color filtran la biblioteca: si se estaba en "Buscar online", se vuelve a ella.
        Nav.EtiquetaCambiada += (_, e) => { OcultarBusqueda(); CambiarEstado(EstadoBib with { Etiqueta = e }); };
        Nav.ColorCambiado += (_, c) => { OcultarBusqueda(); CambiarEstado(EstadoBib with { Color = c }); };

        Buscador.Text = EstadoBib.Texto;
        Buscador.TextChanged += (_, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            _esperaBusqueda.Stop();
            _esperaBusqueda.Start();
        };
        _esperaBusqueda.Tick += (_, _) =>
        {
            _esperaBusqueda.Stop();
            CambiarEstado(EstadoBib with { Texto = Buscador.Text });
        };

        DeslizadorMiniaturas.Value = EstadoBib.TamanoMiniatura;
        DeslizadorMiniaturas.ValueChanged += (_, e) =>
        {
            EstadoBib = EstadoBib with { TamanoMiniatura = (int)e.NewValue };
            _cuadricula.CambiarTamano(ProporcionMiniaturas(), EstadoBib.TamanoMiniatura);
            ProgramarGuardado();
        };
        _esperaGuardado.Tick += (_, _) =>
        {
            _esperaGuardado.Stop();
            try { AlmacenConfiguracion.Guardar(_carpetaDatos, _cfg); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* se vuelve a intentar al cerrar */ }
        };

        _cuadricula.Ver += async (_, img) => await Protegido(() => VerAsync(img));
        _cuadricula.UsarEnZona += (_, img) => UsarEnZonaSeleccionada(img);
        _cuadricula.CambiarFavorita += (_, img) => CambiarFavorita(img);
        _cuadricula.Etiquetar += async (_, img) => await Protegido(() => EtiquetarAsync(img));
        _cuadricula.QuitarMarcas += async (_, img) => await Protegido(() => EditarAsync(img, HerramientaEdicion.Borrar));
        _cuadricula.AmpliarIA += async (_, img) => await Protegido(() => EditarAsync(img, HerramientaEdicion.Ampliar));
        _cuadricula.QuitarRuido += async (_, img) => await Protegido(() => EditarAsync(img, HerramientaEdicion.Limpiar));

        _esperaEscaneo.Tick += (_, _) => { _esperaEscaneo.Stop(); _ = Protegido(EscanearAsync); };

        _biblioteca = _repo.Listar();
        RefrescarBiblioteca();
        ConfigurarVigilantes();
        _ = Protegido(EscanearAsync);
        _revision.Tick += async (_, _) => await Protegido(RevisarAsync);
        _revision.Start();
    }

    private void CerrarBiblioteca()
    {
        _ctsEscaneo?.Cancel();
        _esperaGuardado.Stop(); // al cerrar se guarda todo igualmente
        _revision.Stop();
        foreach (var v in _vigilantes) v.Dispose();
        _vigilantes.Clear();
    }

    private void CambiarEstado(EstadoBiblioteca estado)
    {
        EstadoBib = estado;
        RefrescarBiblioteca();
        ProgramarGuardado();
    }

    /// <summary>
    /// Guarda la configuración un segundo después del último cambio, sin esperar a cerrar la ventana:
    /// si Windows se apaga o el proceso termina de golpe, no se pierde lo elegido.
    /// </summary>
    private void ProgramarGuardado()
    {
        if (_cerrando) return;
        _esperaGuardado.Stop();
        _esperaGuardado.Start();
    }

    private bool EnHistorial => EstadoBib.Seccion == SeccionHistorial;

    /// <summary>Nombre de la sección resaltada en la navegación.</summary>
    private string SeccionActiva() =>
        _enBusqueda ? Navegacion.BuscarOnline : EnHistorial ? Navegacion.Historial : EstadoBib.SeccionActual.ToString();

    private double ProporcionMiniaturas() => EstadoBib.SeccionActual == SeccionBiblioteca.Panoramica ? 3.2 : 1.6;

    /// <summary>Navegación, título y zona inferior (cuadrícula o historial) según el estado.</summary>
    private void RefrescarBiblioteca()
    {
        var e = EstadoBib;
        Nav.Mostrar(PestanaActual, SeccionActiva(), ContarSecciones(), EtiquetasDePestana(), e.Etiqueta, e.Color);

        ControlTamano.Visibility = EnHistorial ? Visibility.Collapsed : Visibility.Visible;
        if (EnHistorial)
        {
            TituloSeccion.Text = "Historial";
            ZonaInferior.Content = _franja;
            return;
        }

        var lista = FiltroBiblioteca.Aplicar(_biblioteca, new CriterioBiblioteca(e.SeccionActual, e.Texto, e.Etiqueta, e.Color, PestanaActual), _zonas, _lienzo);
        _listaVisible = lista;
        var filtros = new List<string>();
        if (e.Etiqueta is not null) filtros.Add($"etiqueta «{e.Etiqueta}»");
        if (e.Color is { } c) filtros.Add($"color {Navegacion.Nombre(c).ToLowerInvariant()}");
        if (e.Texto.Trim().Length > 0) filtros.Add($"«{e.Texto.Trim()}»");
        var unidad = EnAnimados ? (lista.Count == 1 ? "fondo animado" : "fondos animados") : (lista.Count == 1 ? "imagen" : "imágenes");
        TituloSeccion.Text = $"{NombreSeccion(e.SeccionActual)} · {lista.Count} {unidad}"
                             + (filtros.Count > 0 ? " · " + string.Join(", ", filtros) : "");
        ZonaInferior.Content = _cuadricula;
        _cuadricula.Mostrar(lista, ProporcionMiniaturas(), e.TamanoMiniatura, ObtenerMiniaturaAsync);
    }

    private string NombreSeccion(SeccionBiblioteca s) => (s, EnAnimados) switch
    {
        (SeccionBiblioteca.Panoramica, false) => "Panorámicas",
        (SeccionBiblioteca.Panoramica, true) => "Panorámicos",
        (SeccionBiblioteca.UnMonitor, _) => "Un monitor",
        (SeccionBiblioteca.Otras, false) => "Otras",
        (SeccionBiblioteca.Otras, true) => "Otros",
        (SeccionBiblioteca.Favoritas, false) => "Favoritas",
        (SeccionBiblioteca.Favoritas, true) => "Favoritos",
        (_, false) => "Todas",
        (_, true) => "Todos",
    };

    private Task<string?> ObtenerMiniaturaAsync(ImagenBiblioteca img) => Task.Run<string?>(() =>
    {
        try { return _cacheMiniaturas.Obtener(img.Huella, img.Ruta); }
        catch (Exception ex) when (ex is ExcepcionImagen or IOException or UnauthorizedAccessException) { return null; }
    });

    /// <summary>
    /// Escaneo incremental en segundo plano. Uno nuevo cancela el anterior y espera a que acabe, para
    /// que dos escaneos con carpetas distintas nunca escriban a la vez.
    /// </summary>
    private Task EscanearAsync()
    {
        _ctsEscaneo?.Cancel();
        var cts = _ctsEscaneo = new CancellationTokenSource();
        return _escaneoActual = EscanearTrasAsync(_escaneoActual, cts);
    }

    private async Task EscanearTrasAsync(Task? anterior, CancellationTokenSource cts)
    {
        if (anterior is not null)
        {
            try { await anterior; }
            catch (Exception) { /* ya lo notificó quien lo lanzó */ }
        }
        try
        {
            if (!cts.IsCancellationRequested && !_cerrando) await EscanearUnaVezAsync(cts);
        }
        finally
        {
            if (ReferenceEquals(_ctsEscaneo, cts)) _ctsEscaneo = null;
            cts.Dispose();
        }
    }

    private async Task EscanearUnaVezAsync(CancellationTokenSource cts)
    {
        var progreso = new Progress<ProgresoEscaneo>(p =>
            TextoProgreso.Text = $"Analizando biblioteca… {p.Hechas} de {p.Total}" + (p.Omitidas > 0 ? $" ({p.Omitidas} omitidas)" : ""));
        try
        {
            var final = await _escaner.EscanearAsync(_cfg.CarpetasVigiladas, progreso, cts.Token);
            if (cts.IsCancellationRequested || _cerrando) return;
            var nueva = await Task.Run(_repo.Listar);
            // Si nada ha cambiado no se rehace la cuadrícula (volvería arriba mientras se navega).
            if (!MismoListado(_biblioteca, nueva))
            {
                _biblioteca = nueva;
                RefrescarBiblioteca();
            }
            var noDisponibles = _cfg.CarpetasVigiladas.Count(c => !Directory.Exists(c.Ruta));
            TextoProgreso.Text = (final.Omitidas switch
                                 {
                                     0 => "",
                                     1 => "1 archivo no se ha podido leer. ",
                                     var n => $"{n} archivos no se han podido leer. ",
                                 })
                                 + (noDisponibles switch
                                 {
                                     0 => "",
                                     1 => "1 carpeta no está disponible.",
                                     var n => $"{n} carpetas no están disponibles.",
                                 });
            var tope = (long)_cfg.TopeMiniaturasMb * 1024 * 1024;
            await Task.Run(() => _cacheMiniaturas.Recortar(tope));
        }
        catch (OperationCanceledException)
        {
            // Lo ha sustituido un escaneo más reciente.
        }
    }

    private static bool MismoListado(IReadOnlyList<ImagenBiblioteca> a, IReadOnlyList<ImagenBiblioteca> b) =>
        a.Count == b.Count && a.Zip(b).All(p =>
            p.First.Id == p.Second.Id && p.First.Huella == p.Second.Huella && p.First.Ruta == p.Second.Ruta
            && p.First.Tamano == p.Second.Tamano && p.First.Favorita == p.Second.Favorita && p.First.Disponible == p.Second.Disponible
            && p.First.DuracionMs == p.Second.DuracionMs
            && p.First.Colores.SequenceEqual(p.Second.Colores) && p.First.Etiquetas.SequenceEqual(p.Second.Etiquetas));

    /// <summary>
    /// Solo interesan imágenes, vídeos y carpetas (no .txt, descargas a medias…), y nunca la carpeta de datos:
    /// generar miniaturas o fondos dentro de una carpeta vigilada provocaría escaneos sin fin.
    /// </summary>
    private bool Interesa(string ruta) => EscanerBiblioteca.CambioInteresa(ruta, _carpetaDatos);

    /// <summary>Vigila las carpetas mientras la ventana está abierta.</summary>
    private void ConfigurarVigilantes()
    {
        foreach (var v in _vigilantes) v.Dispose();
        _vigilantes.Clear();
        _vigiladasDisponibles = new HashSet<string>(
            _cfg.CarpetasVigiladas.Where(c => Directory.Exists(c.Ruta)).Select(c => c.Ruta), StringComparer.OrdinalIgnoreCase);
        foreach (var c in _cfg.CarpetasVigiladas.Where(c => _vigiladasDisponibles.Contains(c.Ruta)))
        {
            try
            {
                var v = new FileSystemWatcher(c.Ruta)
                {
                    IncludeSubdirectories = c.IncluirSubcarpetas,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                FileSystemEventHandler cambio = (_, e) => { if (Interesa(e.FullPath)) DispatcherQueue.TryEnqueue(ProgramarEscaneo); };
                v.Created += cambio;
                v.Deleted += cambio;
                v.Changed += cambio;
                v.Renamed += (_, e) => { if (Interesa(e.FullPath) || Interesa(e.OldFullPath)) DispatcherQueue.TryEnqueue(ProgramarEscaneo); };
                // Búfer desbordado: se han perdido avisos, así que se revisa todo.
                v.Error += (_, _) => DispatcherQueue.TryEnqueue(ProgramarEscaneo);
                v.EnableRaisingEvents = true;
                _vigilantes.Add(v);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Sin vigilancia en esa carpeta: se sigue escaneando al abrir.
            }
        }
    }

    /// <summary>
    /// Cada pocos minutos: aplica el tope de la caché de miniaturas (que crece al navegar) y, si una
    /// carpeta vigilada se ha conectado o desconectado (disco externo, red), la vigila y vuelve a escanear.
    /// </summary>
    private async Task RevisarAsync()
    {
        if (_cerrando) return;
        var carpetas = _cfg.CarpetasVigiladas.Select(c => c.Ruta).ToList();
        var tope = (long)_cfg.TopeMiniaturasMb * 1024 * 1024;
        // Directory.Exists con una unidad de red caída puede tardar: fuera del hilo de la interfaz.
        var disponibles = await Task.Run(() =>
        {
            _cacheMiniaturas.Recortar(tope);
            return carpetas.Where(Directory.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
        });
        if (_cerrando || disponibles.SetEquals(_vigiladasDisponibles)) return;
        ConfigurarVigilantes();
        ProgramarEscaneo();
    }

    /// <summary>Agrupa los cambios de 1 s y relanza el escaneo.</summary>
    private void ProgramarEscaneo()
    {
        if (_cerrando) return;
        _esperaEscaneo.Stop();
        _esperaEscaneo.Start();
    }

    /// <summary>
    /// Abre el visor con la sección que se está viendo (con sus filtros y en su orden), para pasar de imagen con
    /// las flechas. En el monitor de la ventana: es para ver la imagen, no una vista previa del fondo.
    /// </summary>
    private async Task VerAsync(ImagenBiblioteca img)
    {
        if (!AvisarSiNoDisponible(img)) return;
        var lista = _listaVisible.Any(i => i.Id == img.Id) ? _listaVisible : [img];
        var elementos = lista.Select(i => new ElementoVisor(i.Ruta, NombreTipo(ClasificadorProporcion.Clasificar(i.Tamano, _zonas, _lienzo)),
                                                            i.Etiquetas, i.Favorita, i.EsVideo,
                                                            i.EsVideo ? TimeSpan.FromMilliseconds(i.DuracionMs) : null)).ToList();
        var indice = Math.Max(0, lista.ToList().FindIndex(i => i.Id == img.Id));
        var (rect, _) = MonitorDeLaVentana();
        await VisorBasico.MostrarAsync(new OpcionesVisor(elementos, indice, rect, _renderizador, ActivarSiAbierta,
            e => e.EsVideo ? [] :
            [
                ("Quitar marcas u objetos…", "\uE771", () => _ = Protegido(() => AbrirEdicionAsync(e.Ruta, HerramientaEdicion.Borrar))),
                ("Ampliar con IA…", "\uE740", () => _ = Protegido(() => AbrirEdicionAsync(e.Ruta, HerramientaEdicion.Ampliar))),
                ("Quitar artefactos y ruido…", "\uE790", () => _ = Protegido(() => AbrirEdicionAsync(e.Ruta, HerramientaEdicion.Limpiar))),
            ],
            _cfg.VisorConInformacion,
            abierto => { _cfg.VisorConInformacion = abierto; ProgramarGuardado(); }));
    }

    private static string NombreTipo(Categoria c) => c switch
    {
        Categoria.Panoramica => "Panorámica",
        Categoria.UnMonitor => "Un monitor",
        _ => "Otra",
    };

    /// <summary>False (y un aviso) si la imagen está en una carpeta que ahora no está conectada.</summary>
    private bool AvisarSiNoDisponible(ImagenBiblioteca img)
    {
        if (img.Disponible) return true;
        MostrarResultado(InfoBarSeverity.Warning, "Imagen no disponible",
            $"«{img.Nombre}» está en una carpeta que ahora no está conectada.");
        return false;
    }

    private void UsarEnZonaSeleccionada(ImagenBiblioteca img)
    {
        if (!AvisarSiNoDisponible(img)) return;
        if (_zonas.FirstOrDefault(z => z.Id == _zonaSeleccionada) is { } zona) Asignar(zona, img.Ruta);
        else MostrarResultado(InfoBarSeverity.Informational, "Selecciona primero una zona del lienzo",
                              "O arrastra la miniatura directamente al monitor que quieras.");
    }

    private void CambiarFavorita(ImagenBiblioteca img)
    {
        try { _repo.PonerFavorita(img.Id, !img.Favorita); }
        catch (Exception ex)
        {
            MostrarResultado(InfoBarSeverity.Error, "No se ha podido cambiar la favorita", ex.Message);
            return;
        }
        TrasCambiarImagen(img.Id, EstadoBib.SeccionActual == SeccionBiblioteca.Favoritas);
    }

    private async Task EtiquetarAsync(ImagenBiblioteca img)
    {
        var existentes = _repo.Etiquetas().Select(e => e.Etiqueta).ToList();
        var dialogo = new DialogoEtiquetas(img, existentes) { XamlRoot = Content.XamlRoot };
        if (await dialogo.MostrarAsync() != ContentDialogResult.Primary) return;
        _repo.PonerEtiquetas(img.Id, dialogo.Etiquetas);
        // La búsqueda por texto y el filtro de etiqueta miran las etiquetas: el listado puede cambiar.
        var e = EstadoBib;
        TrasCambiarImagen(img.Id, e.Etiqueta is not null || e.Texto.Trim().Length > 0);
    }

    /// <summary>Relee la biblioteca tras cambiar una imagen; recarga la cuadrícula solo si el listado puede cambiar.</summary>
    private void TrasCambiarImagen(long id, bool afectaAlListado)
    {
        // Solo se relee esa imagen: releer toda la biblioteca comprueba miles de archivos en disco.
        if (_repo.PorId(id) is { } actualizada)
        {
            _biblioteca = [.. _biblioteca.Select(i => i.Id == id ? actualizada with { Ruta = i.Ruta } : i)];
            if (_biblioteca.FirstOrDefault(i => i.Id == id) is { } enLista) _cuadricula.Actualizar(enLista);
        }
        if (afectaAlListado && !EnHistorial) RefrescarBiblioteca();
        else
        {
            var e = EstadoBib;
            Nav.Mostrar(PestanaActual, SeccionActiva(), ContarSecciones(), EtiquetasDePestana(), e.Etiqueta, e.Color);
        }
    }

    /// <summary>Cuántas imágenes hay en cada sección con los filtros actuales (los números de la navegación).</summary>
    private Dictionary<SeccionBiblioteca, int> ContarSecciones()
    {
        var e = EstadoBib;
        return Enum.GetValues<SeccionBiblioteca>().ToDictionary(s => s,
            s => FiltroBiblioteca.Aplicar(_biblioteca, new CriterioBiblioteca(s, e.Texto, e.Etiqueta, e.Color, PestanaActual), _zonas, _lienzo).Count);
    }
}
