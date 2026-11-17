using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using DeskWallpaper.App.IA;
using DeskWallpaper.Nucleo;
using Microsoft.ML.OnnxRuntime;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;

namespace DeskWallpaper.App.Vistas;

public enum HerramientaEdicion { Borrar, Ampliar, Limpiar }

/// <param name="Ruta">Imagen original (nunca se modifica).</param>
/// <param name="Monitor">Monitor donde se abre, en píxeles físicos.</param>
/// <param name="FactorZona">Cuánto se estira la imagen en su zona del lienzo, para proponer ×2 o ×4.</param>
/// <param name="AlGuardar">Se llama con la ruta de cada archivo guardado.</param>
public sealed record OpcionesEdicion(string Ruta, HerramientaEdicion Inicial, global::Windows.Graphics.RectInt32 Monitor,
                                     GestorModelos Modelos, double? FactorZona = null,
                                     Action<string>? AlGuardar = null, Action? AlCerrar = null);

/// <summary>
/// Ventana de edición con IA: quitar marcas con un pincel (LaMa) y ampliar
/// (Real-ESRGAN), con deshacer, cortina antes/después y «Guardar como». El original nunca se toca, y el modelo
/// se libera al cambiar de herramienta y al cerrar la ventana.
/// </summary>
public sealed class VentanaEdicion
{
    private static bool _abierta;

    /// <summary>La ventana abierta, para traerla delante si se pide otra edición.</summary>
    private static VentanaEdicion? _actual;

