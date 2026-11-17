using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// "¿Mantener este fondo?" con cuenta atrás. Si se agota el tiempo o se cierra, se vuelve atrás.
/// </summary>
public static class VentanaPrueba
{
    public const int Segundos = 15;

    private static Window? _abierta;

    /// <summary>Cierra la ventana de prueba si hay una (cuenta como "Volver").</summary>
    public static void CerrarAbierta()
    {
        try { _abierta?.Close(); } catch (Exception) { /* ya se estaba cerrando */ }
    }

    /// <param name="monitor">Rectángulo (px físicos) del monitor donde centrar la ventana.</param>
    /// <param name="escala">Escala de ese monitor.</param>
    /// <returns>True si el usuario pulsa "Mantener".</returns>
    public static Task<bool> PreguntarAsync(global::Windows.Graphics.RectInt32 monitor, double escala, string icono)
    {
        // Continuaciones fuera de los manejadores de la ventana (p. ej. de Closed) para no reentrar en ella.
        var resultado = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cerrada = false;
        var restantes = Segundos;
        var texto = new TextBlock { FontSize = 18, TextWrapping = TextWrapping.Wrap };
        var mantener = new Button { Content = "Mantener", Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 110 };
        var volver = new Button { Content = "Volver", MinWidth = 110 };
        var raiz = new Grid { Padding = new Thickness(24, 16, 24, 20), RowSpacing = 16 };
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var botones = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { mantener, volver } };
        Grid.SetRow(botones, 1);
        raiz.Children.Add(texto);
        raiz.Children.Add(botones);

        var ventana = new Window { Content = raiz, Title = "Probar fondos", SystemBackdrop = new MicaBackdrop() };
        ventana.AppWindow.SetIcon(icono);
        if (ventana.AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsResizable = false;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        var ancho = (int)(400 * escala);
        var alto = (int)(170 * escala);
        ventana.AppWindow.MoveAndResize(new global::Windows.Graphics.RectInt32(
            monitor.X + (monitor.Width - ancho) / 2, monitor.Y + (monitor.Height - alto) / 2, ancho, alto));

        var reloj = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Actualizar() => texto.Text = $"¿Mantener este fondo? Se volverá al anterior en {restantes} s.";
        void Terminar(bool valor)
        {
            if (!resultado.TrySetResult(valor)) return;
            reloj.Stop();
            if (cerrada) return; // ya se está cerrando: llamar a Close() aquí dentro puede fallar
            try { ventana.Close(); } catch (Exception) { /* ya se estaba cerrando */ }
        }

        reloj.Tick += (_, _) =>
        {
            restantes--;
            if (restantes <= 0) Terminar(false);
            else Actualizar();
        };
        mantener.Click += (_, _) => Terminar(true);
        volver.Click += (_, _) => Terminar(false);
        ventana.Closed += (_, _) =>
        {
            cerrada = true;
            if (ReferenceEquals(_abierta, ventana)) _abierta = null;
            Terminar(false);
        };

        Actualizar();
        _abierta = ventana;
        ventana.Activate();
        reloj.Start();
        return resultado.Task;
    }
}
