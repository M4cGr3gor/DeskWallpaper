using DeskWallpaper.Nucleo;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DeskWallpaper.App.IA;

/// <summary>
/// Real-ESRGAN ×4 con ONNX Runtime: en la tarjeta gráfica con DirectML (unos 0,55 s por bloque de 512 px en la
/// RTX 5070) y, si no se puede o falla, en el procesador (unas 15 veces más lento).
/// </summary>
public sealed class AmpliacionEsrgan : IModeloAmpliacion
{
    private readonly string _ruta;
    private SessionOptions _opciones;
    private InferenceSession _sesion;

    private AmpliacionEsrgan(string ruta, SessionOptions opciones, InferenceSession sesion, bool grafica)
    {
        _ruta = ruta;
        _opciones = opciones;
        _sesion = sesion;
        UsaGrafica = grafica;
    }

    public bool UsaGrafica { get; private set; }

    public int Bloque => UsaGrafica ? 512 : 256;

    /// <summary>Con DirectML si se puede; si no, en el procesador.</summary>
    public static AmpliacionEsrgan Crear(string rutaModelo)
    {
        try
        {
            var (opciones, sesion) = Abrir(rutaModelo, grafica: true);
            return new AmpliacionEsrgan(rutaModelo, opciones, sesion, true);
        }
        catch (Exception ex) when (FalloDeGrafica(ex))
        {
            var (opciones, sesion) = Abrir(rutaModelo, grafica: false);
            return new AmpliacionEsrgan(rutaModelo, opciones, sesion, false);
        }
    }

    public float[] Ampliar(float[] bloque, int ancho, int alto)
    {
        try
        {
            return Ejecutar(bloque, ancho, alto);
        }
        catch (Exception ex) when (UsaGrafica && FalloDeGrafica(ex))
        {
            // La gráfica ha fallado (p. ej. sin memoria de vídeo): se sigue en el procesador con el mismo bloque.
            // La sesión nueva se abre antes de soltar la vieja: si fallara, la ventana sigue con algo válido.
            var (opciones, sesion) = Abrir(_ruta, grafica: false);
            _sesion.Dispose();
            _opciones.Dispose();
            (_opciones, _sesion) = (opciones, sesion);
            UsaGrafica = false;
            return Ejecutar(bloque, ancho, alto);
        }
    }

    /// <summary>Errores por los que vale la pena probar en el procesador: los de ONNX y los de cargar DirectML.</summary>
    private static bool FalloDeGrafica(Exception ex) =>
        ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;

    private float[] Ejecutar(float[] bloque, int ancho, int alto)
    {
        var nombre = _sesion.InputMetadata.Keys.First();
        var entrada = NamedOnnxValue.CreateFromTensor(nombre, new DenseTensor<float>(bloque, [1, 3, alto, ancho]));
        using var resultados = _sesion.Run([entrada]);
        return resultados[0].AsTensor<float>().ToArray();
    }

    private static (SessionOptions, InferenceSession) Abrir(string ruta, bool grafica)
    {
        var opciones = new SessionOptions();
        try
        {
            if (grafica)
            {
                // Lo que pide ONNX Runtime para el proveedor DirectML.
                opciones.EnableMemoryPattern = false;
                opciones.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                opciones.AppendExecutionProvider_DML(0);
            }
            return (opciones, new InferenceSession(ruta, opciones));
        }
        catch
        {
            opciones.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _sesion.Dispose();
        _opciones.Dispose();
    }
}
