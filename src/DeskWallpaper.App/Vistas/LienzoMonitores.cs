using DeskWallpaper.Nucleo;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DeskWallpaper.App.Vistas;

/// <summary>Imagen de una zona tal como la dibuja el lienzo: copia reducida, tamaño original y encuadre.</summary>
/// <summary>Imagen (o fotograma de un vídeo, con <paramref name="EsVideo"/>) de una zona, ya reducida.</summary>
public sealed record ImagenZona(BitmapImage Reducida, SizePx Original, Encuadre Encuadre, bool EsVideo = false);

/// <summary>
/// Dibuja los monitores a escala física con la imagen de cada zona en vivo (misma geometría que el
/// aplicador: <see cref="PlanRecorte"/>). Permite seleccionar zonas, encuadrar con el ratón, soltar
/// archivos desde el Explorador y unir o separar monitores contiguos.
/// </summary>
public sealed class LienzoMonitores : UserControl
{
    private const double FactorRueda = 1.1;

    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly DispatcherTimer _finRueda = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private Disposicion? _disposicion;
    private LienzoFisico? _lienzo;
    private IReadOnlyList<Zona> _zonas = [];
    private Dictionary<string, ImagenZona> _imagenes = [];
    private IReadOnlyDictionary<string, double> _avisos = new Dictionary<string, double>();
    private string? _seleccion;

    // Imágenes dibujadas por monitor, para mover el encuadre sin reconstruir (y sin perder la captura del ratón).
    private readonly Dictionary<string, (Image Imagen, Size Area)> _vistas = new(StringComparer.OrdinalIgnoreCase);

    // Marcos dibujados por Id de zona, para cambiar la selección sin reconstruir.
    private readonly Dictionary<string, List<Border>> _marcos = [];
    private Zona? _zonaRueda;

    // Arrastre en curso.
    private Zona? _zonaArrastre;
    private global::Windows.Foundation.Point _ultimoPunto;
    private bool _movido;

    public event EventHandler<Zona>? ElegirImagen;
    public event EventHandler<Zona?>? ZonaSeleccionada;
    public event EventHandler<(Zona Zona, Encuadre Encuadre, bool Final)>? EncuadreCambiado;
    public event EventHandler<(Zona Zona, string Ruta)>? ArchivoSoltado;
    public event EventHandler<(string A, string B, bool Unir)>? EnlaceCambiado;

    public LienzoMonitores()
    {
        Content = _canvas;
        SizeChanged += (_, _) => Redibujar();
        _canvas.PointerPressed += (_, e) =>
        {
            // Clic en un hueco vacío: se quita la selección.
            if (ReferenceEquals(e.OriginalSource, _canvas)) ZonaSeleccionada?.Invoke(this, null);
        };
        _finRueda.Tick += (_, _) =>
        {
            _finRueda.Stop();
            if (_zonaRueda is { } z && _imagenes.TryGetValue(z.Id, out var img))
                EncuadreCambiado?.Invoke(this, (z, img.Encuadre, true));
            _zonaRueda = null;
        };
    }

    /// <param name="imagenes">Imagen de cada zona, por Id de zona.</param>
    /// <param name="avisos">Factor de ampliación por Id de monitor (solo los que superan el umbral).</param>
    public void Mostrar(Disposicion d, LienzoFisico l, IReadOnlyList<Zona> zonas, IReadOnlyDictionary<string, ImagenZona> imagenes,
                        IReadOnlyDictionary<string, double> avisos, string? zonaSeleccionada)
    {
        // Si solo cambia la selección no se reconstruye nada: así un doble clic llega al mismo elemento
        // que recibió el primer clic (al reconstruir, el segundo caía en un elemento nuevo y se perdía).
        var mismoContenido = ReferenceEquals(_disposicion, d) && ReferenceEquals(_lienzo, l) && _zonas.SequenceEqual(zonas)
            && MismoDiccionario(_imagenes, imagenes) && MismoDiccionario(_avisos, avisos);

        _disposicion = d;
        _lienzo = l;
        _zonas = zonas;
        _imagenes = new Dictionary<string, ImagenZona>(imagenes);
        _avisos = avisos;
        _seleccion = zonaSeleccionada;
        if (_zonaArrastre is not null) return; // durante un arrastre no se reconstruye: se perdería la captura
        if (mismoContenido && _marcos.Count > 0) ActualizarSeleccion();
        else Redibujar();
    }

