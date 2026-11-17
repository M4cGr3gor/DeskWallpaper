using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using DeskWallpaper.Nucleo;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace DeskWallpaper.App.Vistas;

/// <summary>Una imagen que el visor puede mostrar, con lo que enseña su panel de información.</summary>
/// <param name="Tipo">«Panorámica», «Un monitor» u «Otra»; null si no se sabe (búsqueda online).</param>
/// <param name="EsVideo">Un fondo animado: se reproduce mudo y en bucle sobre su fotograma.</param>
public sealed record ElementoVisor(string Ruta, string? Tipo = null, IReadOnlyList<string>? Etiquetas = null, bool Favorita = false,
                                   bool EsVideo = false, TimeSpan? Duracion = null);

/// <param name="Imagenes">Las imágenes por las que se pasa con las flechas (la sección que se está viendo).</param>
/// <param name="Indice">La que se muestra al abrir.</param>
/// <param name="Monitor">Rectángulo del monitor en píxeles físicos.</param>
/// <param name="AlCerrar">Se llama al cerrarse (para devolver el foco a la ventana principal).</param>
/// <param name="Acciones">Botones junto al de cerrar para la imagen que se está viendo: al pulsarlos se cierra el visor.</param>
/// <param name="ConInformacion">Si el panel de información empieza abierto (se recuerda entre visores).</param>
/// <param name="AlCambiarInformacion">Se llama al abrir o cerrar el panel, para recordarlo.</param>
public sealed record OpcionesVisor(IReadOnlyList<ElementoVisor> Imagenes, int Indice, global::Windows.Graphics.RectInt32 Monitor,
                                   IRenderizador Renderizador, Action? AlCerrar = null,
                                   Func<ElementoVisor, IReadOnlyList<(string Texto, string Glifo, Action Accion)>>? Acciones = null,
                                   bool ConInformacion = false, Action<bool>? AlCambiarInformacion = null);

/// <summary>
/// Visor a pantalla completa en un solo monitor (el de la ventana principal), para ver la imagen entera y sus
/// detalles; no es una vista previa del fondo. Rueda: zoom hacia el cursor; arrastrar: mover;
/// doble clic: ajustar ↔ 100 %; teclas 0, 1, + y −; Esc o × para salir.
/// Fase 8: ← y → (o las flechas de los bordes) pasan de imagen dentro de la sección que se
/// está viendo, con la anterior y la siguiente ya precargadas; I (o ⓘ) muestra el panel de información.
/// </summary>
public static class VisorBasico
{
    /// <summary>Zoom máximo, en píxeles de pantalla por píxel de la imagen.</summary>
    public const double ZoomMaximo = 8;

    /// <summary>Hay un visor abierto o abriéndose: un doble clic repetido no abre otro encima.</summary>
    private static bool _ocupado;

    /// <summary>Una sola imagen, sin flechas (p. ej. una de la búsqueda online).</summary>
    public static Task MostrarAsync(string ruta, global::Windows.Graphics.RectInt32 monitor, IRenderizador renderizador, Action? alCerrar) =>
        MostrarAsync(new OpcionesVisor([new ElementoVisor(ruta)], 0, monitor, renderizador, alCerrar));

    public static async Task MostrarAsync(OpcionesVisor o)
    {
        if (_ocupado || o.Imagenes.Count == 0) return;
        _ocupado = true;
        try
        {
            await new Visor(o).AbrirAsync();
        }
        catch
        {
            _ocupado = false;
            throw;
        }
    }

    /// <summary>Un visor abierto: su ventana, la imagen actual y su vista (escala y posición).</summary>
    private sealed class Visor
    {
        private static readonly SolidColorBrush FondoBoton = new(ColorHelper.FromArgb(140, 0, 0, 0));
        private static readonly SolidColorBrush Blanco = new(Colors.White);

        private readonly OpcionesVisor _o;
        private readonly Window _ventana;
        private readonly Grid _raiz;
        private readonly Canvas _lienzo = new() { Background = new SolidColorBrush(Colors.Black) };
        private readonly Image _imagen = new() { Stretch = Stretch.Fill };

