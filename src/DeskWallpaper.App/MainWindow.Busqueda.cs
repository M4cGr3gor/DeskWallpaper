using DeskWallpaper.App.Vistas;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace DeskWallpaper.App;

/// <summary>Búsqueda online de fondos.</summary>
public sealed partial class MainWindow
{
    /// <summary>Imágenes descargadas solo para verlas en el visor (las últimas 30).</summary>
    private const int MaximoTemporalesVisor = 30;

    private BuscadorOnline _buscador = null!;
    private DescargadorImagenes _descargador = null!;
    private BusquedaOnline? _vistaBusqueda;
    private bool _enBusqueda;
    private CancellationTokenSource? _ctsBusqueda;
    private string _textoBuscado = "";
    private IReadOnlyList<SizePx> _resolucionesBuscadas = [];
    private readonly Dictionary<string, int> _paginaPorFuente = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _fuentesConMas = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Imágenes ya mostradas en esta búsqueda: una misma imagen no sale dos veces (en dos páginas o dos fuentes).</summary>
    private readonly HashSet<Uri> _yaMostradas = [];
    /// <summary>Errores de las fuentes en esta búsqueda, para enseñarlos todos juntos y no solo el último.</summary>
    private readonly SortedDictionary<string, string> _erroresFuentes = new(StringComparer.OrdinalIgnoreCase);
    private List<PestanaWeb> _pestanasWeb = [];

    /// <summary>De qué es la vista de búsqueda ahora (imágenes o vídeos): al cambiar de pestaña se rehace.</summary>
    private ContenidoFuente? _contenidoVista;

    private ContenidoFuente ContenidoActual => EnAnimados ? ContenidoFuente.Videos : ContenidoFuente.Imagenes;

    /// <summary>Vídeos vistos en el visor desde la búsqueda: pesan mucho, así que solo se guardan los últimos.</summary>
    private const int MaximoVideosVisor = 3;
    private Task<CoreWebView2Environment>? _entornoNavegador;

    private string CarpetaTemporalesVisor => Path.Combine(_carpetaDatos, "cache", "online");

