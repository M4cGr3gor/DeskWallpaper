using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace DeskWallpaper.App.Vistas;

/// <summary>Franja inferior con las combinaciones aplicadas.</summary>
public sealed class FranjaHistorial : UserControl
{
    private readonly StackPanel _elementos = new() { Orientation = Orientation.Horizontal, Spacing = 10 };
    private readonly TextBlock _vacio = new()
    {
        Text = "Aquí aparecerán las combinaciones que apliques.",
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        VerticalAlignment = VerticalAlignment.Center,
    };

    public event EventHandler<EntradaHistorial>? Elegida;
    public event EventHandler<EntradaHistorial>? Quitar;

    public FranjaHistorial()
    {
        // El título "Historial" lo pone la cabecera de la sección.
        Content = new ScrollViewer
        {
            Height = 130,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = new Grid { Children = { _vacio, _elementos } },
        };
    }

    public async Task MostrarAsync(IReadOnlyList<EntradaHistorial> entradas, DateTime ahora)
    {
        // Se prepara todo aparte y solo pinta la llamada más reciente: dos refrescos seguidos
        // (p. ej. quitar dos entradas) no deben mezclar ni duplicar elementos.
        var version = ++_version;
        var nuevos = new List<Button>();
        foreach (var e in entradas)
        {
            nuevos.Add(await ElementoAsync(e, ahora));
            if (version != _version) return;
        }

        _elementos.Children.Clear();
        foreach (var b in nuevos) _elementos.Children.Add(b);
        _vacio.Visibility = entradas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private int _version;

    private async Task<Button> ElementoAsync(EntradaHistorial e, DateTime ahora)
    {
        var faltan = Historial.ImagenesQueFaltan(e.Combinacion);
        var imagen = new Image { Source = await ImagenesPng.CargarAsync(e.Miniatura), Height = 84, Stretch = Stretch.Uniform };
        var contenido = new Grid { RowSpacing = 4 };
        contenido.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contenido.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contenido.Children.Add(imagen);
        var fecha = new TextBlock { Text = Relativa(e.UltimoUso, ahora), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        Grid.SetRow(fecha, 1);
        contenido.Children.Add(fecha);

        var nombres = string.Join(", ", e.Combinacion.Zonas.Select(z => Path.GetFileName(z.RutaImagen)));
        var boton = new Button { Content = contenido, Padding = new Thickness(6) };
        if (faltan.Count > 0)
        {
            imagen.Opacity = 0.45;
            contenido.Children.Add(new FontIcon
            {
                Glyph = "", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4), Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            });
            ToolTipService.SetToolTip(boton, "Faltan imágenes: " + string.Join(", ", faltan.Select(Path.GetFileName)));
        }
        else
        {
            ToolTipService.SetToolTip(boton, $"{nombres}\nUsada {e.Usos} {(e.Usos == 1 ? "vez" : "veces")}");
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(boton, $"Historial: {nombres}");

        boton.Click += (_, _) => Elegida?.Invoke(this, e);
        var quitar = new MenuFlyoutItem { Text = "Quitar del historial", Icon = new FontIcon { Glyph = "" } };
        quitar.Click += (_, _) => Quitar?.Invoke(this, e);
        boton.ContextFlyout = new MenuFlyout { Items = { quitar } };
        return boton;
    }

    private static string Relativa(DateTime cuando, DateTime ahora)
    {
        var d = ahora - cuando;
        if (d.TotalMinutes < 1) return "ahora mismo";
        if (d.TotalHours < 1) return $"hace {(int)d.TotalMinutes} min";
        if (d.TotalDays < 1) return $"hace {(int)d.TotalHours} h";
        if (d.TotalDays < 2) return "ayer";
        if (d.TotalDays < 30) return $"hace {(int)d.TotalDays} días";
        return cuando.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("es-ES"));
    }
}
