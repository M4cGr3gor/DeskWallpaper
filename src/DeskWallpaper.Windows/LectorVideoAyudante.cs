using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Windows;

/// <summary>
/// Lee los vídeos con el modo <c>--info</c> del ayudante en C++: un solo
/// camino para leer vídeo, así que lo que entra en la biblioteca se puede reproducir como fondo.
/// </summary>
public sealed class LectorVideoAyudante(string rutaAyudante) : ILectorVideo
{
    private static readonly TimeSpan Espera = TimeSpan.FromSeconds(30);

    public InfoVideo Leer(string rutaVideo, string rutaFotogramaJpg)
    {
        var nombre = Path.GetFileName(rutaVideo);
        var inicio = new ProcessStartInfo(rutaAyudante)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        inicio.ArgumentList.Add("--info");
        inicio.ArgumentList.Add(rutaVideo);
        inicio.ArgumentList.Add(rutaFotogramaJpg);

        Process? proceso;
        try { proceso = Process.Start(inicio); }
        catch (Win32Exception ex)
        {
            throw new ExcepcionImagen("No se encuentra el ayudante de fondos animados (DeskWallpaper.Animado.exe)", ex);
        }
        using (proceso)
        {
            if (proceso is null) throw new ExcepcionImagen("No se ha podido arrancar el ayudante de fondos animados");
            var salida = proceso.StandardOutput.ReadToEndAsync();
            _ = proceso.StandardError.ReadToEndAsync();
            if (!proceso.WaitForExit(Espera))
            {
                try { proceso.Kill(); } catch (InvalidOperationException) { /* ya había terminado */ }
                throw new ExcepcionImagen($"No se puede leer el vídeo «{nombre}»: tarda demasiado");
            }
            if (proceso.ExitCode != 0 || !File.Exists(rutaFotogramaJpg))
                throw new ExcepcionImagen($"No se puede leer el vídeo «{nombre}»");
            return Analizar(salida.GetAwaiter().GetResult());
        }
    }

    /// <summary>La línea JSON de <c>--info</c>: <c>{"ancho":3840,"alto":1080,"duracionMs":20000}</c>.</summary>
    internal static InfoVideo Analizar(string salida)
    {
        try
        {
            using var doc = JsonDocument.Parse(salida.Trim());
            var r = doc.RootElement;
            var ancho = r.GetProperty("ancho").GetInt32();
            var alto = r.GetProperty("alto").GetInt32();
            var ms = r.GetProperty("duracionMs").GetInt64();
            if (ancho <= 0 || alto <= 0 || ms < 0) throw new ExcepcionImagen("El ayudante ha devuelto datos no válidos");
            return new InfoVideo(new SizePx(ancho, alto), TimeSpan.FromMilliseconds(ms));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new ExcepcionImagen("El ayudante ha devuelto datos no válidos", ex);
        }
    }
}
