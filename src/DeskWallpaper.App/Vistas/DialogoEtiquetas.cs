using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Etiquetas de una imagen: chips con × para quitar y un cuadro con autocompletado
/// de las etiquetas existentes; Intro añade. "Guardar" deja el resultado en <see cref="Etiquetas"/>.
/// </summary>
public sealed class DialogoEtiquetas : ContentDialog
{
    private readonly List<string> _etiquetas;
    private readonly IReadOnlyList<string> _existentes;
    private readonly PanelFlujo _chips = new();
    private readonly TextBlock _sinEtiquetas;
    private readonly AutoSuggestBox _cuadro;

    public IReadOnlyList<string> Etiquetas => _etiquetas;

    public DialogoEtiquetas(ImagenBiblioteca imagen, IReadOnlyList<string> existentes)
    {
        _etiquetas = [.. imagen.Etiquetas];
        _existentes = existentes;
        Title = "Etiquetas";
        PrimaryButtonText = "Guardar";
        CloseButtonText = "Cancelar";
        // Sin botón por defecto: Intro en el cuadro añade la etiqueta, no guarda.
        DefaultButton = ContentDialogButton.None;

        _sinEtiquetas = new TextBlock
        {
            Text = "Sin etiquetas todavía.",
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        _cuadro = new AutoSuggestBox { PlaceholderText = "Nueva etiqueta (Intro para añadir)", QueryIcon = new SymbolIcon(Symbol.Add) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_cuadro, "Nueva etiqueta");
        _cuadro.TextChanged += (s, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput) s.ItemsSource = Sugerencias(s.Text);
        };
        _cuadro.QuerySubmitted += (s, e) =>
        {
            Anadir(e.ChosenSuggestion as string ?? e.QueryText);
            s.Text = "";
            s.ItemsSource = null;
        };

        Content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
            Children =
            {
                new TextBlock { Text = imagen.Nombre, TextTrimming = TextTrimming.CharacterEllipsis },
                _sinEtiquetas,
                _chips,
                _cuadro,
            },
        };
        // Lo escrito y no añadido también cuenta al guardar.
        PrimaryButtonClick += (_, _) => Anadir(_cuadro.Text);
        Opened += (_, _) => _cuadro.Focus(FocusState.Programmatic);
        Pintar();
    }

    private List<string> Sugerencias(string texto)
    {
        var t = FiltroBiblioteca.Normalizar(texto.Trim());
        return _existentes.Where(e => !_etiquetas.Contains(e, StringComparer.OrdinalIgnoreCase)
                                      && (t.Length == 0 || FiltroBiblioteca.Normalizar(e).Contains(t, StringComparison.Ordinal)))
                          .Take(10).ToList();
    }

    private void Anadir(string texto)
    {
        var e = texto.Trim();
        if (e.Length == 0 || _etiquetas.Contains(e, StringComparer.OrdinalIgnoreCase)) return;
        // Si ya existe con otras mayúsculas, se usa la existente para no duplicarla.
        _etiquetas.Add(_existentes.FirstOrDefault(x => string.Equals(x, e, StringComparison.OrdinalIgnoreCase)) ?? e);
        Pintar();
    }

    private void Pintar()
    {
        _chips.Children.Clear();
        foreach (var e in _etiquetas)
        {
            var quitar = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 10 },
                Padding = new Thickness(4),
                MinWidth = 0,
                MinHeight = 0,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(quitar, $"Quitar etiqueta {e}");
            var etiqueta = e;
            quitar.Click += (_, _) => { _etiquetas.Remove(etiqueta); Pintar(); };
            _chips.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 2, 4, 2),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { new TextBlock { Text = e, VerticalAlignment = VerticalAlignment.Center }, quitar },
                },
            });
        }
        _sinEtiquetas.Visibility = _etiquetas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
