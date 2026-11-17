using System.Diagnostics;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App.Vistas;

/// <summary>
/// Diálogos de la IA: descarga de un modelo con barra y mensajes de lo que está pasando
/// y elección del factor de ampliación.
/// </summary>
public static class DialogosIA
{
    /// <summary>
    /// Si el modelo no está, avisa de qué se va a bajar y cuánto ocupa, y lo descarga con barra de progreso,
    /// velocidad, tiempo restante y botón de cancelar. False si el usuario cancela.
    /// </summary>
    /// <param name="cierre">Se cancela al cerrar la ventana: corta la descarga y cierra el diálogo.</param>
    public static async Task<bool> AsegurarModeloAsync(XamlRoot raiz, GestorModelos gestor, ModeloIA modelo, CancellationToken cierre)
    {
        if (gestor.EstaDescargado(modelo)) return true;

        var (paraQue, nombre) = modelo == ModelosIA.RealEsrgan ? ("ampliar imágenes", "Real-ESRGAN")
            : modelo == ModelosIA.Scunet ? ("quitar artefactos y ruido", "SCUNet")
            : ("quitar marcas", "LaMa");
        var explicacion = new TextBlock
        {
            Text = $"Para {paraQue} se usa el modelo {nombre}, que ocupa {modelo.Megas} MB. Se descarga una sola vez de " +
                   "Hugging Face y se guarda en tu equipo; después funciona sin conexión.",
            TextWrapping = TextWrapping.Wrap,
        };
        var barra = new ProgressBar { Maximum = 1, Visibility = Visibility.Collapsed };
        var estado = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
        var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false };
        var dialogo = new ContentDialog
        {
            XamlRoot = raiz,
            Title = "Hace falta descargar un modelo de IA",
            Content = new StackPanel { Spacing = 12, MinWidth = 420, Children = { explicacion, barra, estado, error } },
            PrimaryButtonText = "Descargar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
        };

        var listo = false;
        CancellationTokenSource? cts = null;
        // Cualquier cierre del diálogo antes de terminar (botón, Esc o la ventana que se cierra) corta la descarga.
        dialogo.Closing += (_, _) => { if (!listo) cts?.Cancel(); };
        using var alCerrarVentana = cierre.Register(() =>
        {
            cts?.Cancel();
            try { dialogo.Hide(); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { /* ya no está */ }
        });
        dialogo.PrimaryButtonClick += async (_, e) =>
        {
            e.Cancel = true;   // el diálogo sigue abierto mientras se descarga
            dialogo.IsPrimaryButtonEnabled = false;
            error.IsOpen = false;
            barra.Visibility = Visibility.Visible;
            barra.IsIndeterminate = false;
            barra.Value = 0;
            estado.Text = "Conectando con Hugging Face…";
            cts = CancellationTokenSource.CreateLinkedTokenSource(cierre);

            var reloj = Stopwatch.StartNew();
            var ultimo = TimeSpan.Zero;
            var progreso = new Progress<ProgresoDescarga>(p =>
            {
                if (p.Comprobando)
                {
                    barra.IsIndeterminate = true;
                    estado.Text = p.Texto;
                    return;
                }
                // Sin saturar la interfaz: como mucho diez veces por segundo.
                if (reloj.Elapsed - ultimo < TimeSpan.FromMilliseconds(100) && p.Recibidos < p.Total) return;
                ultimo = reloj.Elapsed;
                barra.IsIndeterminate = false;   // tras comprobar un archivo, sigue el siguiente
                barra.Value = p.Fraccion;
                estado.Text = p.Texto + Ritmo(p, reloj.Elapsed);
            });

            try
            {
                await gestor.DescargarAsync(modelo, progreso, cts.Token);
                barra.IsIndeterminate = false;
                barra.Value = 1;
                estado.Text = "Modelo descargado";
                listo = true;
                await Task.Delay(1000);
                try { dialogo.Hide(); }
                catch (Exception) { /* la ventana se ha cerrado en ese segundo */ }
            }
            catch (OperationCanceledException)
            {
                // El usuario ha pulsado «Cancelar»: el diálogo ya se ha cerrado y no queda nada a medias.
            }
            catch (ExcepcionModelo ex)
            {
                barra.Visibility = Visibility.Collapsed;
                estado.Text = "";
                error.Title = ex.Message;
                error.IsOpen = true;
                dialogo.PrimaryButtonText = "Reintentar";
                dialogo.IsPrimaryButtonEnabled = true;
            }
        };

        await dialogo.MostrarAsync();
        return listo;
    }