    private readonly OpcionesEdicion _o;
    private readonly Window _ventana;
    private readonly Grid _raiz;
    private readonly Canvas _lienzo = new() { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 32, 32, 32)) };
    private readonly Image _vista = new() { Stretch = Stretch.Fill };
    private readonly Image _vistaOriginal = new() { Stretch = Stretch.Fill, Visibility = Visibility.Collapsed };
    private readonly Canvas _trazos = new() { IsHitTestVisible = false };
    private readonly CompositeTransform _transformacion = new();
    private readonly Ellipse _puntero = new()
    {
        Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
    };
    private readonly Grid _barraCortina = new() { Width = 28, Visibility = Visibility.Collapsed };
    private readonly Border _etiquetaAntes = Etiqueta("Antes"), _etiquetaDespues = Etiqueta("Después");
    private readonly Slider _pincel = new() { Minimum = 4, Maximum = 200, Value = 40, Width = 160, StepFrequency = 1 };
    private readonly Button _deshacer, _rehacer, _ampliar, _limpiar, _borrar, _guardar, _quitarTrazos;
    private readonly ToggleButton _comparar = new() { Content = "Antes │ Después" };
    private readonly TextBlock _estado = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressRing _trabajando = new() { Width = 18, Height = 18, IsActive = false };

    private HistorialEdicion _historial = null!;
    /// <summary>Trazos sin aplicar; la máscara es la suya.</summary>
    private TrazosPincel _pinceladas = new(1, 1);
    private Mascara _mascara => _pinceladas.Mascara;

    /// <summary>Espacio alrededor de la imagen (DIP) para poder pintar hasta el borde sin rozar las barras.</summary>
    private const double Margen = 40;

    private static readonly SolidColorBrush Rojo = new(ColorHelper.FromArgb(255, 230, 30, 30));
    private IDisposable? _modelo;
    private Task? _trabajo;
    private bool _ocupado, _cerrarSinPreguntar, _cerrada, _espacio;

    /// <summary>Se cancela al cerrar la ventana: corta la ampliación y la descarga en curso.</summary>
    private readonly CancellationTokenSource _cierre = new();

    /// <summary>Para que una conversión lenta de Deshacer/Rehacer no pise a otra más reciente.</summary>
    private int _versionVista;

    /// <summary>Ancho en píxeles de la imagen que la vista está mostrando (para conservar el zoom al ampliar).</summary>
    private int _anchoMostrado;

    // Vista: escala en píxeles de pantalla por píxel de la imagen actual, y posición en DIP.
    private double _escala = 1, _x, _y, _cortina = double.NaN;

    private VentanaEdicion(OpcionesEdicion o)
    {
        _o = o;
        _deshacer = Boton("", "Deshacer", "Deshacer (Ctrl+Z). Si hay trazos sin aplicar, quita el último.");
        _rehacer = Boton("", "Rehacer", "Rehacer (Ctrl+Y)");
        _ampliar = Boton("", "Ampliar…", "Ampliar con IA ×1,5, ×2, ×3, ×4 o personalizado");
        _limpiar = Boton("", "Quitar ruido…", "Quitar artefactos JPEG y ruido con IA (SCUNet)");
        _borrar = new Button { Content = "Borrar", Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 110 };
        _guardar = new Button { Content = "Guardar como…", MinWidth = 130 };
        _quitarTrazos = new Button { Content = "Quitar trazos", IsEnabled = false };
        ToolTipService.SetToolTip(_quitarTrazos, "Quita todo lo pintado sin aplicar");
        var volver = Boton("", "Volver", "Volver (Esc)");

        var herramientas = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children =
            {
                volver, Separador(),
                new TextBlock { Text = "Pincel", VerticalAlignment = VerticalAlignment.Center }, _pincel,
                Separador(), _deshacer, _rehacer, Separador(), _ampliar, _limpiar,
            },
        };
        // Lo que no se ve a simple vista: cada atajo con su tecla dibujada, separado del resto.
        var ayuda = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(16, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Atajo("zoom", "Rueda"),
                Atajo("mover", "Espacio + arrastrar"),
                Atajo("pincel más pequeño / más grande", "[", "]"),
                Atajo("deshacer / rehacer", "Ctrl+Z", "Ctrl+Y"),
            },
        };
        ToolTipService.SetToolTip(ayuda,
            "Rueda del ratón: zoom. Espacio + arrastrar (o arrastrar con el botón central): mover la imagen. " +
            "[ y ]: pincel más pequeño o más grande. Ctrl+Z y Ctrl+Y: deshacer y rehacer.");
        var superior = new Grid { Padding = new Thickness(12, 8, 12, 8) };
        superior.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        superior.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(ayuda, 1);
        superior.Children.Add(herramientas);
        superior.Children.Add(ayuda);
        ToolTipService.SetToolTip(_pincel, "Tamaño del pincel en píxeles de la imagen ([ y ])");

        var inferior = new Grid { Padding = new Thickness(12, 8, 12, 10), ColumnSpacing = 10 };
        inferior.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inferior.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inferior.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inferior.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inferior.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inferior.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_trabajando, 1);
        Grid.SetColumn(_estado, 2);
        Grid.SetColumn(_quitarTrazos, 3);
        Grid.SetColumn(_borrar, 4);
        Grid.SetColumn(_guardar, 5);
        inferior.Children.Add(_comparar);
        inferior.Children.Add(_trabajando);
        inferior.Children.Add(_estado);
        inferior.Children.Add(_quitarTrazos);
        inferior.Children.Add(_borrar);
        inferior.Children.Add(_guardar);

        _trazos.RenderTransform = _transformacion;
        _trazos.Opacity = 0.5;   // pincel opaco y capa al 50 %: los trazos que se solapan no se oscurecen
        ConstruirCortina();
        _lienzo.Children.Add(_vista);
        _lienzo.Children.Add(_vistaOriginal);
        _lienzo.Children.Add(_trazos);
        _lienzo.Children.Add(_puntero);
        _lienzo.Children.Add(_barraCortina);
        _lienzo.Children.Add(_etiquetaAntes);
        _lienzo.Children.Add(_etiquetaDespues);

        _raiz = new Grid { KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden };
        _raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_lienzo, 1);
        Grid.SetRow(inferior, 2);
        _raiz.Children.Add(superior);
        _raiz.Children.Add(_lienzo);
        _raiz.Children.Add(inferior);

        _ventana = new Window { Content = _raiz, Title = $"Editar — {System.IO.Path.GetFileName(o.Ruta)}", SystemBackdrop = new MicaBackdrop() };
        _ventana.AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Recursos", "DeskWallpaper.ico"));

        volver.Click += async (_, _) => await PedirCierreAsync();
        _deshacer.Click += (_, _) => Deshacer();
        _rehacer.Click += (_, _) => Rehacer();
        _borrar.Click += async (_, _) => await Protegido(BorrarAsync);
        _ampliar.Click += async (_, _) => await Protegido(() => AmpliarAsync(cerrarSiSeCancela: false));
        _limpiar.Click += async (_, _) => await Protegido(() => LimpiarAsync(cerrarSiSeCancela: false));
        _guardar.Click += async (_, _) => await Protegido(GuardarAsync);
        _quitarTrazos.Click += (_, _) => LimpiarTrazos();
        _comparar.Checked += (_, _) => { _cortina = double.NaN; Pintar(); };
        _comparar.Unchecked += (_, _) => Pintar();
        _pincel.ValueChanged += (_, _) => ActualizarPuntero(null);

        ConectarRaton();
        ConectarTeclado();
        _lienzo.SizeChanged += (_, e) =>
        {
            _lienzo.Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            Ajustar();
        };
        _ventana.AppWindow.Closing += AlPedirCierre;
        _ventana.Closed += async (_, _) =>
        {
            try { await AlCerrarAsync(); }
            catch (Exception ex) { App.RegistrarError(ex); }
        };
    }

    /// <summary>Abre la ventana (una sola a la vez). Lanza <see cref="ExcepcionImagen"/> si no se puede leer la imagen.</summary>
    public static async Task AbrirAsync(OpcionesEdicion o)
    {
        if (_abierta)
        {
            // Ya hay una abierta (solo una a la vez): se trae delante en vez de no hacer nada.
            _actual?.TraerDelante();
            return;
        }
        _abierta = true;
        try
        {
            var original = await Task.Run(() => ArchivoImagen.Leer(o.Ruta));
            var editor = new VentanaEdicion(o);
            _actual = editor;
            await editor.MostrarAsync(original);
        }
        catch
        {
            _abierta = false;
            throw;
        }
    }

    private void TraerDelante()
    {
        if (_cerrada) return;
        if (_ventana.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        _ventana.Activate();
    }

    private async Task MostrarAsync(ImagenRgba original)
    {
        _historial = new HistorialEdicion(original);
        _pinceladas = new TrazosPincel(original.Ancho, original.Alto);
        _anchoMostrado = original.Ancho;
        _vistaOriginal.Source = await BitmapDeAsync(original);
        _vista.Source = _vistaOriginal.Source;

        var m = _o.Monitor;
        _ventana.AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(m.X + 40, m.Y + 40, Math.Max(800, m.Width - 80), Math.Max(600, m.Height - 80)));
        if (_ventana.AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
        _ventana.Activate();
        await Cargada();

        Actualizar();
        if (_o.Inicial == HerramientaEdicion.Ampliar)
        {
            await Protegido(() => AmpliarAsync(cerrarSiSeCancela: true));
        }
        else if (_o.Inicial == HerramientaEdicion.Limpiar)
        {
            await Protegido(() => LimpiarAsync(cerrarSiSeCancela: true));
        }
        else
        {
            if (!await Protegido(() => CargarModeloAsync(ModelosIA.Lama))) Cerrar(sinPreguntar: true);
            else Estado("Pinta encima de la marca o el objeto y pulsa «Borrar».");
        }
    }

    private Task Cargada()
    {
        if (_raiz.XamlRoot is not null && _lienzo.ActualWidth > 0) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        void Hecho(object s, RoutedEventArgs e) { _raiz.Loaded -= Hecho; tcs.TrySetResult(); }
        _raiz.Loaded += Hecho;
        return tcs.Task;
    }

    // ── Modelos ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Deja cargado el modelo pedido, liberando antes el otro (nunca hay dos en memoria). False si el usuario
    /// no quiere descargarlo.
    /// </summary>
    private async Task<bool> CargarModeloAsync(ModeloIA modelo)
    {
        var yaCargado = modelo == ModelosIA.Lama ? _modelo is RellenoLama
            : modelo == ModelosIA.Scunet ? _modelo is LimpiezaScunet
            : _modelo is AmpliacionEsrgan;
        if (yaCargado) return true;
        if (!await DialogosIA.AsegurarModeloAsync(_raiz.XamlRoot, _o.Modelos, modelo, _cierre.Token) || _cerrada) return false;

        _modelo?.Dispose();
        _modelo = null;
        var ruta = _o.Modelos.Ruta(modelo);
        await Ocupado("Preparando la IA…", async () =>
        {
            try
            {
                // Se registra como trabajo: al cerrar se espera a que termine y no queda un modelo suelto.
                var nuevo = modelo == ModelosIA.Lama ? await Trabajo<IDisposable>(() => new RellenoLama(ruta))
                    : modelo == ModelosIA.Scunet ? await Trabajo<IDisposable>(() => new LimpiezaScunet(ruta))
                    : await Trabajo<IDisposable>(() => AmpliacionEsrgan.Crear(ruta));
                if (_cerrada) nuevo.Dispose();
                else _modelo = nuevo;
            }
            catch (OnnxRuntimeException ex)
            {
                throw new ExcepcionEdicion("No se ha podido cargar el modelo de IA. Si el problema sigue, borra la carpeta «modelos» de los datos del programa para que se descargue de nuevo.", ex);
            }
        });
        if (_cerrada || _cierre.IsCancellationRequested) return false;
        Estado(_modelo is AmpliacionEsrgan { UsaGrafica: false } ? "Sin tarjeta gráfica compatible: irá más lento" : "");
        return true;
    }

    // ── Borrar ──────────────────────────────────────────────────────────────

    private async Task BorrarAsync()
    {
        if (_mascara.Vacia || _ocupado) return;
        if (!await CargarModeloAsync(ModelosIA.Lama) || _modelo is not RellenoLama lama) return;

        var imagen = _historial.Actual;
        var mascara = _mascara;
        ResultadoBorrado? r = null;
        await Ocupado("Quitando lo pintado…", async () => r = await Trabajo(() => Borrador.Borrar(imagen, mascara, lama)));
        if (r is null || _cerrada) return;

        _historial.Anadir(new PasoEdicion(TipoPaso.Borrado, r.Imagen));
        LimpiarTrazos();
        await MostrarActualAsync();
        Estado(r.ZonaGrande
            ? "En zonas grandes el resultado puede quedar borroso. Compara con «Antes │ Después»."
            : "Listo. Compara con «Antes │ Después» y guarda con «Guardar como…».");
    }

    // ── Ampliar ─────────────────────────────────────────────────────────────

    private async Task AmpliarAsync(bool cerrarSiSeCancela)
    {
        if (_ocupado) return;
        var tamano = _historial.Actual.Tamano;
        if (Ampliador.FactorMaximo(tamano) < 1.1)
        {
            await DialogosIA.AvisarAsync(_raiz.XamlRoot, "Ampliar con IA", "La imagen ya es demasiado grande para ampliarla.");
            if (cerrarSiSeCancela) Cerrar(sinPreguntar: true);
            return;
        }
        // El factor de la zona se refiere al original: si ya se ha ampliado, lo que falta es menos.
        var factorZona = _o.FactorZona / _historial.FactorTotal;
        var factor = await DialogosIA.PedirFactorAsync(_raiz.XamlRoot, tamano, factorZona);
        if (_cerrada) return;
        if (factor is not { } f || !await CargarModeloAsync(ModelosIA.RealEsrgan) || _modelo is not AmpliacionEsrgan esr)
        {
            if (cerrarSiSeCancela && _historial is { HayCambiosSinGuardar: false }) Cerrar(sinPreguntar: true);
            return;
        }

        var imagen = _historial.Actual;
        var nombreFactor = ArchivoImagen.TextoFactor(f);
        var (resultado, sinMemoria) = await ProgresoIAAsync("Ampliando con IA",
            Ampliador.Bloques(tamano, esr.Bloque), (bloque, total) => $"Ampliando ×{nombreFactor}: bloque {bloque} de {total}",
            () => !esr.UsaGrafica, (progreso, ct) => Ampliador.Ampliar(imagen, esr, f, progreso, ct));

        if (_cerrada) return;
        if (sinMemoria)
            await DialogosIA.AvisarAsync(_raiz.XamlRoot, "No hay memoria suficiente",
                $"No hay memoria suficiente para ampliar ×{nombreFactor} esta imagen. Prueba con un factor menor o con una imagen más pequeña.");
        if (resultado is null)
        {
            if (!sinMemoria) Estado("Ampliación cancelada: la imagen sigue como estaba.");
            if (cerrarSiSeCancela && _historial is { HayCambiosSinGuardar: false }) Cerrar(sinPreguntar: true);
            return;
        }
        _historial.Anadir(new PasoEdicion(TipoPaso.Ampliado, resultado, f));
        await MostrarActualAsync();
        _comparar.IsChecked = true;
        Estado((esr.UsaGrafica ? "" : "Se ha hecho sin tarjeta gráfica. ") +
               "Listo. Compara con «Antes │ Después» y guarda con «Guardar como…».");
    }

    // ── Quitar ruido ────────────────────────────────────────────────────────

    /// <summary>Quita artefactos JPEG y ruido de la imagen entera con SCUNet, siempre en el procesador.</summary>
    private async Task LimpiarAsync(bool cerrarSiSeCancela)
    {
        if (_ocupado) return;
        if (!await CargarModeloAsync(ModelosIA.Scunet) || _modelo is not LimpiezaScunet scunet)
        {
            if (cerrarSiSeCancela && _historial is { HayCambiosSinGuardar: false }) Cerrar(sinPreguntar: true);
            return;
        }

        var imagen = _historial.Actual;
        var (resultado, sinMemoria) = await ProgresoIAAsync("Quitando artefactos y ruido",
            Limpiador.Bloques(imagen.Tamano), (bloque, total) => $"Quitando ruido: bloque {bloque} de {total}",
            () => false, (progreso, ct) => Limpiador.Limpiar(imagen, scunet, progreso, ct));

        if (_cerrada) return;
        if (sinMemoria)
            await DialogosIA.AvisarAsync(_raiz.XamlRoot, "No hay memoria suficiente", "No hay memoria suficiente para limpiar esta imagen.");
        if (resultado is null)
        {
            if (!sinMemoria) Estado("Limpieza cancelada: la imagen sigue como estaba.");
            if (cerrarSiSeCancela && _historial is { HayCambiosSinGuardar: false }) Cerrar(sinPreguntar: true);
            return;
        }
        _historial.Anadir(new PasoEdicion(TipoPaso.Limpiado, resultado));
        await MostrarActualAsync();
        _comparar.IsChecked = true;
        Estado("Listo. Compara con «Antes │ Después» y guarda con «Guardar como…».");
    }

    /// <summary>
    /// Trabajo largo de la IA con un diálogo de progreso: barra, «bloque 3 de 40 · quedan unos 25 s» y «Cancelar».
    /// Se cancela con el botón, con Esc (cualquier cierre del diálogo antes de terminar) y al cerrar la ventana.
    /// Devuelve null si se cancela o no hay memoria (y lo indica).
    /// </summary>
    private async Task<(ImagenRgba? Resultado, bool SinMemoria)> ProgresoIAAsync(string titulo, int total,
        Func<int, int, string> textoBloque, Func<bool> sinGrafica, Func<IProgress<double>, CancellationToken, ImagenRgba> trabajo)
    {
        var barra = new ProgressBar { Maximum = 1 };
        var texto = new TextBlock { Text = textoBloque(1, total), TextWrapping = TextWrapping.Wrap };
        var aviso = new TextBlock
        {
            Text = "Sin tarjeta gráfica compatible: irá más lento",
            Visibility = sinGrafica() ? Visibility.Visible : Visibility.Collapsed,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        var dialogo = new ContentDialog
        {
            XamlRoot = _raiz.XamlRoot,
            Title = titulo,
            Content = new StackPanel { Spacing = 10, MinWidth = 420, Children = { texto, barra, aviso } },
            CloseButtonText = "Cancelar",
        };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cierre.Token);
        var terminado = false;
        dialogo.Closing += (_, _) => { if (!terminado) cts.Cancel(); };
        var reloj = Stopwatch.StartNew();
        var progreso = new Progress<double>(fr =>
        {
            if (_cerrada) return;
            barra.Value = fr;
            var hechos = (int)Math.Round(fr * total);
            var resto = hechos > 0 ? TimeSpan.FromTicks((long)(reloj.Elapsed.Ticks * (double)(total - hechos) / hechos)) : (TimeSpan?)null;
            texto.Text = textoBloque(Math.Min(total, hechos + 1), total)
                         + (resto is { } t && hechos < total ? $" · quedan {DialogosIA.Duracion(t)}" : "");
            aviso.Visibility = sinGrafica() ? Visibility.Visible : Visibility.Collapsed;
        });

        ImagenRgba? resultado = null;
        var sinMemoria = false;
        _ocupado = true;
        Actualizar();
        Task? mostrado = null;
        try
        {
            mostrado = dialogo.MostrarAsync().AsTask();
            resultado = await Trabajo(() => trabajo(progreso, cts.Token));
        }
        catch (OperationCanceledException)
        {
            // Cancelado: la imagen sigue como estaba.
        }
        catch (OutOfMemoryException)
        {
            sinMemoria = true;
        }
        finally
        {
            terminado = true;
            _ocupado = false;
            if (!_cerrada)
            {
                try
                {
                    dialogo.Hide();
                    if (mostrado is not null) await mostrado;
                }
                catch (Exception) { /* el diálogo no llegó a abrirse */ }
                Actualizar();
            }
        }
        if (sinMemoria) Estado("");
        return (resultado, sinMemoria);
    }

    // ── Guardar y cerrar ────────────────────────────────────────────────────

    private async Task GuardarAsync()
    {
        if (_ocupado) return;
        var propuesto = ArchivoImagen.NombrePropuesto(_o.Ruta, _historial.HayRetoque, _historial.FactorTotal);
        var extension = System.IO.Path.GetExtension(_o.Ruta).ToLowerInvariant();
        while (true)
        {
            var selector = new Microsoft.Windows.Storage.Pickers.FileSavePicker(_ventana.AppWindow.Id)
            {
                SuggestedFolder = System.IO.Path.GetDirectoryName(propuesto)!,
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(propuesto),
                DefaultFileExtension = extension,
            };
            selector.FileTypeChoices.Add($"Imagen {extension.TrimStart('.').ToUpperInvariant()}", [extension]);
            var elegido = await selector.PickSaveFileAsync();
            if (elegido is null) return;

            var imagen = _historial.Actual;
            try
            {
                await Ocupado("Guardando…", () => Trabajo(() => ArchivoImagen.Guardar(imagen, elegido.Path, _o.Ruta)));
            }
            catch (ExcepcionEdicion ex) when (ex.EsElOriginal)
            {
                Estado("");
                await DialogosIA.AvisarAsync(_raiz.XamlRoot, "Elige otro nombre",
                    "No se puede guardar encima del original: el original nunca se modifica.");
                continue;
            }
            _historial.MarcarGuardado();
            Estado($"Guardada en {elegido.Path}");
            Actualizar();
            _o.AlGuardar?.Invoke(elegido.Path);
            return;
        }
    }

    /// <summary>La × de la ventana. Window.Close() no pasa por aquí: «Volver» y Esc usan <see cref="PedirCierreAsync"/>.</summary>
    private async void AlPedirCierre(AppWindow sender, AppWindowClosingEventArgs e)
    {
        // Con algo en marcha (la IA, una descarga, su diálogo) la ventana no se puede destruir todavía: tocar después
        // un diálogo de una ventana ya cerrada tumba el proceso entero (0xc0000005, comprobado en real). Se cancela lo
        // que haya, con la ventana aún viva, y se cierra en cuanto termina (preguntando si hay cambios sin guardar).
        if (_enCurso > 0 && !_cerrada)
        {
            e.Cancel = true;
            _cerrarAlTerminar = true;
            Estado("Cerrando…");
            _cierre.Cancel();
            // Los diálogos que no dependen de la cancelación (elegir el tamaño, avisos) se cierran también ahora.
            foreach (var p in VisualTreeHelper.GetOpenPopupsForXamlRoot(_raiz.XamlRoot))
                if (p.Child is ContentDialog d)
                {
                    try { d.Hide(); } catch (Exception) { /* ya se estaba cerrando */ }
                }
            return;
        }
        if (_cerrarSinPreguntar || _historial is null || !_historial.HayCambiosSinGuardar) return;
        e.Cancel = true;
        try { await PedirCierreAsync(); }
        catch (Exception ex) { App.RegistrarError(ex); Cerrar(sinPreguntar: true); }
    }

    /// <summary>Cierra la ventana; si hay cambios sin guardar, antes pregunta «¿Descartar los cambios?».</summary>
    private async Task PedirCierreAsync()
    {
        if (_cerrarSinPreguntar || _historial is null || !_historial.HayCambiosSinGuardar)
        {
            Cerrar(sinPreguntar: true);
            return;
        }
        var dialogo = new ContentDialog
        {
            XamlRoot = _raiz.XamlRoot,
            Title = "¿Descartar los cambios?",
            Content = new TextBlock { Text = "La imagen editada no se ha guardado. El original no ha cambiado.", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Descartar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            if (await dialogo.MostrarAsync() == ContentDialogResult.Primary) Cerrar(sinPreguntar: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // Ya había otro diálogo abierto: el usuario lo cierra y vuelve a intentarlo.
        }
    }

    private void Cerrar(bool sinPreguntar)
    {
        _cerrarSinPreguntar |= sinPreguntar;
        try { _ventana.Close(); } catch (COMException) { /* ya se estaba cerrando */ }
    }

    private async Task AlCerrarAsync()
    {
        if (_cerrada) return;
        _cerrada = true;
        _cierre.Cancel();
        // Si la IA estaba trabajando, se espera a que acabe ese paso antes de liberar el modelo.
        if (_trabajo is { } t)
        {
            try { await t; } catch (Exception) { /* da igual: la ventana ya se ha cerrado */ }
        }
        _modelo?.Dispose();
        _modelo = null;
        _historial = null!;
        _abierta = false;
        if (ReferenceEquals(_actual, this)) _actual = null;
        // Las imágenes del historial pueden ocupar cientos de MB: se devuelven ya, no cuando le toque al recolector.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        _o.AlCerrar?.Invoke();
    }

    // ── Deshacer ────────────────────────────────────────────────────────────

    private void Deshacer()
    {
        if (_ocupado) return;
        if (_pinceladas.QuitarUltimo())
        {
            RedibujarTrazos();
            Actualizar();
            return;
        }
        if (!_historial.PuedeDeshacer) return;
        _historial.Deshacer();
        _ = MostrarActualAsync();
    }

    private void Rehacer()
    {
        if (_ocupado || !_historial.PuedeRehacer) return;
        _historial.Rehacer();
        _ = MostrarActualAsync();
    }

    private async Task MostrarActualAsync()
    {
        if (_cerrada) return;
        var version = ++_versionVista;
        var actual = _historial.Actual;
        if (_anchoMostrado != actual.Ancho)
        {
            // Misma vista en pantalla con más (o menos) píxeles. Se ajusta ya, sin esperar a la conversión.
            _escala *= _anchoMostrado / (double)actual.Ancho;
            _anchoMostrado = actual.Ancho;
        }
        if (_mascara.Ancho != actual.Ancho || _mascara.Alto != actual.Alto) _pinceladas = new TrazosPincel(actual.Ancho, actual.Alto);
        LimpiarTrazos();
        var fuente = ReferenceEquals(actual, _historial.Original) ? _vistaOriginal.Source : await BitmapDeAsync(actual);
        // Otro Deshacer/Rehacer más reciente ya ha cambiado la imagen: esta conversión llega tarde.
        if (_cerrada || version != _versionVista) return;
        _vista.Source = fuente;
        Pintar();
        Actualizar();
    }

    // ── Vista: zoom, desplazamiento, pincel y cortina ───────────────────────

    private double Raster() => _raiz.XamlRoot?.RasterizationScale ?? 1;
    private ImagenRgba Img => _historial.Actual;
    private double AjusteEscala() => Math.Min(Math.Max(1, _lienzo.ActualWidth - 2 * Margen) * Raster() / Img.Ancho,
                                              Math.Max(1, _lienzo.ActualHeight - 2 * Margen) * Raster() / Img.Alto);
    private double AnchoDip => Img.Ancho * _escala / Raster();
    private double AltoDip => Img.Alto * _escala / Raster();

    private void Ajustar()
    {
        if (_historial is null || _lienzo.ActualWidth <= 0) return;
        _escala = AjusteEscala();
        Pintar();
    }

    private void Pintar()
    {
        if (_historial is null) return;
        var ancho = AnchoDip;
        var alto = AltoDip;
        // Cabe: centrada. No cabe: se puede mover hasta dejar el margen en cada lado, y no más.
        _x = ancho <= _lienzo.ActualWidth - 2 * Margen ? (_lienzo.ActualWidth - ancho) / 2
                                                       : Math.Clamp(_x, _lienzo.ActualWidth - ancho - Margen, Margen);
        _y = alto <= _lienzo.ActualHeight - 2 * Margen ? (_lienzo.ActualHeight - alto) / 2
                                                       : Math.Clamp(_y, _lienzo.ActualHeight - alto - Margen, Margen);
        foreach (var img in new[] { _vista, _vistaOriginal })
        {
            img.Width = ancho;
            img.Height = alto;
            Canvas.SetLeft(img, _x);
            Canvas.SetTop(img, _y);
        }
        var s = _escala / Raster();
        _transformacion.ScaleX = _transformacion.ScaleY = s;
        _transformacion.TranslateX = _x;
        _transformacion.TranslateY = _y;

        var comparando = _comparar.IsChecked == true;
        _vistaOriginal.Visibility = comparando ? Visibility.Visible : Visibility.Collapsed;
        _barraCortina.Visibility = _etiquetaAntes.Visibility = _etiquetaDespues.Visibility = _vistaOriginal.Visibility;
        _trazos.Visibility = comparando ? Visibility.Collapsed : Visibility.Visible;
        if (comparando)
        {
            if (double.IsNaN(_cortina)) _cortina = _x + ancho / 2;
            _cortina = Math.Clamp(_cortina, Math.Max(0, _x), Math.Min(_lienzo.ActualWidth, _x + ancho));
            _vistaOriginal.Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, Math.Max(0, _cortina - _x), alto) };
            _barraCortina.Height = _lienzo.ActualHeight;
            Canvas.SetLeft(_barraCortina, _cortina - _barraCortina.Width / 2);
            // «Antes» a la izquierda de la cortina y «Después» a la derecha, arriba de la imagen.
            var arriba = Math.Max(0, _y) + 12;
            _etiquetaAntes.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(_etiquetaAntes, _cortina - 10 - _etiquetaAntes.DesiredSize.Width);
            Canvas.SetTop(_etiquetaAntes, arriba);
            Canvas.SetLeft(_etiquetaDespues, _cortina + 10);
            Canvas.SetTop(_etiquetaDespues, arriba);
        }
    }

    private void Zoom(double nueva, double px, double py)
    {
        nueva = Math.Clamp(nueva, Math.Min(AjusteEscala(), 1), Math.Max(VisorBasico.ZoomMaximo, AjusteEscala()));
        var f = nueva / _escala;
        _x = px - (px - _x) * f;
        _y = py - (py - _y) * f;
        if (!double.IsNaN(_cortina)) _cortina = px - (px - _cortina) * f;
        _escala = nueva;
        Pintar();
        ActualizarPuntero(null);
    }

    /// <summary>De DIP del lienzo a píxeles de la imagen actual.</summary>
    private (double X, double Y) AImagen(global::Windows.Foundation.Point p) =>
        ((p.X - _x) * Raster() / _escala, (p.Y - _y) * Raster() / _escala);

    private bool Pintando => _comparar.IsChecked != true && !_ocupado;

    private void ConectarRaton()
    {
        global::Windows.Foundation.Point? arrastre = null;
        (double X, double Y)? ultimo = null;

        _lienzo.PointerWheelChanged += (_, e) =>
        {
            var p = e.GetCurrentPoint(_lienzo);
            Zoom(_escala * (p.Properties.MouseWheelDelta > 0 ? 1.25 : 0.8), p.Position.X, p.Position.Y);
            e.Handled = true;
        };
        _lienzo.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(_lienzo);
            var mover = p.Properties.IsMiddleButtonPressed || (p.Properties.IsLeftButtonPressed && (_espacio || !Pintando));
            if (mover)
            {
                arrastre = p.Position;
            }
            else if (p.Properties.IsLeftButtonPressed && Pintando)
            {
                var (ix, iy) = AImagen(p.Position);
                _pinceladas.Empezar(ix, iy, _pincel.Value / 2);
                Dibujar(new SegmentoTrazo(ix, iy, ix, iy, _pincel.Value / 2));
                ultimo = (ix, iy);
            }
            else return;
            _lienzo.CapturePointer(e.Pointer);
        };
        _lienzo.PointerMoved += (_, e) =>
        {
            var punto = e.GetCurrentPoint(_lienzo);
            var p = punto.Position;
            // A veces llega un movimiento suelto, sin botón y con una posición antigua (p. ej. justo tras
            // CapturePointer): no debe pintar una raya hasta allí.
            if ((arrastre is not null || ultimo is not null)
                && !punto.Properties.IsLeftButtonPressed && !punto.Properties.IsMiddleButtonPressed) return;
            ActualizarPuntero(p);
            if (arrastre is { } a)
            {
                _x += p.X - a.X;
                _y += p.Y - a.Y;
                if (!double.IsNaN(_cortina)) _cortina += p.X - a.X;
                arrastre = p;
                Pintar();
            }
            else if (ultimo is { } u)
            {
                var (ix, iy) = AImagen(p);
                _pinceladas.Seguir(ix, iy);
                Dibujar(new SegmentoTrazo(u.X, u.Y, ix, iy, _pincel.Value / 2));
                ultimo = (ix, iy);
            }
        };
        void Soltar()
        {
            arrastre = null;
            if (ultimo is not null) { ultimo = null; _pinceladas.Terminar(); Actualizar(); }
        }
        _lienzo.PointerReleased += (_, e) => { Soltar(); _lienzo.ReleasePointerCapture(e.Pointer); };
        _lienzo.PointerCaptureLost += (_, _) => Soltar();
        _lienzo.PointerExited += (_, _) => _puntero.Visibility = Visibility.Collapsed;
    }

    /// <summary>Dibuja un tramo del pincel en rojo (la capa entera va al 50 %).</summary>
    private void Dibujar(SegmentoTrazo s)
    {
        if (s.X0 == s.X1 && s.Y0 == s.Y1)
        {
            var punto = new Ellipse { Width = 2 * s.Radio, Height = 2 * s.Radio, Fill = Rojo };
            Canvas.SetLeft(punto, s.X0 - s.Radio);
            Canvas.SetTop(punto, s.Y0 - s.Radio);
            _trazos.Children.Add(punto);
        }
        else
        {
            _trazos.Children.Add(new Line
            {
                X1 = s.X0, Y1 = s.Y0, X2 = s.X1, Y2 = s.Y1, Stroke = Rojo, StrokeThickness = 2 * s.Radio,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        }
    }

    private void RedibujarTrazos()
    {
        _trazos.Children.Clear();
        foreach (var segmento in _pinceladas.Trazos.SelectMany(t => t)) Dibujar(segmento);
    }

    private void LimpiarTrazos()
    {
        _trazos.Children.Clear();
        _pinceladas.Limpiar();
        Actualizar();
    }

    /// <summary>Círculo del tamaño del pincel bajo el ratón.</summary>
    private void ActualizarPuntero(global::Windows.Foundation.Point? p)
    {
        if (p is { } punto) { Canvas.SetLeft(_puntero, punto.X - _puntero.Width / 2); Canvas.SetTop(_puntero, punto.Y - _puntero.Height / 2); }
        var diametro = _pincel.Value * _escala / Raster();
        var centroX = Canvas.GetLeft(_puntero) + _puntero.Width / 2;
        var centroY = Canvas.GetTop(_puntero) + _puntero.Height / 2;
        _puntero.Width = _puntero.Height = Math.Max(2, diametro);
        Canvas.SetLeft(_puntero, centroX - _puntero.Width / 2);
        Canvas.SetTop(_puntero, centroY - _puntero.Height / 2);
        _puntero.Visibility = Pintando && !_espacio && p is not null ? Visibility.Visible : _puntero.Visibility;
        if (!Pintando || _espacio) _puntero.Visibility = Visibility.Collapsed;
    }

    private void ConstruirCortina()
    {
        var linea = new Border { Width = 2, Background = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center };
        var asa = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon { Glyph = "", FontSize = 12, Foreground = new SolidColorBrush(Colors.Black) },
        };
        _barraCortina.Children.Add(linea);
        _barraCortina.Children.Add(asa);
        _barraCortina.Background = new SolidColorBrush(Colors.Transparent);

        double? agarre = null;
        _barraCortina.PointerPressed += (_, e) =>
        {
            agarre = e.GetCurrentPoint(_lienzo).Position.X - _cortina;
            _barraCortina.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        _barraCortina.PointerMoved += (_, e) =>
        {
            if (agarre is not { } a) return;
            _cortina = e.GetCurrentPoint(_lienzo).Position.X - a;
            Pintar();
            e.Handled = true;
        };
        _barraCortina.PointerReleased += (_, e) => { agarre = null; _barraCortina.ReleasePointerCapture(e.Pointer); e.Handled = true; };
        _barraCortina.PointerCaptureLost += (_, _) => agarre = null;
    }

    private void ConectarTeclado()
    {
        void Tecla(global::Windows.System.VirtualKey tecla, global::Windows.System.VirtualKeyModifiers mod, Action accion)
        {
            var k = new KeyboardAccelerator { Key = tecla, Modifiers = mod };
            k.Invoked += (_, e) => { e.Handled = true; accion(); };
            _raiz.KeyboardAccelerators.Add(k);
        }
        const global::Windows.System.VirtualKeyModifiers ctrl = global::Windows.System.VirtualKeyModifiers.Control;
        const global::Windows.System.VirtualKeyModifiers nada = global::Windows.System.VirtualKeyModifiers.None;
        Tecla(global::Windows.System.VirtualKey.Z, ctrl, Deshacer);
        Tecla(global::Windows.System.VirtualKey.Y, ctrl, Rehacer);
        Tecla(global::Windows.System.VirtualKey.Escape, nada, () => _ = PedirCierreAsync());

        // [ y ]: tamaño del pincel (como en Photoshop). Por carácter, para que funcione con cualquier teclado.
        _raiz.CharacterReceived += (_, e) =>
        {
            if (e.Character == '[') _pincel.Value = Math.Max(_pincel.Minimum, _pincel.Value / 1.25);
            else if (e.Character == ']') _pincel.Value = Math.Min(_pincel.Maximum, _pincel.Value * 1.25);
        };
        // Barra espaciadora pulsada: arrastrar mueve la imagen en vez de pintar.
        _raiz.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Space) return;
            if (FocusManager.GetFocusedElement(_raiz.XamlRoot) is ButtonBase or ToggleSwitch or TextBox) return;
            _espacio = true;
            _puntero.Visibility = Visibility.Collapsed;
            e.Handled = true;
        };
        _raiz.PreviewKeyUp += (_, e) =>
        {
            // Solo si la pulsación fue para mover la imagen: si no, el botón con el foco se pulsa al soltar la tecla.
            if (e.Key != global::Windows.System.VirtualKey.Space || !_espacio) return;
            _espacio = false;
            e.Handled = true;
        };
    }

    // ── Utilidades ──────────────────────────────────────────────────────────

    private void Actualizar()
    {
        if (_historial is null || _cerrada) return;
        _deshacer.IsEnabled = !_ocupado && (_pinceladas.Cantidad > 0 || _historial.PuedeDeshacer);
        _quitarTrazos.IsEnabled = !_ocupado && _pinceladas.Cantidad > 0;
        _rehacer.IsEnabled = !_ocupado && _historial.PuedeRehacer;
        _borrar.IsEnabled = !_ocupado && !_mascara.Vacia && _comparar.IsChecked != true;
        _ampliar.IsEnabled = !_ocupado;
        _limpiar.IsEnabled = !_ocupado;
        _guardar.IsEnabled = !_ocupado && _historial.HayCambiosSinGuardar;
        _comparar.IsEnabled = !_ocupado && !ReferenceEquals(_historial.Actual, _historial.Original);
        if (!_comparar.IsEnabled && _comparar.IsChecked == true) _comparar.IsChecked = false;
        _trabajando.IsActive = _ocupado;
    }

    private void Estado(string texto)
    {
        if (!_cerrada) _estado.Text = texto;
    }

    /// <summary>Marca la ventana como ocupada mientras dura <paramref name="accion"/>, con el anillo y un mensaje.</summary>
    private async Task Ocupado(string mensaje, Func<Task> accion)
    {
        _ocupado = true;
        Estado(mensaje);
        Actualizar();
        try { await accion(); }
        finally
        {
            _ocupado = false;
            Actualizar();
        }
    }

    /// <summary>Trabajo pesado en segundo plano; se recuerda para no liberar el modelo mientras se usa.</summary>
    private async Task<T> Trabajo<T>(Func<T> funcion)
    {
        var t = Task.Run(funcion);
        _trabajo = t;
        try { return await t; }
        finally { _trabajo = null; }
    }

    private Task Trabajo(Action accion) => Trabajo(() => { accion(); return true; });

    /// <summary>Acciones en marcha (dentro de <see cref="Protegido"/>): mientras haya alguna, la × espera.</summary>
    private int _enCurso;

    /// <summary>Se ha pulsado la × con algo en marcha: se cierra al terminar.</summary>
    private bool _cerrarAlTerminar;

    /// <summary>Ejecuta una acción mostrando cualquier error en un diálogo. False si ha fallado.</summary>
    private async Task<bool> Protegido(Func<Task> accion)
    {
        _enCurso++;
        try
        {
            return await ProtegidoSinCierre(accion);
        }
        finally
        {
            _enCurso--;
            if (_enCurso == 0 && _cerrarAlTerminar && !_cerrada)
            {
                _cerrarAlTerminar = false;
                try { await PedirCierreAsync(); }
                catch (Exception ex) { App.RegistrarError(ex); Cerrar(sinPreguntar: true); }
            }
        }
    }

    private async Task<bool> ProtegidoSinCierre(Func<Task> accion)
    {
        try
        {
            await accion();
            return true;
        }
        catch (Exception ex) when (ex is ExcepcionEdicion or ExcepcionImagen or ExcepcionModelo or OnnxRuntimeException
                                       or IOException or UnauthorizedAccessException or InvalidOperationException
                                       or OutOfMemoryException)
        {
            Estado("");
            if (_cerrada) return false;
            var mensaje = ex switch
            {
                OnnxRuntimeException => "La IA ha fallado: " + ex.Message,
                OutOfMemoryException => "No hay memoria suficiente para esta imagen. Prueba con una más pequeña.",
                _ => ex.Message,
            };
            await DialogosIA.AvisarAsync(_raiz.XamlRoot, "No se ha podido completar", mensaje);
            return false;
        }
        catch (Exception ex)
        {
            // Lo inesperado (SkiaSharp sin memoria, el selector de archivos, ONNX sin instalar…) tampoco cierra nada.
            App.RegistrarError(ex);
            Estado("");
            if (_cerrada) return false;
            try { await DialogosIA.AvisarAsync(_raiz.XamlRoot, "No se ha podido completar", "Ha ocurrido un error inesperado: " + ex.Message); }
            catch (Exception) { /* ya está en error.log */ }
            return false;
        }
    }

    private static Button Boton(string glifo, string texto, string ayuda)
    {
        var b = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { new FontIcon { Glyph = glifo, FontSize = 14 }, new TextBlock { Text = texto } },
            },
        };
        ToolTipService.SetToolTip(b, ayuda);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, texto);
        return b;
    }

    private static Border Etiqueta(string texto) => new()
    {
        Background = new SolidColorBrush(ColorHelper.FromArgb(150, 0, 0, 0)), CornerRadius = new CornerRadius(4),
        Padding = new Thickness(8, 3, 8, 4), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        Child = new TextBlock { Text = texto, Foreground = new SolidColorBrush(Colors.White), FontSize = 12 },
    };

    /// <summary>Un atajo de la ayuda: sus teclas dibujadas como teclas y, al lado, para qué sirve.</summary>
    private static StackPanel Atajo(string accion, params string[] teclas)
    {
        var atajo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var tecla in teclas)
            atajo.Children.Add(new Border
            {
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2),
                BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                Child = new TextBlock { Text = tecla, FontSize = 12 },
            });
        atajo.Children.Add(new TextBlock
        {
            Text = accion, FontSize = 12, Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        return atajo;
    }

    private static Border Separador() => new()
    {
        Width = 1, Margin = new Thickness(4, 6, 4, 6),
        Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
    };

    /// <summary>RGBA sin premultiplicar → BGRA premultiplicado, que es lo que usa WriteableBitmap.</summary>
    private static async Task<WriteableBitmap> BitmapDeAsync(ImagenRgba img)
    {
        var bgra = await Task.Run(() =>
        {
            var p = img.Pixeles;
            var d = new byte[p.Length];
            for (var i = 0; i < p.Length; i += 4)
            {
                var a = p[i + 3];
                d[i] = (byte)(p[i + 2] * a / 255);
                d[i + 1] = (byte)(p[i + 1] * a / 255);
                d[i + 2] = (byte)(p[i] * a / 255);
                d[i + 3] = a;
            }
            return d;
        });
        var wb = new WriteableBitmap(img.Ancho, img.Alto);
        using (var flujo = wb.PixelBuffer.AsStream()) await flujo.WriteAsync(bgra);
        wb.Invalidate();
        return wb;
    }
}