    private void InicializarBusqueda()
    {
        _buscador = new BuscadorOnline(new HttpClientHandler());
        _descargador = new DescargadorImagenes(new HttpClientHandler(), _renderizador);
        // Lo visto en el visor en la sesión anterior ya no hace falta.
        try { if (Directory.Exists(CarpetaTemporalesVisor)) Directory.Delete(CarpetaTemporalesVisor, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* se recorta al ver la siguiente */ }
    }

    private void CerrarBusqueda() => _ctsBusqueda?.Cancel();

    /// <summary>Las fuentes activas de ese tipo para la pestaña actual (imágenes o fondos animados).</summary>
    private IEnumerable<FuenteBusqueda> FuentesActivas(TipoFuente tipo) =>
        (_cfg.FuentesBusqueda ?? []).Where(f => f.Activa && f.Tipo == tipo && f.Contenido == ContenidoActual);

    /// <summary>Entra en «Buscar online»: la vista ocupa el centro y la columna derecha.</summary>
    private void MostrarBusqueda()
    {
        if (_vistaBusqueda is null)
        {
            _vistaBusqueda = new BusquedaOnline();
            _vistaBusqueda.Buscar += async (_, b) => await Protegido(() => BuscarOnlineAsync(b.Texto, b.Resoluciones));
            _vistaBusqueda.EstadoCambiado += (_, e) => { _cfg.Busqueda = e; ProgramarGuardado(); };
            _vistaBusqueda.Resultados.PedirMas += async (_, _) => await Protegido(PedirMasAsync);
            _vistaBusqueda.Resultados.Ver += async (_, r) => await Protegido(() => VerOnlineAsync(r));
            _vistaBusqueda.Resultados.Descargar += async (_, lista) => await Protegido(() => DescargarOnlineAsync(lista));
            _vistaBusqueda.PestanaCambiada += async (_, contenido) =>
            {
                if (contenido is PestanaWeb web) await Protegido(web.ActivarAsync);
            };
            VistaBusqueda.Content = _vistaBusqueda;
        }
        // La primera vez, o si se entra desde la otra pestaña: sus fuentes son otras.
        if (_contenidoVista != ContenidoActual)
        {
            _contenidoVista = ContenidoActual;
            _ctsBusqueda?.Cancel();
            _textoBuscado = "";
            _vistaBusqueda.Resultados.Empezar(new Dictionary<SizePx, string>());
            _vistaBusqueda.Resultados.Fin(false);
            _vistaBusqueda.Resultados.Avisar(ContenidoActual == ContenidoFuente.Videos
                ? "Escribe qué fondo animado buscas y pulsa Buscar."
                : "Escribe qué buscas y pulsa Buscar.");
            ReconstruirPestanasWeb();
        }
        // Las zonas pueden haber cambiado desde la última vez: las casillas se rehacen al entrar.
        _vistaBusqueda.Configurar(ResolucionesBusqueda.DeZonas(_zonas, _disposicion), _cfg.Busqueda);

        _enBusqueda = true;
        ZonaCentral.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Collapsed;
        Buscador.Visibility = Visibility.Collapsed; // el buscador de arriba es el de la biblioteca
        VistaBusqueda.Visibility = Visibility.Visible;
        RefrescarBiblioteca(); // resalta la sección en la navegación
    }

    private void OcultarBusqueda()
    {
        if (!_enBusqueda) return;
        _enBusqueda = false;
        VistaBusqueda.Visibility = Visibility.Collapsed;
        ZonaCentral.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Visible;
        Buscador.Visibility = Visibility.Visible;
    }

    /// <summary>Una pestaña por fuente web activa, detrás de «Resultados» (también tras cambiar las fuentes en Ajustes).</summary>
    private void ReconstruirPestanasWeb()
    {
        if (_vistaBusqueda is null) return;
        foreach (var vieja in _pestanasWeb) vieja.Cerrar();
        _pestanasWeb = [.. FuentesActivas(TipoFuente.Web).Select(f =>
        {
            var p = new PestanaWeb(f, EntornoNavegador, () => _cfg.CarpetaDescargas);
            p.DescargarImagen += async (_, uri) => await Protegido(() => DescargarDesdeWebAsync(uri, p.Seleccionada));
            // Una descarga normal del navegador puede ser cualquier archivo: se dice cuál, sin llamarlo imagen.
            p.DescargaTerminada += (_, ruta) => MostrarResultado(InfoBarSeverity.Success, "Archivo descargado",
                $"{Path.GetFileName(ruta)} en {Path.GetDirectoryName(ruta)}", ("Abrir carpeta", () => AbrirCarpeta(Path.GetDirectoryName(ruta) ?? _cfg.CarpetaDescargas)));
            p.DescargaFallida += (_, mensaje) => MostrarResultado(InfoBarSeverity.Error, "No se ha podido descargar", mensaje);
            return p;
        })];
        _vistaBusqueda.PonerPestanasWeb(_pestanasWeb.Select(p => (p.NombreFuente, (UIElement)p)));
        if (_textoBuscado.Length > 0)
            foreach (var p in _pestanasWeb) p.Buscar(_textoBuscado, _vistaBusqueda.Marcadas);
    }

    /// <summary>Un solo entorno para todos los navegadores, con sus datos (cookies, caché) dentro de la carpeta de datos.</summary>
    private Task<CoreWebView2Environment> EntornoNavegador() =>
        _entornoNavegador ??= CoreWebView2Environment.CreateWithOptionsAsync(
            "", Path.Combine(_carpetaDatos, "navegador"), new CoreWebView2EnvironmentOptions()).AsTask();

    /// <summary>"Descargar con DeskWallpaper" desde una pestaña web; avisa si es más pequeña que el tamaño elegido.</summary>
    private async Task DescargarDesdeWebAsync(Uri uri, SizePx? elegida)
    {
        MostrarResultado(InfoBarSeverity.Informational, "Descargando…", "");
        try
        {
            var d = await _descargador.DescargarAsync(uri, _cfg.CarpetaDescargas, CancellationToken.None);
            if (elegida is { } e && (d.Tamano.Ancho < e.Ancho || d.Tamano.Alto < e.Alto))
                MostrarResultado(InfoBarSeverity.Warning, "Descargada, pero es pequeña",
                    $"Esta imagen es de {d.Tamano.Ancho}×{d.Tamano.Alto}, menor que {e.Ancho}×{e.Alto}. Si es una miniatura, ábrela en grande antes de descargarla.",
                    ("Abrir carpeta", () => AbrirCarpeta(_cfg.CarpetaDescargas)));
            else
                AvisarDescargas(1, 1, _cfg.CarpetaDescargas, null);
        }
        catch (ExcepcionDescarga ex)
        {
            MostrarResultado(InfoBarSeverity.Error, ex.Message, "");
        }
    }

    /// <summary>Busca en todas las fuentes con API activas a la vez; cada una añade lo suyo según llega.</summary>
    private async Task BuscarOnlineAsync(string texto, IReadOnlyList<SizePx> resoluciones)
    {
        _ctsBusqueda?.Cancel();
        var cts = _ctsBusqueda = new CancellationTokenSource();
        _textoBuscado = texto;
        _resolucionesBuscadas = resoluciones;
        _paginaPorFuente.Clear();
        _yaMostradas.Clear();
        _erroresFuentes.Clear();
        _fuentesConMas.Clear();

        // Las pestañas web se abren con la misma búsqueda (cada una con su selector de tamaño).
        foreach (var p in _pestanasWeb) p.Buscar(texto, _vistaBusqueda!.Marcadas);

        var resultados = _vistaBusqueda!.Resultados;
        // "3840×1080 (2+1)" → "2+1", para que cada miniatura diga para qué casilla encaja.
        resultados.Empezar(ResolucionesBusqueda.DeZonas(_zonas, _disposicion).ToDictionary(
            r => r.Tamano, r => r.Etiqueta[(r.Etiqueta.IndexOf('(') + 1)..].TrimEnd(')')));
        var fuentes = FuentesActivas(TipoFuente.Api).Where(f => !BuscadorOnline.FaltaClave(f)).ToList();
        var sinClave = FuentesActivas(TipoFuente.Api).Where(BuscadorOnline.FaltaClave).Select(f => f.Nombre).ToList();
        if (fuentes.Count == 0)
        {
            resultados.Fin(false);
            resultados.Avisar(sinClave.Count > 0
                ? $"Para ver aquí los resultados de {string.Join(" y ", sinClave)} hace falta tu clave personal (gratis, en su web): " +
                  "escríbela en Ajustes › Búsqueda online. Mientras, busca en sus pestañas de arriba."
                : "No hay fuentes con API activas. Puedes añadirlas en Ajustes › Búsqueda online.");
            return;
        }
        try
        {
            await Task.WhenAll(fuentes.Select(f => PaginaAsync(f, 1, cts.Token)));
        }
        finally
        {
            // Pase lo que pase, la lista no se queda en "Buscando…" ni bloquea "cargar más".
            if (!cts.IsCancellationRequested) resultados.Fin(_fuentesConMas.Count > 0);
        }
    }

    /// <summary>Siguiente página de las fuentes que dijeron tener más.</summary>
    private async Task PedirMasAsync()
    {
        if (_ctsBusqueda is not { } cts || cts.IsCancellationRequested) return;
        var fuentes = FuentesActivas(TipoFuente.Api).Where(f => _fuentesConMas.Contains(f.Nombre)).ToList();
        try
        {
            await Task.WhenAll(fuentes.Select(f => PaginaAsync(f, _paginaPorFuente.GetValueOrDefault(f.Nombre, 1) + 1, cts.Token)));
        }
        finally
        {
            if (!cts.IsCancellationRequested) _vistaBusqueda!.Resultados.Fin(_fuentesConMas.Count > 0);
        }
    }

    private async Task PaginaAsync(FuenteBusqueda f, int pagina, CancellationToken ct)
    {
        ResultadoFuente r;
        try { r = await _buscador.BuscarAsync(f, _textoBuscado, _resolucionesBuscadas, pagina, ct); }
        catch (OperationCanceledException) { return; } // la sustituyó una búsqueda nueva

        if (ct.IsCancellationRequested) return;
        _paginaPorFuente[f.Nombre] = pagina;
        if (r.HayMas) _fuentesConMas.Add(f.Nombre);
        else _fuentesConMas.Remove(f.Nombre);
        _vistaBusqueda!.Resultados.Anadir([.. r.Resultados.Where(e => _yaMostradas.Add(e.Resultado.Imagen))]);
        if (r.Error is not null) _erroresFuentes[f.Nombre] = r.Error;
        else _erroresFuentes.Remove(f.Nombre);
        if (_erroresFuentes.Count > 0)
            MostrarResultado(InfoBarSeverity.Warning,
                _erroresFuentes.Count == 1 ? "Una fuente ha fallado" : $"{_erroresFuentes.Count} fuentes han fallado",
                string.Join(" · ", _erroresFuentes.Values));
    }

    /// <summary>Descarga la imagen (o el vídeo) a la carpeta temporal y la abre en el visor.</summary>
    private async Task VerOnlineAsync(ResultadoEncajado r)
    {
        if (r.Resultado.Archivos is not null || ContenidoActual == ContenidoFuente.Videos)
        {
            await VerVideoOnlineAsync(r);
            return;
        }
        MostrarResultado(InfoBarSeverity.Informational, "Descargando la imagen para verla…", "");
        try
        {
            var d = await _descargador.DescargarAsync(r.Resultado.Imagen, CarpetaTemporalesVisor, CancellationToken.None);
            DescargadorImagenes.RecortarTemporales(CarpetaTemporalesVisor, MaximoTemporalesVisor);
            Estado.Ocultar();
            var (rect, _) = MonitorDeLaVentana();
            await VisorBasico.MostrarAsync(d.Ruta, rect, _renderizador, Activate);
        }
        catch (ExcepcionDescarga ex)
        {
            MostrarResultado(InfoBarSeverity.Error, ex.Message, "");
        }
    }

    /// <summary>Descarga el vídeo (el archivo elegido para la casilla) a temporales y lo abre en el visor, que lo reproduce.</summary>
    private async Task VerVideoOnlineAsync(ResultadoEncajado r)
    {
        var carpeta = Path.Combine(CarpetaTemporalesVisor, "videos");
        try
        {
            var ruta = await _descargador.DescargarVideoAsync(r.Resultado.Imagen, carpeta,
                ProgresoEnEstado("Descargando el vídeo para verlo…"), CancellationToken.None);
            DescargadorImagenes.RecortarTemporales(carpeta, MaximoVideosVisor);
            Estado.Ocultar();
            var (rect, _) = MonitorDeLaVentana();
            await VisorBasico.MostrarAsync(new OpcionesVisor([new ElementoVisor(ruta, EsVideo: true, Duracion: r.Resultado.Duracion)],
                0, rect, _renderizador, Activate));
        }
        catch (ExcepcionDescarga ex)
        {
            MostrarResultado(InfoBarSeverity.Error, ex.Message, "");
        }
    }

    /// <summary>
    /// «Descargando…  45 %» en la barra de estado, como mucho cuatro veces por segundo. A prueba de fallos: una excepción
    /// en una devolución de Progress cerraría la app.
    /// </summary>
    private IProgress<double> ProgresoEnEstado(string texto)
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        TimeSpan? ultimo = null;
        MostrarResultado(InfoBarSeverity.Informational, texto, "");
        return new Progress<double>(f =>
        {
            try
            {
                if (ultimo is { } u && reloj.Elapsed - u < TimeSpan.FromMilliseconds(250) && f < 1) return;
                ultimo = reloj.Elapsed;
                MostrarResultado(InfoBarSeverity.Informational, texto, $"{f:P0}");
            }
            catch (Exception) { /* solo es el aviso del progreso */ }
        });
    }

