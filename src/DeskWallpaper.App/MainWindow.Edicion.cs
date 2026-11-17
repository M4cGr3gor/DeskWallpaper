using DeskWallpaper.App.Vistas;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml.Controls;

namespace DeskWallpaper.App;

/// <summary>Parte de la ventana principal que abre la ventana de edición con IA.</summary>
public sealed partial class MainWindow
{
    private GestorModelos? _modelosIA;

    /// <summary>Modelos de IA en &lt;datos&gt;\modelos; se descargan la primera vez que se usan.</summary>
    private GestorModelos ModelosIA_ => _modelosIA ??= new GestorModelos(Path.Combine(_carpetaDatos, "modelos"), new HttpClientHandler());

    private Task EditarAsync(ImagenBiblioteca img, HerramientaEdicion herramienta) =>
        AvisarSiNoDisponible(img) ? AbrirEdicionAsync(img.Ruta, herramienta) : Task.CompletedTask;

    /// <summary>
    /// «Ampliar con IA…» desde el aviso de calidad: propone el factor que deja la imagen sin estirar y, al guardar,
    /// la zona pasa a usar la imagen ampliada con el mismo encuadre (es relativo, así que no cambia lo que se ve).
    /// </summary>
    private void AmpliarZonaSeleccionada() => ConZonaSeleccionada((zona, a, original) =>
    {
        var factor = PlanRecorte.Calcular(original, zona.MonitorIds, _disposicion, _lienzo, a.Encuadre).Max(p => p.Factor);
        var ruta = a.RutaImagen;
        // La imagen que tiene ahora la zona: cambia con cada «Guardar como», así manda siempre la última guardada.
        var enLaZona = ruta;
        _ = Protegido(() => AbrirEdicionAsync(ruta, HerramientaEdicion.Ampliar, factor, nueva =>
        {
            if (_cerrando) return;
            if (!_asignaciones.TryGetValue(zona.Id, out var actual) || !string.Equals(actual.RutaImagen, enLaZona, StringComparison.OrdinalIgnoreCase)) return;
            _asignaciones[zona.Id] = actual with { RutaImagen = nueva };
            enLaZona = nueva;
            GuardarLienzo();
            Refrescar();
        }));
    });

    /// <summary>Al cerrar el editor o el visor se vuelve a la ventana principal, si sigue abierta.</summary>
    private void ActivarSiAbierta()
    {
        if (!_cerrando) Activate();
    }

    /// <summary>Abre la ventana de edición en el monitor de la ventana principal.</summary>
    private async Task AbrirEdicionAsync(string ruta, HerramientaEdicion herramienta, double? factorZona = null, Action<string>? alGuardar = null)
    {
        if (FormatosVideo.EsVideo(ruta))
        {
            MostrarResultado(InfoBarSeverity.Informational, "Las herramientas de IA son solo para imágenes", "");
            return;
        }
        var (rect, _) = MonitorDeLaVentana();
        try
        {
            await VentanaEdicion.AbrirAsync(new OpcionesEdicion(ruta, herramienta, rect, ModelosIA_, factorZona, alGuardar, ActivarSiAbierta));
        }
        catch (ExcepcionImagen ex)
        {
            MostrarResultado(InfoBarSeverity.Error, "No se puede abrir la imagen", ex.Message);
        }
    }
}
