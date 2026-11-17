using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App.Vistas;

/// <summary>«Reproducción…» de la pestaña Fondos animados.</summary>
internal static class DialogoReproduccion
{
    /// <returns>Las opciones elegidas, o null si se cancela.</returns>
    public static async Task<OpcionesReproduccion?> PedirAsync(XamlRoot raiz, OpcionesReproduccion actuales)
    {
        var pausar = new ToggleSwitch
        {
            Header = "Pausar con ventanas maximizadas o a pantalla completa",
            IsOn = actuales.PausarMaximizadas,
            OnContent = "Sí",
            OffContent = "No",
        };
        var fps = new RadioButtons { Header = "Fotogramas por segundo" };
        var treinta = new RadioButton { Content = "30 (gasta menos)", Tag = 30 };
        var comoVideo = new RadioButton { Content = "Como el vídeo", Tag = 0 };
        fps.Items.Add(treinta);
        fps.Items.Add(comoVideo);
        fps.SelectedItem = actuales.TopeFps == 0 ? comoVideo : treinta;
        var reducir = new ToggleSwitch
        {
            Header = "Reducir los vídeos más grandes que sus monitores (gasta menos memoria)",
            IsOn = actuales.ReducirVideos,
            OnContent = "Sí",
            OffContent = "No",
        };
        var nota = new TextBlock
        {
            Text = "Con una ventana maximizada se deja de dibujar ese monitor; el vídeo se para del todo cuando no se ve " +
                   "ninguno de sus monitores. Al reducir, se hace una copia del tamaño de los monitores la primera vez que se aplica " +
                   "(el original no se toca). Los fondos animados nunca tienen sonido.",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        var dialogo = new ContentDialog
        {
            XamlRoot = raiz,
            Title = "Reproducción",
            Content = new StackPanel { Spacing = 16, MinWidth = 420, Children = { pausar, fps, reducir, nota } },
            PrimaryButtonText = "Guardar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialogo.MostrarAsync() != ContentDialogResult.Primary) return null;
        return new OpcionesReproduccion(pausar.IsOn, fps.SelectedItem is RadioButton { Tag: int tope } ? tope : 30, reducir.IsOn);
    }
}