    /// <summary>Descarga una o varias a la carpeta de descargas, una detrás de otra.</summary>
    private async Task DescargarOnlineAsync(IReadOnlyList<ResultadoEncajado> lista)
    {
        if (lista.Count == 0) return;
        if (ContenidoActual == ContenidoFuente.Videos)
        {
            await DescargarVideosOnlineAsync(lista);
            return;
        }
        var carpeta = _cfg.CarpetaDescargas;
        var hechas = 0;
        string? ultimoError = null;
        foreach (var r in lista)
        {
            MostrarResultado(InfoBarSeverity.Informational, $"Descargando {hechas + 1} de {lista.Count}…", "");
            try
            {
                await _descargador.DescargarAsync(r.Resultado.Imagen, carpeta, CancellationToken.None);
                hechas++;
            }
            catch (ExcepcionDescarga ex)
            {
                ultimoError = ex.Message;
            }
        }
        AvisarDescargas(hechas, lista.Count, carpeta, ultimoError);
    }

    /// <summary>Los vídeos marcados, uno detrás de otro, con su progreso en la barra de estado.</summary>
    private async Task DescargarVideosOnlineAsync(IReadOnlyList<ResultadoEncajado> lista)
    {
        var carpeta = _cfg.CarpetaDescargas;
        var hechas = 0;
        string? ultimoError = null;
        for (var i = 0; i < lista.Count; i++)
        {
            try
            {
                await _descargador.DescargarVideoAsync(lista[i].Resultado.Imagen, carpeta,
                    ProgresoEnEstado($"Descargando vídeo {i + 1} de {lista.Count}…"), CancellationToken.None);
                hechas++;
            }
            catch (ExcepcionDescarga ex)
            {
                ultimoError = ex.Message;
            }
        }
        AvisarDescargas(hechas, lista.Count, carpeta, ultimoError, videos: true);
    }

    /// <summary>"3 imágenes descargadas en …" (o vídeos), con el enlace «Abrir carpeta».</summary>
    private void AvisarDescargas(int hechas, int total, string carpeta, string? error, bool videos = false)
    {
        (string, Action)? abrir = hechas > 0 ? ("Abrir carpeta", () => AbrirCarpeta(carpeta)) : null;
        var texto = videos
            ? hechas == 1 ? "1 vídeo descargado" : $"{hechas} vídeos descargados"
            : hechas == 1 ? "1 imagen descargada" : $"{hechas} imágenes descargadas";
        if (error is null)
            MostrarResultado(InfoBarSeverity.Success, texto, $"en {carpeta}", abrir);
        else
            MostrarResultado(hechas > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Error,
                hechas > 0 ? $"{texto}; {total - hechas} con error" : "No se ha podido descargar", error, abrir);
    }

    private static void AbrirCarpeta(string carpeta) =>
        System.Diagnostics.Process.Start("explorer.exe", $"\"{carpeta}\"")?.Dispose();
}
