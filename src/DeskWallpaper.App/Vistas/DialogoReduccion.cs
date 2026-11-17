using System.Diagnostics;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Al aplicar o probar con la opción «Reducir los vídeos más grandes que sus monitores»: si hay que hacer alguna copia,
/// La hace con barra de progreso y botón de cancelar (en las esperas, ver qué pasa y poder
/// cancelar). Si las copias ya están en la caché, no se ve nada.
/// </summary>
internal static class DialogoReduccion
{
    /// <param name="registrar">Recibe el token de cancelación mientras dura (para cortarlo si se cierra la ventana) y null al acabar.</param>
    /// <returns>La configuración con las copias reducidas, o null si el usuario cancela.</returns>
    public static async Task<ConfigAnimado?> PrepararAsync(XamlRoot raiz, VideosReducidos reducidos, ConfigAnimado c,
                                                           Disposicion d, List<string> avisos,
                                                           Action<CancellationTokenSource?> registrar)
    {
        if (await Task.Run(() => reducidos.Pendientes(c, d)) == 0)
            return await Task.Run(() => reducidos.Preparar(c, d, avisos, null, CancellationToken.None));

        var explicacion = new TextBlock
        {
            Text = "El vídeo es más grande que los monitores donde se ve: se hace una copia de su tamaño para que gaste " +
                   "menos memoria. Solo la primera vez; el original no se toca.",
            TextWrapping = TextWrapping.Wrap,
        };
        var barra = new ProgressBar { Maximum = 1, IsIndeterminate = true };
        var estado = new TextBlock
        {
            Text = "Empezando…",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };
        var dialogo = new ContentDialog
        {
            XamlRoot = raiz,
            Title = "Preparando el vídeo para tus monitores",
            Content = new StackPanel { Spacing = 12, MinWidth = 420, Children = { explicacion, barra, estado } },
            CloseButtonText = "Cancelar",
        };

        using var cts = new CancellationTokenSource();
        registrar(cts);
        var terminado = false;
        // Cualquier cierre antes de terminar (botón, Esc o la ventana que se cierra) cancela.
        dialogo.Closing += (_, _) => { if (!terminado) cts.Cancel(); };

        var reloj = Stopwatch.StartNew();
        TimeSpan? ultimo = null;   // no TimeSpan.MinValue: «Elapsed - MinValue» desborda y tumbaba la app (0xC000027B)
        IProgress<ProgresoReduccion> progreso = new Progress<ProgresoReduccion>(p =>
        {
            // Sin saturar la interfaz: como mucho diez veces por segundo.
            if (ultimo is { } u && reloj.Elapsed - u < TimeSpan.FromMilliseconds(100) && p.Fraccion < 1) return;
            ultimo = reloj.Elapsed;
            barra.IsIndeterminate = false;
            barra.Value = p.Fraccion;
            estado.Text = $"«{p.Nombre}»" + (p.Total > 1 ? $" ({p.Indice} de {p.Total})" : "") + $" · {p.Fraccion:P0}";
        });

        var trabajo = Task.Run(() => reducidos.Preparar(c, d, avisos, progreso.Report, cts.Token));
        var mostrado = dialogo.MostrarAsync().AsTask();
        ConfigAnimado? resultado = null;
        try
        {
            resultado = await trabajo;
        }
        catch (OperationCanceledException)
        {
            // Cancelado: no queda nada a medias en la caché.
        }
        finally
        {
            terminado = true;
            registrar(null);
            try { dialogo.Hide(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* ya cerrado */ }
        }
        await mostrado;
        return resultado;
    }
}
