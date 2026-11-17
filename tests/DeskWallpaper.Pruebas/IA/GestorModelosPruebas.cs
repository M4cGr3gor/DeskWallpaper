using System.Net;
using System.Security.Cryptography;
using DeskWallpaper.Nucleo;
using DeskWallpaper.Pruebas.Online;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>Modelos descargados una vez, comprobando su huella y sin dejar nada a medias.</summary>
public class GestorModelosPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly ManejadorFalso _red = new();
    private readonly string _carpeta;
    private readonly byte[] _datos;
    private readonly ModeloIA _modelo;

    public GestorModelosPruebas()
    {
        _carpeta = Path.Combine(_img.Carpeta, "modelos");
        _datos = new byte[300 * 1024];
        new Random(3).NextBytes(_datos);
        _modelo = new ModeloIA("Prueba",
            [new ArchivoModelo("prueba.onnx", new Uri("https://x.test/prueba.onnx"), _datos.Length, Convert.ToHexStringLower(SHA256.HashData(_datos)))],
            "MIT", new Uri("https://x.test"));
        _segundo = new byte[3 * 1024];
        new Random(5).NextBytes(_segundo);
        _doble = new ModeloIA("Doble",
        [
            new ArchivoModelo("doble.onnx", new Uri("https://x.test/doble.onnx"), _segundo.Length, Convert.ToHexStringLower(SHA256.HashData(_segundo))),
            new ArchivoModelo("doble.onnx.data", new Uri("https://x.test/doble.onnx.data"), _datos.Length, Convert.ToHexStringLower(SHA256.HashData(_datos))),
        ], "Apache-2.0", new Uri("https://x.test"));
    }

    private readonly byte[] _segundo;
    private readonly ModeloIA _doble;

    [Fact]
    public async Task DosArchivos_SeDescarganLosDos()
    {
        _red.Responder(_segundo, "application/octet-stream").Responder(_datos, "application/octet-stream");
        var informes = new List<ProgresoDescarga>();
        var g = Nuevo();

        var ruta = await g.DescargarAsync(_doble, new ProgresoSincrono(informes.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(_carpeta, "doble.onnx"), ruta);
        Assert.Equal(["doble.onnx", "doble.onnx.data"], Archivos().Select(Path.GetFileName).Order());
        Assert.True(g.EstaDescargado(_doble));
        Assert.Equal(_segundo.Length + _datos.Length, _doble.Bytes);
        Assert.All(informes, i => Assert.Equal(_doble.Bytes, i.Total));
        Assert.Equal(1.0, informes[^1].Fraccion, 6);
        Assert.Equal(informes.Select(i => i.Recibidos).Order(), informes.Select(i => i.Recibidos));
    }

    [Fact]
    public async Task HuellaMalaEnElSegundo_NoQuedaNinguno()
    {
        var alterados = (byte[])_datos.Clone();
        alterados[10] ^= 0xFF;
        _red.Responder(_segundo, "application/octet-stream").Responder(alterados, "application/octet-stream");

        var ex = await Assert.ThrowsAsync<ExcepcionModelo>(() => Nuevo().DescargarAsync(_doble, null, CancellationToken.None));

        Assert.Equal("No se ha podido descargar el modelo: el archivo descargado no es el esperado", ex.Message);
        Assert.Empty(Archivos());
    }

    [Fact]
    public void FaltaUno_NoEstaDescargado()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllBytes(Path.Combine(_carpeta, "doble.onnx"), _segundo);

        Assert.False(Nuevo().EstaDescargado(_doble));
    }

    [Fact]
    public void Eliminar_BorraTodos()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllBytes(Path.Combine(_carpeta, "doble.onnx"), _segundo);
        File.WriteAllBytes(Path.Combine(_carpeta, "doble.onnx.data"), _datos);

        Nuevo().Eliminar(_doble);

        Assert.Empty(Archivos());
    }

    public void Dispose() => _img.Dispose();

    private GestorModelos Nuevo() => new(_carpeta, _red);

    private string[] Archivos() => Directory.Exists(_carpeta) ? Directory.GetFiles(_carpeta) : [];

    [Fact]
    public async Task Descarga_Correcta_QuedaEnSuSitio()
    {
        _red.Responder(_datos, "application/octet-stream");
        var g = Nuevo();
        Assert.False(g.EstaDescargado(_modelo));

        var ruta = await g.DescargarAsync(_modelo, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_carpeta, "prueba.onnx"), ruta);
        Assert.Equal(_datos, File.ReadAllBytes(ruta));
        Assert.True(g.EstaDescargado(_modelo));
        Assert.Equal([ruta], Archivos());
        Assert.Equal(BuscadorOnline.AgenteUsuario, _red.Peticiones.Single().Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task HuellaDistinta_NoQuedaNada()
    {
        var alterados = (byte[])_datos.Clone();
        alterados[1000] ^= 0xFF;
        _red.Responder(alterados, "application/octet-stream");

        var ex = await Assert.ThrowsAsync<ExcepcionModelo>(() => Nuevo().DescargarAsync(_modelo, null, CancellationToken.None));

        Assert.Equal("No se ha podido descargar el modelo: el archivo descargado no es el esperado", ex.Message);
        Assert.Empty(Archivos());
    }

    [Fact]
    public async Task Error404()
    {
        _red.Responder(HttpStatusCode.NotFound, "no", "text/plain");

        var ex = await Assert.ThrowsAsync<ExcepcionModelo>(() => Nuevo().DescargarAsync(_modelo, null, CancellationToken.None));

        Assert.Equal("No se ha podido descargar el modelo: error 404", ex.Message);
        Assert.Empty(Archivos());
    }

    [Fact]
    public async Task SinConexion()
    {
        _red.Responder(_ => throw new HttpRequestException("sin red"));

        var ex = await Assert.ThrowsAsync<ExcepcionModelo>(() => Nuevo().DescargarAsync(_modelo, null, CancellationToken.None));

        Assert.Equal("No se ha podido descargar el modelo: sin conexión", ex.Message);
    }

    /// <summary>Contenido que se corta a mitad, como una red que se cae.</summary>
    private sealed class ContenidoCortado : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(new byte[1000]);
            throw new IOException("conexión cerrada");
        }

        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    [Fact]
    public async Task CorteAMitad_NoQuedaNada()
    {
        _red.Responder(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ContenidoCortado() });

        var ex = await Assert.ThrowsAsync<ExcepcionModelo>(() => Nuevo().DescargarAsync(_modelo, null, CancellationToken.None));

        Assert.Equal("No se ha podido descargar el modelo: se ha cortado la conexión", ex.Message);
        Assert.Empty(Archivos());
    }

    /// <summary>Flujo que cancela la descarga al primer trozo leído (el usuario pulsa «Cancelar»).</summary>
    private sealed class FlujoQueCancela(byte[] datos, CancellationTokenSource cts) : MemoryStream(datos)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            cts.Cancel();
            return base.ReadAsync(buffer, ct);
        }
    }

    [Fact]
    public async Task Cancelada_NoQuedaNada()
    {
        using var cts = new CancellationTokenSource();
        _red.Responder(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FlujoQueCancela(_datos, cts), 4096) });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Nuevo().DescargarAsync(_modelo, null, cts.Token));

        Assert.Empty(Archivos());
    }

    [Fact]
    public async Task Progreso_LlegaAUnoYTerminaComprobando()
    {
        _red.Responder(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(_datos), 16 * 1024) });
        var informes = new List<ProgresoDescarga>();

        await Nuevo().DescargarAsync(_modelo, new ProgresoSincrono(informes.Add), CancellationToken.None);

        Assert.True(informes.Count >= 3);
        Assert.Equal(informes.Select(i => i.Fraccion).Order(), informes.Select(i => i.Fraccion));
        Assert.Equal(1.0, informes[^1].Fraccion, 6);
        Assert.True(informes[^1].Comprobando);
        Assert.All(informes.SkipLast(1), i => Assert.False(i.Comprobando));
    }

    [Fact]
    public void ProgresoDescarga_Texto()
    {
        Assert.Equal("Descargando: 45 MB de 198 MB", new ProgresoDescarga(47_185_920, 208_044_816).Texto);
        Assert.Equal("Comprobando que el archivo es correcto…", new ProgresoDescarga(10, 10, Comprobando: true).Texto);
        Assert.Equal(0.5, new ProgresoDescarga(5, 10).Fraccion);
        Assert.Equal(0, new ProgresoDescarga(5, 0).Fraccion);
    }

    [Fact]
    public void EstaDescargado_TamanoDistinto_False()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllBytes(Path.Combine(_carpeta, "prueba.onnx"), new byte[10]);

        Assert.False(Nuevo().EstaDescargado(_modelo));
    }

    [Fact]
    public void Eliminar_BorraElModeloYLosRestos()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllBytes(Path.Combine(_carpeta, "prueba.onnx"), _datos);
        File.WriteAllBytes(Path.Combine(_carpeta, "prueba.onnx.part"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(_carpeta, "otro.onnx"), "de otro modelo");
        var g = Nuevo();

        g.Eliminar(_modelo);

        Assert.False(g.EstaDescargado(_modelo));
        Assert.Equal(["otro.onnx"], Archivos().Select(Path.GetFileName));
    }

    [Fact]
    public void Eliminar_SinDescargar_NoFalla() => Nuevo().Eliminar(_modelo);

    [Fact]
    public void Eliminar_UnArchivoEnUso_NoBorraNinguno()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllBytes(Path.Combine(_carpeta, "doble.onnx"), _segundo);
        var datos = Path.Combine(_carpeta, "doble.onnx.data");
        File.WriteAllBytes(datos, _datos);

        using (new FileStream(datos, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<ExcepcionModelo>(() => Nuevo().Eliminar(_doble));

        Assert.True(Nuevo().EstaDescargado(_doble));
    }

    [Fact]
    public void HayArchivos_ConUnoSuelto_SePuedeEliminar()
    {
        Directory.CreateDirectory(_carpeta);
        File.WriteAllBytes(Path.Combine(_carpeta, "doble.onnx.data"), _datos);
        var g = Nuevo();

        Assert.False(g.EstaDescargado(_doble));
        Assert.True(g.HayArchivos(_doble));
        g.Eliminar(_doble);
        Assert.False(g.HayArchivos(_doble));
    }

    [Fact]
    public void Eliminar_EnUso_ErrorClaro()
    {
        Directory.CreateDirectory(_carpeta);
        var ruta = Path.Combine(_carpeta, "prueba.onnx");
        File.WriteAllBytes(ruta, _datos);
        using var abierto = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read);

        var ex = Assert.Throws<ExcepcionModelo>(() => Nuevo().Eliminar(_modelo));

        Assert.Equal("No se puede eliminar el modelo porque se está usando. Cierra la ventana de edición y vuelve a intentarlo.", ex.Message);
    }

    [Fact]
    public void ModelosFijos_TamanoEnMegas()
    {
        Assert.Equal(198, ModelosIA.Lama.Megas);
        Assert.Equal(64, ModelosIA.RealEsrgan.Megas);
        Assert.Equal(73, ModelosIA.Scunet.Megas);
        Assert.Equal("scunet_color_real_psnr.onnx", ModelosIA.Scunet.Archivo);
        Assert.Equal(3, ModelosIA.Todos.Count);
    }

    private sealed class ProgresoSincrono(Action<ProgresoDescarga> accion) : IProgress<ProgresoDescarga>
    {
        public void Report(ProgresoDescarga value) => accion(value);
    }
}
