using DeskWallpaper.App.Nativo;
using DeskWallpaper.Nucleo;
using Microsoft.UI.Xaml;

namespace DeskWallpaper.App;

/// <summary>Punto de entrada de la aplicación WinUI.</summary>
public partial class App : Application
{
    private MainWindow? _ventana;

    public App()
    {
        InitializeComponent();
        // Regla del diseño: un error inesperado no cierra la aplicación. Se apunta en error.log y se avisa.
        UnhandledException += (_, e) =>
        {
            RegistrarError(e.Exception);
            e.Handled = true;
            _ventana?.AvisarErrorInesperado(e.Exception);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Una sola copia por carpeta de datos: si ya hay una, se trae al frente y esta termina.
        if (!InstanciaUnica.Reclamar())
        {
            Exit();
            return;
        }
        try
        {
            _ventana = new MainWindow();
            _ventana.Mostrar();
        }
        catch (Exception ex)
        {
            RegistrarError(ex);
            throw;
        }
    }

    /// <summary>Deja constancia de un error no controlado en <c>error.log</c> de la carpeta de datos.</summary>
    public static void RegistrarError(Exception ex)
    {
        try
        {
            var carpeta = CarpetaDatos.Resolver(Environment.GetCommandLineArgs(),
                () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            File.AppendAllText(Path.Combine(carpeta, "error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch (Exception)
        {
            // Si ni siquiera se puede escribir el registro, no hay nada más que hacer.
        }
    }
}
