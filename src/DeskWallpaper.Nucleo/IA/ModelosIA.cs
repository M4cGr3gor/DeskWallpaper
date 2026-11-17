namespace DeskWallpaper.Nucleo;

/// <summary>
/// Modelo de relleno (LaMa, entrada fija de 512×512). La implementación con ONNX vive en la App;
/// las pruebas usan uno falso.
/// </summary>
public interface IModeloRelleno : IDisposable
{
    const int Lado = 512;

    /// <param name="imagen">CHW, RGB en 0–1, 3·512·512 valores.</param>
    /// <param name="mascara">512·512 valores: 1 = borrar, 0 = conservar.</param>
    /// <returns>CHW, RGB en 0–255, 3·512·512 valores.</returns>
    float[] Rellenar(float[] imagen, float[] mascara);
}

/// <summary>Modelo de ampliación ×4 (Real-ESRGAN), por bloques de tamaño libre.</summary>
public interface IModeloAmpliacion : IDisposable
{
    /// <summary>False si va en el procesador (no hay gráfica compatible o ha fallado).</summary>
    bool UsaGrafica { get; }

    /// <summary>Lado del bloque de entrada recomendado: 512 con gráfica, 256 sin ella.</summary>
    int Bloque { get; }

    /// <summary>CHW RGB 0–1 de ancho×alto → CHW RGB 0–1 de (4·ancho)×(4·alto).</summary>
    float[] Ampliar(float[] bloque, int ancho, int alto);
}

/// <summary>Un archivo de un modelo: dirección fija (revisión concreta), tamaño y huella.</summary>
public sealed record ArchivoModelo(string Nombre, Uri Url, long Bytes, string Sha256);

/// <summary>
/// Un modelo de IA que se descarga la primera vez que se usa. Puede tener varios archivos (SCUNet trae los pesos en
/// un «.onnx.data» aparte); el primero es el que se abre.
/// </summary>
public sealed record ModeloIA(string Nombre, IReadOnlyList<ArchivoModelo> Archivos, string Licencia, Uri Pagina)
{
    public long Bytes => Archivos.Sum(a => a.Bytes);

    /// <summary>Tamaño en MB (1 MB = 1024·1024 bytes), redondeado.</summary>
    public long Megas => (long)Math.Round(Bytes / (1024.0 * 1024.0));

    /// <summary>El archivo que se abre con ONNX Runtime.</summary>
    public string Archivo => Archivos[0].Nombre;
}

/// <summary>Los modelos de la IA: LaMa, Real-ESRGAN y SCUNet.</summary>
public static class ModelosIA
{
    public static readonly ModeloIA Lama = new(
        "Quitar marcas (LaMa)",
        [new ArchivoModelo("lama_fp32.onnx",
            new Uri("https://huggingface.co/Carve/LaMa-ONNX/resolve/c3c0c9e468934d62e79c329e35d82dd09ff8c444/lama_fp32.onnx"),
            208044816, "1faef5301d78db7dda502fe59966957ec4b79dd64e16f03ed96913c7a4eb68d6")],
        "Apache-2.0 · entrenado con Places2 (CC-BY 4.0)",
        new Uri("https://huggingface.co/Carve/LaMa-ONNX"));

    public static readonly ModeloIA RealEsrgan = new(
        "Ampliar (Real-ESRGAN)",
        [new ArchivoModelo("real_esrgan_x4.onnx",
            new Uri("https://huggingface.co/SceneWorks/real-esrgan-onnx/resolve/09f741bac80a246b407da3ee902bf5f3291b602f/real_esrgan_x4.onnx"),
            67051616, "5c586662929cbc686c1a5c38d9c060dbdb4ea5863a1f7672b8c0761e6b89c033")],
        "BSD-3-Clause",
        new Uri("https://huggingface.co/SceneWorks/real-esrgan-onnx"));

    private const string RevisionScunet = "https://huggingface.co/Heliosoph/scunet-onnx/resolve/6d11417ee2fbcc73783c502a238ac115097754fe/";

    /// <summary>Quitar artefactos JPEG y ruido. Solo en el procesador: con DirectML hace caer el proceso.</summary>
    public static readonly ModeloIA Scunet = new(
        "Quitar ruido (SCUNet)",
        [
            new ArchivoModelo("scunet_color_real_psnr.onnx", new Uri(RevisionScunet + "scunet_color_real_psnr.onnx"),
                3798678, "231be201ab413dbc999d7951caa9844846b93a12a40a41e037d6b5888ed4e88c"),
            new ArchivoModelo("scunet_color_real_psnr.onnx.data", new Uri(RevisionScunet + "scunet_color_real_psnr.onnx.data"),
                73138176, "98825ea1210b641c71e5f052f582c70c49fd44b35387ebe2c034268c17df3feb"),
        ],
        "Apache-2.0",
        new Uri("https://huggingface.co/Heliosoph/scunet-onnx"));

    public static IReadOnlyList<ModeloIA> Todos { get; } = [Lama, RealEsrgan, Scunet];
}
