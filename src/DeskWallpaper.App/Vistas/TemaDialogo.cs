using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Muestra los diálogos con el tema claro u oscuro que tiene su ventana en ese momento. Si el usuario cambia el
/// tema de Windows con el programa abierto, sin esto el fondo del diálogo se quedaba con el tema del arranque y
/// el texto de los botones con el nuevo (texto blanco sobre fondo claro: «Cancelar» y «Descartar» no se veían).
/// </summary>
public static class TemaDialogo
{
    public static global::Windows.Foundation.IAsyncOperation<ContentDialogResult> MostrarAsync(this ContentDialog dialogo)
    {
        if (dialogo.XamlRoot?.Content is FrameworkElement raiz) dialogo.RequestedTheme = raiz.ActualTheme;
        return dialogo.ShowAsync();
    }
}