        /// <summary>
        /// Reproductor de los fondos animados, encima de su fotograma: ajustado a la pantalla, sin zoom ni controles.
        /// Usa Media Foundation y los códecs del sistema, como el ayudante.
        /// </summary>
        private readonly MediaPlayerElement _video = new()
        {
            AreTransportControlsEnabled = false, Stretch = Stretch.Uniform, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        private readonly TextBlock _pie = new() { Foreground = new SolidColorBrush(ColorHelper.FromArgb(220, 255, 255, 255)), FontSize = 12 };
        private readonly TextBlock _error = new()
        {
            Foreground = Blanco, FontSize = 16, Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        private readonly StackPanel _acciones = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        private readonly Button _anterior, _siguiente, _informacion;
        private readonly Border _panel;
        private readonly StackPanel _datos = new() { Spacing = 10 };

        /// <summary>Imágenes ya cargadas o cargándose, por índice (como mucho la actual y sus dos vecinas).</summary>
        private readonly Dictionary<int, Task<(byte[] Png, SizePx Original)>> _cargas = [];

        private int _indice;
        private int _version;

        /// <summary>La imagen que se ve (mientras carga la siguiente sigue viéndose esta).</summary>
        private int _indiceMostrado;

        /// <summary>Número de la petición que se ve: una que llega tarde no tapa a otra pedida después.</summary>
        private int _versionMostrada;

        /// <summary>Como mucho dos decodificaciones a la vez: con la flecha pulsada no se amontonan cientos de MB.</summary>
        private readonly SemaphoreSlim _turno = new(2);
        private SizePx _original = new(1, 1);
        private bool _cerrado;

        // Vista: escala en píxeles de pantalla por píxel de imagen, y posición en DIP.
        private double _escala = 1, _x, _y;

        public Visor(OpcionesVisor o)
        {
            _o = o;
            _indice = _indiceMostrado = Math.Clamp(o.Indice, 0, o.Imagenes.Count - 1);
            _lienzo.Children.Add(_imagen);

            var pie = new Border
            {
                Background = FondoBoton, CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false, Child = _pie,
            };
            var cerrar = BotonIcono("", "Cerrar (Esc)", "Cerrar visor");
            cerrar.Click += (_, _) => Cerrar();
            _informacion = BotonIcono("", "Información (I)", "Información de la imagen");
            _informacion.Click += (_, _) => CambiarInformacion();
            var arriba = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Children = { _acciones, _informacion, cerrar },
            };

            _anterior = Flecha("", "Imagen anterior (←)", HorizontalAlignment.Left);
            _siguiente = Flecha("", "Imagen siguiente (→)", HorizontalAlignment.Right);
            _anterior.Click += async (_, _) => await IrAAsync(_indice - 1);
            _siguiente.Click += async (_, _) => await IrAAsync(_indice + 1);
            var conFlechas = o.Imagenes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            _anterior.Visibility = _siguiente.Visibility = conFlechas;

            _panel = new Border
            {
                Width = 320, Padding = new Thickness(20), Margin = new Thickness(0, 72, 16, 72),
                CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(ColorHelper.FromArgb(200, 20, 20, 20)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Visibility = o.ConInformacion ? Visibility.Visible : Visibility.Collapsed,
                Child = new ScrollViewer { Content = _datos },
            };

            _raiz = new Grid
            {
                Background = new SolidColorBrush(Colors.Black),
                Children = { _lienzo, _video, _error, pie, _anterior, _siguiente, _panel, arriba },
                // Sin el globo automático de WinUI con la tecla ("Esc") en medio de la imagen.
                KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden,
                // Que el tema claro no pinte de negro sobre negro los textos de los botones.
                RequestedTheme = ElementTheme.Dark,
            };
            _ventana = new Window { Content = _raiz, Title = "Vista a pantalla completa" };
            _ventana.Closed += (_, _) => Cerrar();
            ConectarRaton();
            ConectarTeclado();
        }

        public async Task AbrirAsync()
        {
            await MostrarIndiceAsync(_indice);
            _ventana.AppWindow.MoveAndResize(_o.Monitor);
            _ventana.Activate();
            _ventana.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

            // Con doble clic, al soltar el segundo clic la ventana principal puede recuperar el primer plano y el
            // visor quedaba detrás. Se trae delante otra vez cuando ya ha terminado el clic.
            await Task.Delay(250);
            if (!_cerrado)
            {
                _ventana.Activate();
                SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(_ventana));
            }
        }

        private void Cerrar()
        {
            if (_cerrado) return;
            _cerrado = true;
            _ocupado = false;
            SoltarReproductor();
            try { _ventana.Close(); } catch (Exception) { /* ya se estaba cerrando */ }
            _o.AlCerrar?.Invoke();
        }

        // ── Pasar de imagen ─────────────────────────────────────────────────

        private async Task IrAAsync(int indice)
        {
            if (indice < 0 || indice >= _o.Imagenes.Count || indice == _indice) return;
            await MostrarIndiceAsync(indice);
        }

        private async Task MostrarIndiceAsync(int indice)
        {
            _indice = indice;
            var version = ++_version;
            var elemento = _o.Imagenes[indice];
            _anterior.IsEnabled = indice > 0;
            _siguiente.IsEnabled = indice < _o.Imagenes.Count - 1;

            BitmapImage? fuente = null;
            var original = new SizePx(1, 1);
            // Con la flecha pulsada se ve pasar cada imagen que termina de cargar, aunque ya se haya pedido otra;
            // solo se descarta si ya se está viendo una pedida después.
            try
            {
                (var png, original) = await Cargar(indice);
                if (version <= _versionMostrada || _cerrado) return;
                fuente = await ImagenesPng.CargarAsync(png);
            }
            catch (OperationCanceledException)
            {
                return;   // se pasó de largo sin decodificarla
            }
            catch (Exception)
            {
                // Dañada, ilegible o tan grande que no cabe en memoria: se dice y se puede seguir pasando.
                fuente = null;
                original = new SizePx(1, 1);
            }
            if (version <= _versionMostrada || _cerrado) return;
            _versionMostrada = version;

            _indiceMostrado = indice;
            _original = original;
            PintarAcciones(elemento);

            _imagen.Source = fuente;
            _imagen.Visibility = fuente is null ? Visibility.Collapsed : Visibility.Visible;
            _error.Text = $"No se puede abrir «{Path.GetFileName(elemento.Ruta)}»";
            _error.Visibility = fuente is null ? Visibility.Visible : Visibility.Collapsed;
            PintarInformacion(elemento, fuente is not null);
            Ajustar();
            MostrarVideo(elemento);
            Precargar(_indice);
        }

        /// <summary>
        /// Un solo reproductor para todo el visor: crear uno por vídeo dejaba memoria sin soltar (medido en real: de
        /// 270 MB a 1 GB en 24 pasos con las flechas).
        /// </summary>
        private MediaPlayer? _reproductor;

        /// <summary>El vídeo que se está reproduciendo, para el aviso si falla.</summary>
        private string? _rutaVideo;

        /// <summary>Con un vídeo, lo reproduce encima de su fotograma; si no se puede, queda el fotograma y el aviso.</summary>
        private void MostrarVideo(ElementoVisor e)
        {
            PararVideo();
            if (!e.EsVideo) return;
            if (_reproductor is null)
            {
                _reproductor = new MediaPlayer { IsMuted = true, IsLoopingEnabled = true, AutoPlay = true };
                _reproductor.MediaFailed += (_, _) => _raiz.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_cerrado || _rutaVideo is null) return;
                    _video.Visibility = Visibility.Collapsed;
                    _error.Text = $"No se puede reproducir «{Path.GetFileName(_rutaVideo)}»";
                    _error.Visibility = Visibility.Visible;
                });
                _video.SetMediaPlayer(_reproductor);
            }
            _rutaVideo = e.Ruta;
            _reproductor.Source = MediaSource.CreateFromUri(new Uri(e.Ruta));
            _video.Visibility = Visibility.Visible;
        }

        /// <summary>Para el vídeo y suelta su fuente al pasar a otro elemento.</summary>
        private void PararVideo()
        {
            _rutaVideo = null;
            _video.Visibility = Visibility.Collapsed;
            if (_reproductor is null) return;
            _reproductor.Pause();
            var fuente = _reproductor.Source as MediaSource;
            _reproductor.Source = null;
            fuente?.Dispose();
        }

        /// <summary>Al cerrar el visor, también el reproductor.</summary>
        private void SoltarReproductor()
        {
            PararVideo();
            if (_reproductor is null) return;
            _video.SetMediaPlayer(null);
            _reproductor.Dispose();
            _reproductor = null;
        }

        /// <summary>Copia orientada (EXIF) a tamaño completo, decodificada por Skia como el aplicador.</summary>
        private Task<(byte[] Png, SizePx Original)> Cargar(int indice)
        {
            // Una carga que se saltó (ya no hacía falta) o falló se repite si se vuelve a esa imagen.
            if (!_cargas.TryGetValue(indice, out var carga) || carga.IsFaulted || carga.IsCanceled)
            {
                var ruta = _o.Imagenes[indice].Ruta;
                _cargas[indice] = carga = Task.Run(async () =>
                {
                    await _turno.WaitAsync();
                    try
                    {
                        // Con la flecha pulsada se pasa de largo: lo que ya no es la actual ni una vecina no se decodifica.
                        if (_cerrado || Math.Abs(indice - Volatile.Read(ref _indice)) > 1) throw new OperationCanceledException();
                        return _o.Renderizador.Reducida(ruta, 16384);
                    }
                    finally
                    {
                        _turno.Release();
                    }
                });
            }
            return carga;
        }

        /// <summary>La anterior y la siguiente, para que el cambio sea inmediato; las demás se sueltan.</summary>
        private void Precargar(int indice)
        {
            foreach (var viejo in _cargas.Keys.Where(k => Math.Abs(k - indice) > 1).ToList()) _cargas.Remove(viejo);
            foreach (var vecino in new[] { indice + 1, indice - 1 })
                if (vecino >= 0 && vecino < _o.Imagenes.Count)
                    _ = Cargar(vecino).ContinueWith(t => _ = t.Exception, TaskScheduler.Default);   // un fallo se verá al llegar
        }

        private void PintarAcciones(ElementoVisor elemento)
        {
            _acciones.Children.Clear();
            foreach (var (texto, glifo, accion) in _o.Acciones?.Invoke(elemento) ?? [])
            {
                var b = BotonIcono(glifo, texto, texto);
                b.Click += (_, _) => { Cerrar(); accion(); };
                _acciones.Children.Add(b);
            }
        }

        // ── Panel de información ────────────────────────────────────────────

        private void CambiarInformacion()
        {
            var abierto = _panel.Visibility != Visibility.Visible;
            _panel.Visibility = abierto ? Visibility.Visible : Visibility.Collapsed;
            _o.AlCambiarInformacion?.Invoke(abierto);
        }

        private void PintarInformacion(ElementoVisor e, bool leida)
        {
            _datos.Children.Clear();
            _datos.Children.Add(new TextBlock { Text = Path.GetFileName(e.Ruta), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (leida) Dato("Resolución", $"{_original.Ancho}×{_original.Alto} px");
            if (e.Tipo is not null) Dato("Tipo", e.Tipo);
            if (e.Duracion is { } d) Dato("Duración", FormatosVideo.TextoDuracion(d));
            var info = new FileInfo(e.Ruta);
            if (info.Exists)
            {
                Dato("Tamaño", $"{info.Length / (1024.0 * 1024.0):0.0} MB");
                Dato("Modificada", info.LastWriteTime.ToString("d 'de' MMMM 'de' yyyy, HH:mm", CultureInfo.GetCultureInfo("es-ES")));
            }
            Dato("Carpeta", Path.GetDirectoryName(e.Ruta) ?? "");
            var abrir = new HyperlinkButton { Content = "Abrir ubicación", Padding = new Thickness(0) };
            abrir.Click += (_, _) => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{e.Ruta}\"")?.Dispose();
            _datos.Children.Add(abrir);
            if (e.Etiquetas is { } etiquetas)
                Dato("Etiquetas", etiquetas.Count > 0 ? string.Join(", ", etiquetas) : "Sin etiquetas");
            if (e.Favorita) _datos.Children.Add(new TextBlock { Text = "★ Favorita", Foreground = new SolidColorBrush(Colors.Gold) });
            if (_o.Imagenes.Count > 1)
                _datos.Children.Add(new TextBlock
                {
                    Text = $"{_indiceMostrado + 1} de {_o.Imagenes.Count}", FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0),
                });
        }

        private void Dato(string etiqueta, string valor) => _datos.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = etiqueta, FontSize = 12, Opacity = 0.7 },
                new TextBlock { Text = valor, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            },
        });

        // ── Vista: zoom y desplazamiento ────────────────────────────────────

        private double Raster() => _raiz.XamlRoot?.RasterizationScale ?? 1;
        private double Ajuste() => Math.Min(_lienzo.ActualWidth * Raster() / _original.Ancho, _lienzo.ActualHeight * Raster() / _original.Alto);
        private double Minimo() => Math.Min(Ajuste(), 1);

        private void Pintar()
        {
            var ancho = _original.Ancho * _escala / Raster();
            var alto = _original.Alto * _escala / Raster();
            // Más pequeña que la pantalla: centrada; más grande: sin dejar bordes negros al moverla.
            _x = ancho <= _lienzo.ActualWidth ? (_lienzo.ActualWidth - ancho) / 2 : Math.Clamp(_x, _lienzo.ActualWidth - ancho, 0);
            _y = alto <= _lienzo.ActualHeight ? (_lienzo.ActualHeight - alto) / 2 : Math.Clamp(_y, _lienzo.ActualHeight - alto, 0);
            _imagen.Width = ancho;
            _imagen.Height = alto;
            Canvas.SetLeft(_imagen, _x);
            Canvas.SetTop(_imagen, _y);
            var posicion = _o.Imagenes.Count > 1 ? $" · {_indiceMostrado + 1} de {_o.Imagenes.Count}" : "";
            var nombre = Path.GetFileName(_o.Imagenes[_indiceMostrado].Ruta);
            // Si no se ha podido abrir no hay resolución ni zoom que enseñar.
            var elemento = _o.Imagenes[_indiceMostrado];
            _pie.Text = _imagen.Source is null
                ? $"{nombre}{posicion}"
                : elemento is { EsVideo: true, Duracion: { } duracion }
                    ? $"{nombre} · {_original.Ancho}×{_original.Alto} · ▶ {FormatosVideo.TextoDuracion(duracion)}{posicion}"
                    : $"{nombre} · {_original.Ancho}×{_original.Alto} · {_escala * 100:0} %{posicion}";
        }

        /// <summary>Zoom hacia un punto (en DIP): ese punto de la imagen se queda bajo el cursor.</summary>
        private void Zoom(double nueva, double px, double py)
        {
            nueva = Math.Clamp(nueva, Minimo(), Math.Max(ZoomMaximo, Ajuste()));
            var f = nueva / _escala;
            _x = px - (px - _x) * f;
            _y = py - (py - _y) * f;
            _escala = nueva;
            Pintar();
        }

        private void Ajustar()
        {
            if (_lienzo.ActualWidth <= 0) return;
            _escala = Ajuste();
            Pintar();
        }

        private void Centro(double nueva) => Zoom(nueva, _lienzo.ActualWidth / 2, _lienzo.ActualHeight / 2);

        private void ConectarRaton()
        {
            _lienzo.SizeChanged += (_, _) => Ajustar();
            _lienzo.PointerWheelChanged += (_, e) =>
            {
                var p = e.GetCurrentPoint(_lienzo);
                Zoom(_escala * (p.Properties.MouseWheelDelta > 0 ? 1.25 : 0.8), p.Position.X, p.Position.Y);
                e.Handled = true;
            };
            _lienzo.DoubleTapped += (_, e) =>
            {
                var p = e.GetPosition(_lienzo);
                if (Math.Abs(_escala - Ajuste()) < 0.001) Zoom(1, p.X, p.Y);
                else Ajustar();
            };

            // Arrastrar para mover.
            global::Windows.Foundation.Point? agarre = null;
            _lienzo.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(_lienzo).Properties.IsLeftButtonPressed) return;
                agarre = e.GetCurrentPoint(_lienzo).Position;
                _lienzo.CapturePointer(e.Pointer);
            };
            _lienzo.PointerMoved += (_, e) =>
            {
                if (agarre is not { } a) return;
                var p = e.GetCurrentPoint(_lienzo).Position;
                _x += p.X - a.X;
                _y += p.Y - a.Y;
                agarre = p;
                Pintar();
            };
            _lienzo.PointerReleased += (_, e) => { agarre = null; _lienzo.ReleasePointerCapture(e.Pointer); };
            _lienzo.PointerCaptureLost += (_, _) => agarre = null;
        }

        private void ConectarTeclado()
        {
            void Tecla(global::Windows.System.VirtualKey tecla, Action accion)
            {
                var k = new KeyboardAccelerator { Key = tecla };
                k.Invoked += (_, e) => { e.Handled = true; accion(); };
                _raiz.KeyboardAccelerators.Add(k);
            }
            Tecla(global::Windows.System.VirtualKey.Escape, Cerrar);
            Tecla(global::Windows.System.VirtualKey.Number0, Ajustar);
            Tecla(global::Windows.System.VirtualKey.NumberPad0, Ajustar);
            Tecla(global::Windows.System.VirtualKey.Number1, () => Centro(1));
            Tecla(global::Windows.System.VirtualKey.NumberPad1, () => Centro(1));
            Tecla(global::Windows.System.VirtualKey.Add, () => Centro(_escala * 1.25));
            Tecla(global::Windows.System.VirtualKey.Subtract, () => Centro(_escala * 0.8));
            Tecla(global::Windows.System.VirtualKey.I, CambiarInformacion);

            // Las flechas, antes que nadie: si un botón tiene el foco, las usaría para moverse entre botones.
            _raiz.PreviewKeyDown += async (_, e) =>
            {
                if (e.Key == global::Windows.System.VirtualKey.Left) { e.Handled = true; await IrAAsync(_indice - 1); }
                else if (e.Key == global::Windows.System.VirtualKey.Right) { e.Handled = true; await IrAAsync(_indice + 1); }
            };
        }

        private static Button BotonIcono(string glifo, string ayuda, string nombre)
        {
            var b = new Button
            {
                Content = new FontIcon { Glyph = glifo, FontSize = 14 }, Foreground = Blanco, Background = FondoBoton,
                BorderThickness = new Thickness(0), Width = 40, Height = 40,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, nombre);
            ToolTipService.SetToolTip(b, ayuda);
            return b;
        }

        private static Button Flecha(string glifo, string ayuda, HorizontalAlignment lado)
        {
            var b = new Button
            {
                Content = new FontIcon { Glyph = glifo, FontSize = 20 }, Foreground = Blanco, Background = FondoBoton,
                BorderThickness = new Thickness(0), Width = 48, Height = 96, Margin = new Thickness(16),
                HorizontalAlignment = lado, VerticalAlignment = VerticalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, ayuda);
            ToolTipService.SetToolTip(b, ayuda);
            return b;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr ventana);
}