    /// <summary>« · 12 MB/s · quedan unos 13 s» cuando ya hay datos para estimarlo.</summary>
    internal static string Ritmo(ProgresoDescarga p, TimeSpan transcurrido)
    {
        if (transcurrido.TotalSeconds < 1 || p.Recibidos <= 0) return "";
        var porSegundo = p.Recibidos / transcurrido.TotalSeconds;
        var restante = TimeSpan.FromSeconds((p.Total - p.Recibidos) / porSegundo);
        return $" · {porSegundo / (1024 * 1024):0.#} MB/s · quedan {Duracion(restante)}";
    }

    /// <summary>«unos 13 s» o «unos 3 min».</summary>
    internal static string Duracion(TimeSpan t) =>
        t.TotalSeconds < 90 ? $"unos {Math.Max(1, Math.Round(t.TotalSeconds))} s" : $"unos {Math.Round(t.TotalMinutes)} min";

    /// <summary>×1,5, ×2, ×3 o ×4 (solo los posibles, con el propuesto marcado). Null si se cancela.</summary>
    public static async Task<double?> PedirFactorAsync(XamlRoot raiz, SizePx tamano, double? factorZona)
    {
        var factores = Ampliador.Factores(tamano);
        var opciones = new RadioButtons { Header = "Tamaño del resultado" };
        foreach (var f in factores)
        {
            var r = Ampliador.Resultado(tamano, f);
            opciones.Items.Add(new RadioButton { Content = $"×{ArchivoImagen.TextoFactor(f)}  ({r.Ancho}×{r.Alto})", Tag = f });
        }
        opciones.SelectedIndex = Math.Max(0, factores.ToList().IndexOf(Ampliador.Propuesto(tamano, factorZona)));
        var faltan = Ampliador.Posibles.Count - factores.Count;

        // Ampliación personalizada: cualquier factor de ×1,1 a ×4 en décimas.
        var maximo = Ampliador.FactorMaximo(tamano);
        var personalizado = new RadioButton { Content = "Personalizado", Tag = "personalizado" };
        var deslizador = new Slider
        {
            Minimum = 1.1, Maximum = maximo, StepFrequency = 0.1, SmallChange = 0.1, LargeChange = 0.5,
            Value = Math.Clamp(Ampliador.Propuesto(tamano, factorZona), 1.1, maximo), Width = 260,
            IsThumbToolTipEnabled = false,
        };
        var textoPersonalizado = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
        void PintarPersonalizado()
        {
            var f = Math.Round(deslizador.Value, 1);
            var r = Ampliador.Resultado(tamano, f);
            textoPersonalizado.Text = $"×{ArchivoImagen.TextoFactor(f)}  ({r.Ancho}×{r.Alto})";
        }
        deslizador.ValueChanged += (_, _) => { PintarPersonalizado(); opciones.SelectedItem = personalizado; };
        PintarPersonalizado();
        var filaPersonalizada = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(28, -4, 0, 0),
            Children = { deslizador, textoPersonalizado },
            Visibility = maximo >= 1.1 ? Visibility.Visible : Visibility.Collapsed,
        };
        if (maximo >= 1.1) opciones.Items.Add(personalizado);

        var dialogo = new ContentDialog
        {
            XamlRoot = raiz,
            Title = "Ampliar con IA",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Se amplía con el modelo Real-ESRGAN. Antes de guardar podrás compararla con el original.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    opciones,
                    filaPersonalizada,
                    new TextBlock
                    {
                        // Por qué a veces hay menos opciones.
                        Text = $"Los factores mayores no se ofrecen porque la imagen pasaría de {Ampliador.LadoMaximo:N0} px de lado, " +
                               "demasiado grande para usarla de fondo.",
                        TextWrapping = TextWrapping.Wrap,
                        Visibility = faltan > 0 ? Visibility.Visible : Visibility.Collapsed,
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                    },
                },
            },
            PrimaryButtonText = "Ampliar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialogo.MostrarAsync() != ContentDialogResult.Primary) return null;
        return opciones.SelectedItem switch
        {
            RadioButton { Tag: double elegido } => elegido,
            RadioButton { Tag: "personalizado" } => Math.Round(deslizador.Value, 1),
            _ => null,
        };
    }

    /// <summary>Mensaje sencillo con un botón «Aceptar».</summary>
    public static async Task AvisarAsync(XamlRoot raiz, string titulo, string mensaje)
    {
        await new ContentDialog
        {
            XamlRoot = raiz,
            Title = titulo,
            Content = new TextBlock { Text = mensaje, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "Aceptar",
        }.MostrarAsync();
    }
}