    private static bool MismoDiccionario<T>(IReadOnlyDictionary<string, T> a, IReadOnlyDictionary<string, T> b) =>
        a.Count == b.Count && a.All(par => b.TryGetValue(par.Key, out var v) && EqualityComparer<T>.Default.Equals(par.Value, v));

    /// <summary>Solo el borde de los monitores: resaltado en la zona seleccionada.</summary>
    private void ActualizarSeleccion()
    {
        foreach (var (zonaId, marcos) in _marcos)
            foreach (var marco in marcos)
            {
                var seleccionada = zonaId == _seleccion;
                marco.BorderThickness = new Thickness(seleccionada ? 3 : 2);
                marco.BorderBrush = Recurso(seleccionada ? "AccentFillColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush");
            }
    }

    private (double Escala, double OrigenX, double OrigenY, double MinX, double MinY)? Transformacion()
    {
        if (_disposicion is null || _lienzo is null || _disposicion.Monitores.Count == 0 || ActualWidth < 10 || ActualHeight < 10)
            return null;
        var rects = _disposicion.Monitores.Select(m => _lienzo.Monitor(m.Id)).ToList();
        var minX = rects.Min(r => r.X);
        var minY = rects.Min(r => r.Y);
        var anchoMm = rects.Max(r => r.Derecha) - minX;
        var altoMm = rects.Max(r => r.Abajo) - minY;
        const double margen = 24;
        var escala = Math.Min((ActualWidth - 2 * margen) / anchoMm, (ActualHeight - 2 * margen) / altoMm);
        return (escala, (ActualWidth - anchoMm * escala) / 2, (ActualHeight - altoMm * escala) / 2, minX, minY);
    }

