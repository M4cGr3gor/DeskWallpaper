using DeskWallpaper.Nucleo;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DeskWallpaper.App.IA;

/// <summary>
/// LaMa con ONNX Runtime, siempre en el procesador: con DirectML falla un nodo de su parte de Fourier
///. Un borrado tarda menos de un segundo y no ocupa memoria de vídeo.
/// </summary>
public sealed class RellenoLama : IModeloRelleno
{
    private const int Lado = IModeloRelleno.Lado;

    private readonly SessionOptions _opciones;
    private readonly InferenceSession _sesion;
    private readonly string _entradaImagen;
    private readonly string _entradaMascara;

    public RellenoLama(string rutaModelo)
    {
        _opciones = new SessionOptions();
        try
        {
            _sesion = new InferenceSession(rutaModelo, _opciones);
        }
        catch
        {
            _opciones.Dispose();
            throw;
        }
        // Nombres según la ficha del modelo: «image» y «mask»; se buscan por si cambian de orden.
        var entradas = _sesion.InputMetadata.Keys.ToList();
        _entradaImagen = entradas.FirstOrDefault(n => n.Contains("image", StringComparison.OrdinalIgnoreCase)) ?? entradas[0];
        _entradaMascara = entradas.FirstOrDefault(n => n.Contains("mask", StringComparison.OrdinalIgnoreCase)) ?? entradas[1];
    }

    public float[] Rellenar(float[] imagen, float[] mascara)
    {
        var entradas = new[]
        {
            NamedOnnxValue.CreateFromTensor(_entradaImagen, new DenseTensor<float>(imagen, [1, 3, Lado, Lado])),
            NamedOnnxValue.CreateFromTensor(_entradaMascara, new DenseTensor<float>(mascara, [1, 1, Lado, Lado])),
        };
        using var resultados = _sesion.Run(entradas);
        return resultados[0].AsTensor<float>().ToArray();
    }

    public void Dispose()
    {
        _sesion.Dispose();
        _opciones.Dispose();
    }
}
