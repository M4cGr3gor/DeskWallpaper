using DeskWallpaper.Nucleo;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DeskWallpaper.App.IA;

/// <summary>
/// SCUNet («real PSNR») con ONNX Runtime para quitar artefactos JPEG y ruido. Siempre en el procesador: con DirectML
/// el proceso se cae con una violación de acceso que no se puede capturar.
/// Unos 0,6 s por bloque de 256 px. Los pesos van en el «.onnx.data» de al lado, que ONNX Runtime encuentra solo.
/// </summary>
public sealed class LimpiezaScunet : IModeloLimpieza
{
    private readonly SessionOptions _opciones;
    private readonly InferenceSession _sesion;
    private readonly string _entrada;

    public LimpiezaScunet(string rutaModelo)
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
        _entrada = _sesion.InputMetadata.Keys.First();   // «image»
    }

    public float[] Limpiar(float[] bloque, int ancho, int alto)
    {
        var entrada = NamedOnnxValue.CreateFromTensor(_entrada, new DenseTensor<float>(bloque, [1, 3, alto, ancho]));
        using var resultados = _sesion.Run([entrada]);
        return resultados[0].AsTensor<float>().ToArray();
    }

    public void Dispose()
    {
        _sesion.Dispose();
        _opciones.Dispose();
    }
}
