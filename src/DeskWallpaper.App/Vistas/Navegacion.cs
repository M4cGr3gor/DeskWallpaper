using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Columna izquierda: secciones de la biblioteca con su número, Historial, y debajo las
/// etiquetas más usadas y las muestras de color para filtrar.
/// </summary>
public sealed class Navegacion : UserControl
{
    private const int MaximoEtiquetas = 12;

    private readonly StackPanel _secciones = new() { Spacing = 2 };
    private readonly StackPanel _etiquetas = new() { Spacing = 2 };
    private readonly Grid _colores = new() { RowSpacing = 6, ColumnSpacing = 6 };

    /// <summary>Nombre de la sección pulsada en la columna izquierda.</summary>
    public const string Historial = "Historial";
    public const string BuscarOnline = "BuscarOnline";

    /// <summary>«Reproducción…» de la pestaña Fondos animados: abre sus opciones (no es una sección).</summary>
    public const string Reproduccion = "Reproduccion";

    /// <summary>Sección elegida: un nombre de <see cref="SeccionBiblioteca"/>, <see cref="Historial"/> o <see cref="BuscarOnline"/>.</summary>
    public event EventHandler<string>? SeccionCambiada;
    public event EventHandler<string?>? EtiquetaCambiada;
    public event EventHandler<ColorNombre?>? ColorCambiado;

    private static readonly (string Clave, SeccionBiblioteca? Contar, string Texto, string Glifo)[] Secciones =
    [
        (nameof(SeccionBiblioteca.Todas), SeccionBiblioteca.Todas, "Todas", ""),
        (nameof(SeccionBiblioteca.Panoramica), SeccionBiblioteca.Panoramica, "Panorámicas", ""),
        (nameof(SeccionBiblioteca.UnMonitor), SeccionBiblioteca.UnMonitor, "Un monitor", ""),
        (nameof(SeccionBiblioteca.Otras), SeccionBiblioteca.Otras, "Otras", ""),
        (nameof(SeccionBiblioteca.Favoritas), SeccionBiblioteca.Favoritas, "Favoritas", ""),
        (Historial, null, "Historial", ""),
        (BuscarOnline, null, "Buscar online", ""),
    ];

    /// <summary>Las mismas secciones con sus nombres en masculino y con «Reproducción…» (y «Buscar online», de vídeos).</summary>
    private static readonly (string Clave, SeccionBiblioteca? Contar, string Texto, string Glifo)[] SeccionesAnimados =
    [
        .. Secciones.Select(s => s with
        {
            Texto = s.Texto switch { "Todas" => "Todos", "Panorámicas" => "Panorámicos", "Otras" => "Otros", "Favoritas" => "Favoritos", var t => t },
        }),
        (Reproduccion, null, "Reproducción…", ""),
    ];

