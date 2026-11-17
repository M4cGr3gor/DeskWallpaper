using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Windows;

/// <summary>
/// Copias reducidas de vídeos con el modo <c>--reducir</c> del ayudante en C++, que escribe «progreso 0.42» por línea y
/// «listo» al final. Cancelar cierra el ayudante; la salida a medias la borra quien llama.
/// </summary>
public sealed class ReductorVideoAyudante(string rutaAyudante) : IReductorVideo
{
    public void Reducir(string entrada, string salida, SizePx tamano, TimeSpan duracion, Action<double>? progreso,
                        CancellationToken ct)
    {
        var nombre = Path.GetFileName(entrada);
        var inicio = new ProcessStartInfo(rutaAyudante)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[]
                 {
                     "--reducir", entrada, salida, tamano.Ancho.ToString(CultureInfo.InvariantCulture),
                     tamano.Alto.ToString(CultureInfo.InvariantCulture),
                     ((long)duracion.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
                 })
            inicio.ArgumentList.Add(a);

        Process? proceso;
        try { proceso = Process.Start(inicio); }
        catch (Win32Exception ex)
        {
            throw new ExcepcionImagen("No se encuentra el ayudante de fondos animados (DeskWallpaper.Animado.exe)", ex);
        }
        using (proceso)
        {
            if (proceso is null) throw new ExcepcionImagen("No se ha podido arrancar el ayudante de fondos animados");
            using var alCancelar = ct.Register(() =>
            {
                try { proceso.Kill(); } catch (InvalidOperationException) { /* ya había terminado */ }
            });
            var errores = proceso.StandardError.ReadToEndAsync(CancellationToken.None);
            while (proceso.StandardOutput.ReadLine() is { } linea)
            {
                if (linea.StartsWith("progreso ", StringComparison.Ordinal) &&
                    double.TryParse(linea.AsSpan(9), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    progreso?.Invoke(Math.Clamp(f, 0, 1));
            }
            proceso.WaitForExit();
            ct.ThrowIfCancellationRequested();
            if (proceso.ExitCode != 0 || !File.Exists(salida))
            {
                var motivo = errores.GetAwaiter().GetResult().Trim();
                throw new ExcepcionImagen($"No se ha podido reducir «{nombre}»" + (motivo.Length > 0 ? $": {motivo}" : ""));
            }
            progreso?.Invoke(1);
        }
    }
}