    private void Redibujar()
    {
        _canvas.Children.Clear();
        _vistas.Clear();
        _marcos.Clear();
        if (Transformacion() is not { } t || _disposicion is null || _lienzo is null) return;

        global::Windows.Foundation.Rect APantalla(RectMm r) =>
            new(t.OrigenX + (r.X - t.MinX) * t.Escala, t.OrigenY + (r.Y - t.MinY) * t.Escala, r.Ancho * t.Escala, r.Alto * t.Escala);

        foreach (var zona in _zonas)
        {
            var seleccionada = zona.Id == _seleccion;
            _imagenes.TryGetValue(zona.Id, out var imagen);

            foreach (var id in zona.MonitorIds)
            {
                if (_disposicion.Buscar(id) is not { } m) continue;
                var area = APantalla(_lienzo.Monitor(m.Id));
                var contenido = new Grid();

                if (imagen is not null)
                {
                    var lamina = new Canvas
                    {
                        Clip = new RectangleGeometry { Rect = new global::Windows.Foundation.Rect(0, 0, area.Width, area.Height) },
                    };
                    var vista = new Image { Source = imagen.Reducida, Stretch = Stretch.Fill };
                    lamina.Children.Add(vista);
                    contenido.Children.Add(lamina);
                    _vistas[m.Id] = (vista, new Size(area.Width, area.Height));
                }

                contenido.Children.Add(Etiqueta($"{m.Numero} · {m.Nombre}"));
                if (m.Principal) contenido.Children.Add(IconoPrincipal());
                if (imagen is { EsVideo: true }) contenido.Children.Add(IconoVideo());
                if (_avisos.TryGetValue(m.Id, out var factor)) contenido.Children.Add(IconoAviso(factor));

                var marco = new Border
                {
                    Width = area.Width,
                    Height = area.Height,
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(seleccionada ? 3 : 2),
                    BorderBrush = Recurso(seleccionada ? "AccentFillColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush"),
                    Background = Recurso("CardBackgroundFillColorSecondaryBrush"),
                    Child = contenido,
                    AllowDrop = true,
                };
                Conectar(marco, zona);
                if (!_marcos.TryGetValue(zona.Id, out var deZona)) _marcos[zona.Id] = deZona = [];
                deZona.Add(marco);
                Canvas.SetLeft(marco, area.X);
                Canvas.SetTop(marco, area.Y);
                _canvas.Children.Add(marco);
            }

            if (imagen is not null) ColocarImagen(zona, imagen);
            else
            {
                var envolvente = APantalla(_lienzo.Envolvente(zona));
                var boton = new Button { Content = "Elegir imagen…", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                boton.Click += (_, _) => ElegirImagen?.Invoke(this, zona);
                // Fondo transparente (no nulo) para que el contenedor reciba lo que se suelte encima del botón.
                var contenedor = new Grid { Width = envolvente.Width, Height = envolvente.Height, Children = { boton },
                                            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                ConectarSoltar(contenedor, zona);
                contenedor.PointerPressed += (_, e) => { ZonaSeleccionada?.Invoke(this, zona); e.Handled = true; };
                contenedor.DoubleTapped += (_, _) => ElegirImagen?.Invoke(this, zona);
                Canvas.SetLeft(contenedor, envolvente.X);
                Canvas.SetTop(contenedor, envolvente.Y);
                _canvas.Children.Add(contenedor);
            }
        }

        DibujarEnlaces(t.Escala, t.OrigenX, t.OrigenY, t.MinX, t.MinY);
    }

    /// <summary>Coloca la imagen de cada monitor de la zona según el encuadre (sin reconstruir nada).</summary>
    private void ColocarImagen(Zona zona, ImagenZona imagen)
    {
        if (_disposicion is null || _lienzo is null) return;
        foreach (var p in PlanRecorte.Calcular(imagen.Original, zona.MonitorIds, _disposicion, _lienzo, imagen.Encuadre))
        {
            if (!_vistas.TryGetValue(p.MonitorId, out var v)) continue;
            var ex = v.Area.Width / p.Region.Ancho;
            var ey = v.Area.Height / p.Region.Alto;
            v.Imagen.Width = imagen.Original.Ancho * ex;
            v.Imagen.Height = imagen.Original.Alto * ey;
            Canvas.SetLeft(v.Imagen, -p.Region.X * ex);
            Canvas.SetTop(v.Imagen, -p.Region.Y * ey);
        }
    }

    private void Conectar(Border marco, Zona zona)
    {
        marco.PointerPressed += (_, e) =>
        {
            var punto = e.GetCurrentPoint(_canvas);
            if (!punto.Properties.IsLeftButtonPressed) return;
            if (!_imagenes.ContainsKey(zona.Id))
            {
                ZonaSeleccionada?.Invoke(this, zona);
                return;
            }
            // Primero se captura el ratón y luego se avisa de la selección: así Mostrar no reconstruye
            // el lienzo a mitad del gesto (el arrastre se quedaría enganchado a un elemento ya retirado).
            _zonaArrastre = zona;
            _ultimoPunto = punto.Position;
            _movido = false;
            marco.CapturePointer(e.Pointer);
            ZonaSeleccionada?.Invoke(this, zona);
            e.Handled = true;
        };
        marco.PointerMoved += (_, e) =>
        {
            if (_zonaArrastre != zona || !_imagenes.TryGetValue(zona.Id, out var img)) return;
            var p = e.GetCurrentPoint(_canvas).Position;
            var (dx, dy) = (p.X - _ultimoPunto.X, p.Y - _ultimoPunto.Y);
            _ultimoPunto = p;
            if (Math.Abs(dx) + Math.Abs(dy) < 0.5) return;
            _movido = true;
            var escala = EscalaPantallaPorPixelImagen(zona, img);
            var nuevo = EncuadreInteractivo.Desplazar(img.Original, _lienzo!.Envolvente(zona), img.Encuadre, dx / escala, dy / escala);
            ActualizarEncuadre(zona, img with { Encuadre = nuevo }, final: false);
        };
        marco.PointerReleased += (_, e) => TerminarArrastre(marco, e.Pointer, zona);
        marco.PointerCaptureLost += (_, _) => { if (_zonaArrastre == zona) TerminarArrastre(marco, null, zona); };
        marco.PointerWheelChanged += (_, e) =>
        {
            if (!_imagenes.TryGetValue(zona.Id, out var img)) return;
            var pasos = e.GetCurrentPoint(_canvas).Properties.MouseWheelDelta / 120.0;
            var nuevo = EncuadreInteractivo.Zoom(img.Original, _lienzo!.Envolvente(zona), img.Encuadre, Math.Pow(FactorRueda, pasos));
            ActualizarEncuadre(zona, img with { Encuadre = nuevo }, final: false);
            _zonaRueda = zona;
            _finRueda.Stop();
            _finRueda.Start();
            e.Handled = true;
        };
        marco.DoubleTapped += (_, _) => ElegirImagen?.Invoke(this, zona);
        ConectarSoltar(marco, zona);
    }

    /// <summary>
    /// Permite soltar sobre el elemento una miniatura de la biblioteca o un archivo del Explorador
    /// para usarlo en la zona.
    /// </summary>
    private void ConectarSoltar(UIElement elemento, Zona zona)
    {
        elemento.AllowDrop = true;
        elemento.DragOver += async (_, e) =>
        {
            if (e.DataView.Contains(CuadriculaBiblioteca.FormatoRuta))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Usar en esta zona";
                return;
            }
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var aplazamiento = e.GetDeferral();
            try
            {
                var elementos = await e.DataView.GetStorageItemsAsync();
                var valida = elementos.OfType<StorageFile>().Any(f => Admitido(f.Path));
                e.AcceptedOperation = valida ? DataPackageOperation.Copy : DataPackageOperation.None;
                e.DragUIOverride.Caption = valida ? "Usar en esta zona" : "No es una imagen ni un vídeo admitido";
            }
            catch (Exception)
            {
                // Un origen virtual (adjunto de correo, interior de un .zip, teléfono) puede no dar archivos.
                e.AcceptedOperation = DataPackageOperation.None;
            }
            finally
            {
                aplazamiento.Complete();
            }
        };
        elemento.Drop += async (_, e) =>
        {
            string? ruta = null;
            try
            {
                if (e.DataView.Contains(CuadriculaBiblioteca.FormatoRuta))
                    ruta = await e.DataView.GetDataAsync(CuadriculaBiblioteca.FormatoRuta) as string;
                else if (e.DataView.Contains(StandardDataFormats.StorageItems))
                    // Solo el primer archivo admitido; el resto (carpetas, PDF…) se ignora.
                    ruta = (await e.DataView.GetStorageItemsAsync()).OfType<StorageFile>()
                           .FirstOrDefault(f => Admitido(f.Path))?.Path;
            }
            catch (Exception)
            {
                return; // origen que no da archivos: se ignora, como al soltar algo que no es imagen
            }
            if (ruta is not null && ruta.Length > 0 && Admitido(ruta)) ArchivoSoltado?.Invoke(this, (zona, ruta));
        };
    }

    private void TerminarArrastre(Border marco, Pointer? puntero, Zona zona)
    {
        if (_zonaArrastre != zona) return;
        _zonaArrastre = null;
        if (puntero is not null) marco.ReleasePointerCapture(puntero);
        var movido = _movido;
        _movido = false;
        if (movido && _imagenes.TryGetValue(zona.Id, out var img))
            EncuadreCambiado?.Invoke(this, (zona, img.Encuadre, true));
        else
            ActualizarSeleccion(); // solo un clic: mostrar la selección que se pidió durante el gesto
    }

    private void ActualizarEncuadre(Zona zona, ImagenZona img, bool final)
    {
        _imagenes[zona.Id] = img;
        ColocarImagen(zona, img);
        EncuadreCambiado?.Invoke(this, (zona, img.Encuadre, final));
    }

    /// <summary>Píxeles de pantalla por píxel de la imagen original (igual en todos los monitores de la zona).</summary>
    private double EscalaPantallaPorPixelImagen(Zona zona, ImagenZona img)
    {
        var p = PlanRecorte.Calcular(img.Original, zona.MonitorIds, _disposicion!, _lienzo!, img.Encuadre)[0];
        return _vistas.TryGetValue(p.MonitorId, out var v) ? v.Area.Width / p.Region.Ancho : 1;
    }

    private void DibujarEnlaces(double escala, double origenX, double origenY, double minX, double minY)
    {
        foreach (var (a, b, unidos) in EditorZonas.Contiguos(_disposicion!, _zonas))
        {
            var ra = _lienzo!.Monitor(a);
            var rb = _lienzo.Monitor(b);
            var pa = _disposicion!.Buscar(a)!.Escritorio;
            var pb = _disposicion.Buscar(b)!.Escritorio;
            double x, y;
            if (pa.Derecha == pb.X || pb.Derecha == pa.X)
            {
                var (izq, der) = ra.X < rb.X ? (ra, rb) : (rb, ra);
                x = (izq.Derecha + der.X) / 2;
                // En la parte alta del borde común: en el centro taparía el botón "Elegir imagen…" de la zona.
                var arriba = Math.Max(ra.Y, rb.Y);
                y = arriba + (Math.Min(ra.Abajo, rb.Abajo) - arriba) * 0.2;
            }
            else
            {
                var (arr, aba) = ra.Y < rb.Y ? (ra, rb) : (rb, ra);
                y = (arr.Abajo + aba.Y) / 2;
                x = (Math.Max(ra.X, rb.X) + Math.Min(ra.Derecha, rb.Derecha)) / 2;
            }

            var boton = new Button
            {
                Width = 30, Height = 30, Padding = new Thickness(0), CornerRadius = new CornerRadius(15),
                Content = new FontIcon { Glyph = "", FontSize = 14 },
                Style = unidos ? (Style)Application.Current.Resources["AccentButtonStyle"] : null,
                Opacity = unidos ? 1 : 0.85,
            };
            var texto = unidos ? "Separar monitores" : "Unir monitores";
            ToolTipService.SetToolTip(boton, texto);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(boton,
                $"{texto} {_disposicion.Buscar(a)!.Numero} y {_disposicion.Buscar(b)!.Numero}");
            boton.Click += (_, _) => EnlaceCambiado?.Invoke(this, (a, b, !unidos));
            Canvas.SetLeft(boton, origenX + (x - minX) * escala - 15);
            Canvas.SetTop(boton, origenY + (y - minY) * escala - 15);
            _canvas.Children.Add(boton);
        }
    }

    private static Border Etiqueta(string texto) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(6),
        Padding = new Thickness(8, 2, 8, 3),
        CornerRadius = new CornerRadius(4),
        Background = Recurso("SmokeFillColorDefaultBrush"),
        IsHitTestVisible = false,
        Child = new TextBlock { Text = texto, FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
    };

    /// <summary>▶ arriba a la izquierda: la zona tiene un fondo animado; se ve su fotograma fijo.</summary>
    private static Border IconoVideo()
    {
        var icono = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(4),
            Background = Recurso("SmokeFillColorDefaultBrush"),
            IsHitTestVisible = false,
            Child = new FontIcon { Glyph = "\uE768", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(icono, "Fondo animado");
        return icono;
    }

    /// <summary>Lo que se puede soltar en una zona: imágenes y, desde la fase B, vídeos.</summary>
    private static bool Admitido(string ruta) => FormatosImagen.EsImagenAdmitida(ruta) || FormatosVideo.EsVideo(ruta);

    /// <summary>Casa arriba a la derecha: la pantalla principal de Windows.</summary>
    private static Border IconoPrincipal()
    {
        var icono = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(4),
            Background = Recurso("SmokeFillColorDefaultBrush"),
            Child = new FontIcon { Glyph = "", FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) },
        };
        ToolTipService.SetToolTip(icono, "Pantalla principal de Windows");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(icono, "Pantalla principal");
        return icono;
    }

    private static Border IconoAviso(double factor)
    {
        var icono = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6),
            Padding = new Thickness(5),
            CornerRadius = new CornerRadius(4),
            Background = Recurso("SystemFillColorCautionBackgroundBrush"),
            Child = new FontIcon { Glyph = "", FontSize = 14, Foreground = Recurso("SystemFillColorCautionBrush") },
        };
        ToolTipService.SetToolTip(icono, $"La imagen se ampliará un {Math.Round((factor - 1) * 100)} %");
        return icono;
    }

    private static Brush Recurso(string clave) => (Brush)Application.Current.Resources[clave];

    private readonly record struct Size(double Width, double Height);
}
