using System.Globalization;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DeskWallpaper.App.Vistas;

/// <summary>Columna derecha: detalles y encuadre de la zona seleccionada.</summary>
public sealed partial class PanelZona : UserControl
{
    private bool _actualizando;

    public event EventHandler<double>? ZoomCambiado;
    public event EventHandler? CentrarPulsado;
    public event EventHandler? CambiarPulsado;
    public event EventHandler? QuitarPulsado;

    /// <summary>El interruptor «Reducir al tamaño de los monitores» (la opción de «Reproducción…», para todos los vídeos).</summary>
    public event EventHandler<bool>? ReducirCambiado;

    /// <summary>«Ampliar con IA…» en el aviso de calidad.</summary>
    public event EventHandler? AmpliarPulsado;

    private readonly HyperlinkButton _ampliar = new() { Content = "Ampliar con IA…" };

    public PanelZona()
    {
        InitializeComponent();
        _ampliar.Click += (_, _) => AmpliarPulsado?.Invoke(this, EventArgs.Empty);
        AvisoCalidad.ActionButton = _ampliar;
    }

    /// <param name="factorMaximo">Mayor ampliación de los monitores de la zona, si supera el umbral de aviso.</param>
    /// <param name="esVideo">La zona tiene un fondo animado: sin herramientas de IA.</param>
    /// <param name="reducir">Con un vídeo: si la opción de reducir está activada.</param>
    /// <param name="tamanoReducido">Con un vídeo: el tamaño de la copia que se usaría (null si ya cabe en sus monitores).</param>
    public void Mostrar(Zona? zona, Disposicion d, AsignacionZona? asignacion, SizePx? original, double? factorMaximo,
                        bool esVideo = false, bool reducir = true, SizePx? tamanoReducido = null)
    {
        SinSeleccion.Visibility = zona is null ? Visibility.Visible : Visibility.Collapsed;
        Detalles.Visibility = zona is null ? Visibility.Collapsed : Visibility.Visible;
        if (zona is null) return;

        var numeros = zona.MonitorIds.Select(id => d.Buscar(id)?.Numero.ToString(CultureInfo.InvariantCulture) ?? "?").ToList();
        Titulo.Text = numeros.Count > 1 ? "Monitores " + string.Join(" + ", numeros) : "Monitor " + numeros[0];

        var conImagen = asignacion is not null;
        Detalle.Text = conImagen
            ? (esVideo ? "▶ " : "") + Path.GetFileName(asignacion!.RutaImagen) + (original is { } o ? $" · {o.Ancho}×{o.Alto}" : "")
            : "Sin imagen: al aplicar, quedará en negro. Arrastra una desde el Explorador o pulsa «Elegir imagen…».";
        PanelEncuadre.Visibility = conImagen ? Visibility.Visible : Visibility.Collapsed;
        BotonQuitar.Visibility = conImagen ? Visibility.Visible : Visibility.Collapsed;
        BotonCambiar.Content = conImagen ? "Cambiar imagen…" : "Elegir imagen…";

        _actualizando = true;
        DeslizadorZoom.Value = asignacion?.Encuadre.Zoom ?? 1;
        InterruptorReducir.IsOn = reducir;
        _actualizando = false;

        PanelVideo.Visibility = conImagen && esVideo ? Visibility.Visible : Visibility.Collapsed;
        // Qué pasa y por qué.
        var tamanoVideo = original is { } o1 ? $" ({o1.Ancho}×{o1.Alto})" : "";
        TextoReducir.Text = !reducir
            ? $"Se usa el vídeo original{tamanoVideo}" + (tamanoReducido is not null ? ", aunque sea más grande que los monitores." : ".")
            : tamanoReducido is { } t
                ? $"El vídeo{tamanoVideo} es más grande que estos monitores. Se usará una copia de {t.Ancho}×{t.Alto}: " +
                  "se ve igual y gasta menos memoria."
                : "El vídeo no es más grande de lo que muestran estos monitores, así que no se reduce.";
        TextoZoom.Text = $"{DeslizadorZoom.Value:0.0}×";

        AvisoCalidad.IsOpen = factorMaximo is not null;
        if (factorMaximo is { } f) AvisoCalidad.Title = $"{(esVideo ? "El vídeo" : "La imagen")} se ampliará un {Math.Round((f - 1) * 100)} %";
        AvisoCalidad.ActionButton = esVideo ? null : _ampliar;
    }

    private void DeslizadorZoom_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Durante la carga del XAML (Minimum="1" corrige el valor inicial) el resto de controles aún no existe.
        if (TextoZoom is null) return;
        TextoZoom.Text = $"{e.NewValue:0.0}×";
        if (!_actualizando) ZoomCambiado?.Invoke(this, e.NewValue);
    }

    private void InterruptorReducir_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_actualizando) ReducirCambiado?.Invoke(this, InterruptorReducir.IsOn);
    }

    private void Centrar_Click(object sender, RoutedEventArgs e) => CentrarPulsado?.Invoke(this, EventArgs.Empty);
    private void Cambiar_Click(object sender, RoutedEventArgs e) => CambiarPulsado?.Invoke(this, EventArgs.Empty);
    private void Quitar_Click(object sender, RoutedEventArgs e) => QuitarPulsado?.Invoke(this, EventArgs.Empty);
}