    public Navegacion()
    {
        for (var i = 0; i < 6; i++) _colores.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _colores.RowDefinitions.Add(new RowDefinition());
        _colores.RowDefinitions.Add(new RowDefinition());

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    _secciones,
                    Titulo("Etiquetas"), _etiquetas,
                    Titulo("Colores"), _colores,
                },
            },
        };
    }

    /// <param name="seccionActiva">Sección activa, con los mismos nombres que <see cref="SeccionCambiada"/>.</param>
    /// <param name="cuantas">Número de imágenes por sección.</param>
    public void Mostrar(Pestana pestana, string seccionActiva, IReadOnlyDictionary<SeccionBiblioteca, int> cuantas,
                        IReadOnlyList<(string Etiqueta, int Usos)> etiquetas, string? etiqueta, ColorNombre? color)
    {
        _secciones.Children.Clear();
        foreach (var (clave, contar, texto, glifo) in pestana == Pestana.Animados ? SeccionesAnimados : Secciones)
        {
            if (clave == Historial) _secciones.Children.Add(new Border { Height = 1, Margin = new Thickness(4, 6, 4, 6), Background = Recurso("DividerStrokeColorDefaultBrush") });
            var n = contar is { } sec && cuantas.TryGetValue(sec, out var c) ? $" ({c})" : "";
            var boton = Boton(glifo, texto + n, clave == seccionActiva);
            boton.Click += (_, _) => SeccionCambiada?.Invoke(this, clave);
            _secciones.Children.Add(boton);
        }

        _etiquetas.Children.Clear();
        // La etiqueta activa se ve siempre (aunque ya no tenga imágenes o no esté entre las más
        // usadas): es la única forma de quitar ese filtro.
        var visibles = etiquetas.Take(MaximoEtiquetas).ToList();
        if (etiqueta is not null && !visibles.Any(v => string.Equals(v.Etiqueta, etiqueta, StringComparison.OrdinalIgnoreCase)))
            visibles.Add(etiquetas.FirstOrDefault(v => string.Equals(v.Etiqueta, etiqueta, StringComparison.OrdinalIgnoreCase)) is { Etiqueta: not null } t
                ? t : (etiqueta, 0));
        if (visibles.Count == 0)
            _etiquetas.Children.Add(new TextBlock { Text = "Aún no hay etiquetas.", FontSize = 12, Margin = new Thickness(10, 0, 0, 0), Foreground = Recurso("TextFillColorTertiaryBrush") });
        foreach (var (e, usos) in visibles)
        {
            var activa = string.Equals(e, etiqueta, StringComparison.OrdinalIgnoreCase);
            var boton = Boton("", $"{e} ({usos})", activa);
            boton.Click += (_, _) => EtiquetaCambiada?.Invoke(this, activa ? null : e);
            _etiquetas.Children.Add(boton);
        }

        _colores.Children.Clear();
        var colores = Enum.GetValues<ColorNombre>();
        for (var i = 0; i < colores.Length; i++)
        {
            var cn = colores[i];
            var activo = cn == color;
            var muestra = new Button
            {
                Width = 22, Height = 22, Padding = new Thickness(0), CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Muestra(cn)),
                BorderBrush = Recurso(activo ? "AccentFillColorDefaultBrush" : "ControlStrongStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(activo ? 3 : 1),
            };
            var nombre = Nombre(cn);
            ToolTipService.SetToolTip(muestra, activo ? $"{nombre} (quitar filtro)" : nombre);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(muestra, "Color " + nombre);
            muestra.Click += (_, _) => ColorCambiado?.Invoke(this, activo ? null : cn);
            Grid.SetColumn(muestra, i % 6);
            Grid.SetRow(muestra, i / 6);
            _colores.Children.Add(muestra);
        }
    }

    public static string Nombre(ColorNombre c) => c switch
    {
        ColorNombre.Marron => "Marrón",
        _ => c.ToString(),
    };

    private static global::Windows.UI.Color Muestra(ColorNombre c) => c switch
    {
        ColorNombre.Rojo => Rgb(220, 40, 40),
        ColorNombre.Naranja => Rgb(245, 140, 30),
        ColorNombre.Amarillo => Rgb(245, 210, 40),
        ColorNombre.Verde => Rgb(60, 170, 70),
        ColorNombre.Cian => Rgb(30, 190, 210),
        ColorNombre.Azul => Rgb(40, 100, 220),
        ColorNombre.Morado => Rgb(140, 70, 200),
        ColorNombre.Rosa => Rgb(235, 100, 170),
        ColorNombre.Marron => Rgb(120, 75, 35),
        ColorNombre.Blanco => Rgb(250, 250, 250),
        ColorNombre.Gris => Rgb(140, 140, 140),
        _ => Rgb(20, 20, 20),
    };

    private static global::Windows.UI.Color Rgb(byte r, byte g, byte b) => global::Windows.UI.Color.FromArgb(255, r, g, b);

    private static Button Boton(string glifo, string texto, bool activo)
    {
        var b = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 6, 10, 6),
            Background = activo ? Recurso("SubtleFillColorSecondaryBrush") : Recurso("SubtleFillColorTransparentBrush"),
            BorderThickness = new Thickness(0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new FontIcon { Glyph = glifo, FontSize = 14, Foreground = activo ? Recurso("AccentTextFillColorPrimaryBrush") : Recurso("TextFillColorSecondaryBrush") },
                    new TextBlock { Text = texto, FontWeight = activo ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal },
                },
            },
        };
        return b;
    }

    private static TextBlock Titulo(string texto) => new()
    {
        Text = texto,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        Margin = new Thickness(10, 14, 0, 2),
    };

    private static Brush Recurso(string clave) => (Brush)Application.Current.Resources[clave];
}
