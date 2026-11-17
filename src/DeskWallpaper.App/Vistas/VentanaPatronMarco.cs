using DeskWallpaper.Nucleo;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Patrón para ajustar el marco a ojo: una ventana negra a pantalla completa en cada monitor de la
/// zona, con líneas trazadas en coordenadas físicas. Si el marco es el correcto, las líneas siguen
/// rectas al cruzar de una pantalla a otra.
/// </summary>
public static class VentanaPatronMarco
{
    private const double Paso = 0.5;

    /// <param name="alTerminar">Recibe el valor final en mm al pulsar "Listo" o Esc.</param>
    /// <param name="claves">Claves de marco que toma el valor.</param>
    public static void Mostrar(Disposicion d, Zona zona, IReadOnlyList<string> claves, double mmInicial,
                               IReadOnlyDictionary<string, double> marcosBase, Action<double> alTerminar)
    {
        var mm = mmInicial;
        var ventanas = new List<(Window Ventana, DatosMonitor Monitor, Canvas Lienzo)>();
        TextBlock? texto = null;
        var cerrado = false;

        void Dibujar()
        {
            var marcos = new Dictionary<string, double>(marcosBase, StringComparer.OrdinalIgnoreCase);
            foreach (var clave in claves) marcos[clave] = mm;
            var l = LienzoFisico.DesdeDisposicion(d, marcos);
            var e = l.Envolvente(zona);
            foreach (var (ventana, m, lienzo) in ventanas)
            {
                lienzo.Children.Clear();
                var r = l.Monitor(m.Id);
                var escala = ventana.Content.XamlRoot?.RasterizationScale ?? m.Escala;
                // mm físicos → DIP de esta ventana.
                double X(double xmm) => (xmm - r.X) * (m.Escritorio.Ancho / r.Ancho) / escala;
                double Y(double ymm) => (ymm - r.Y) * (m.Escritorio.Alto / r.Alto) / escala;

                // Todo en mm físicos sobre la zona entera, para que sirva igual con monitores al lado,
                // encima o debajo: rejilla, diagonales a 45° en los dos sentidos
                // y un círculo. Con el marco correcto todo cruza las uniones sin quebrarse.
                var paso = Math.Min(e.Ancho, e.Alto) / 4;
                for (var x = e.X + paso / 2; x < e.Derecha; x += paso)
                    lienzo.Children.Add(Linea(X(x), Y(e.Y), X(x), Y(e.Abajo), Rejilla, 1.5));
                for (var y = e.Y + paso / 2; y < e.Abajo; y += paso)
                    lienzo.Children.Add(Linea(X(e.X), Y(y), X(e.Derecha), Y(y), Rejilla, 1.5));
                for (var c = e.X - e.Alto; c < e.Derecha; c += paso)
                {
                    lienzo.Children.Add(Linea(X(c), Y(e.Y), X(c + e.Alto), Y(e.Abajo), Colors.Gold, 2.5));
                    lienzo.Children.Add(Linea(X(c), Y(e.Abajo), X(c + e.Alto), Y(e.Y), Colors.Cyan, 2.5));
                }
                var radio = 0.45 * Math.Min(e.Ancho, e.Alto);
                var (cx, cy) = (e.X + e.Ancho / 2, e.Y + e.Alto / 2);
                var circulo = new Ellipse
                {
                    Width = X(cx + radio) - X(cx - radio),
                    Height = Y(cy + radio) - Y(cy - radio),
                    Stroke = new SolidColorBrush(Colors.Magenta),
                    StrokeThickness = 3,
                };
                Canvas.SetLeft(circulo, X(cx - radio));
                Canvas.SetTop(circulo, Y(cy - radio));
                lienzo.Children.Add(circulo);
            }
            if (texto is not null) texto.Text = $"Marco: {mm:0.0} mm";
        }

        void Cerrar()
        {
            if (cerrado) return;
            cerrado = true;
            foreach (var (v, _, _) in ventanas)
            {
                try { v.Close(); } catch (Exception) { /* ya se estaba cerrando */ }
            }
            alTerminar(mm);
        }

        var monitores = zona.MonitorIds.Select(id => d.Buscar(id)).OfType<DatosMonitor>().ToList();
        var derecha = monitores.MaxBy(m => m.Escritorio.Derecha);
        foreach (var m in monitores)
        {
            var lienzo = new Canvas { Background = new SolidColorBrush(Colors.Black) };
            var raiz = new Grid
            {
                Background = new SolidColorBrush(Colors.Black),
                Children = { lienzo },
                KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden, // sin el globo "Esc"
            };
            if (m == derecha) raiz.Children.Add(BarraControl(out texto, () => { mm = Math.Max(0, mm - Paso); Dibujar(); },
                                                             () => { mm = Math.Min(100, mm + Paso); Dibujar(); }, Cerrar));
            // Atajo y no KeyDown: un Grid no recibe el foco, y Esc debe funcionar en cualquier ventana del patrón.
            var esc = new KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Escape };
            esc.Invoked += (_, e) => { e.Handled = true; Cerrar(); };
            raiz.KeyboardAccelerators.Add(esc);

            var ventana = new Window { Content = raiz, Title = "Alineación del marco" };
            ventana.AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(m.Escritorio.X, m.Escritorio.Y, m.Escritorio.Ancho, m.Escritorio.Alto));
            ventana.Closed += (_, _) => Cerrar();
            raiz.Loaded += (_, _) => { Dibujar(); raiz.Focus(FocusState.Programmatic); };
            ventanas.Add((ventana, m, lienzo));
            ventana.Activate();
            // Después de mostrarla: fijado antes, Windows no siempre lo respeta en todas las ventanas.
            ventana.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            raiz.SizeChanged += (_, _) => Dibujar();
        }
    }

    private static readonly global::Windows.UI.Color Rejilla = Microsoft.UI.ColorHelper.FromArgb(150, 200, 200, 200);

    private static Line Linea(double x1, double y1, double x2, double y2, global::Windows.UI.Color color, double grosor) => new()
    {
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
        Stroke = new SolidColorBrush(color),
        StrokeThickness = grosor,
    };

    private static Border BarraControl(out TextBlock texto, Action menos, Action mas, Action listo)
    {
        texto = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 16, Foreground = new SolidColorBrush(Colors.White), MinWidth = 140 };
        var bMenos = new Button { Content = "−", MinWidth = 40 };
        var bMas = new Button { Content = "+", MinWidth = 40 };
        var bListo = new Button { Content = "Listo", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        bMenos.Click += (_, _) => menos();
        bMas.Click += (_, _) => mas();
        bListo.Click += (_, _) => listo();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bMenos, "Reducir marco");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bMas, "Aumentar marco");
        return new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 60),
            Padding = new Thickness(16, 10, 16, 10),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(230, 32, 32, 32)),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    texto, bMenos, bMas, bListo,
                    new TextBlock { Text = "Ajusta hasta que las líneas sigan rectas al cruzar el hueco. Esc para terminar.",
                                    Foreground = new SolidColorBrush(Colors.LightGray), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) },
                },
            },
        };
    }
}
