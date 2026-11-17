using System.Net;
using DeskWallpaper.Nucleo;
using SkiaSharp;

namespace DeskWallpaper.Pruebas.Online;

/// <summary>Descargas validadas, sin sobrescribir y sin dejar restos.</summary>
public class DescargadorImagenesPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly ManejadorFalso _red = new();
    private readonly string _destino;
    private readonly byte[] _png;

    public DescargadorImagenesPruebas()
    {
        _destino = Path.Combine(_img.Carpeta, "descargas");
        _png = File.ReadAllBytes(_img.Franjas("origen.png", 64, 18, SKColors.Red));
    }

    public void Dispose() => _img.Dispose();

    private DescargadorImagenes Nuevo() => new(_red, new RenderizadorSkia());

    [Fact]
    public async Task Descarga_Valida()
    {
        _red.Responder(_png, "image/png");

        var d = await Nuevo().DescargarAsync(new Uri("https://w.wallhaven.cc/full/0q/wallhaven-0q2w27.jpg"), _destino, CancellationToken.None);

        // El contenido es PNG aunque la dirección diga .jpg: manda el contenido.
        Assert.Equal(Path.Combine(_destino, "wallhaven-0q2w27.png"), d.Ruta);
        Assert.Equal(new SizePx(64, 18), d.Tamano);
        Assert.Equal(_png, File.ReadAllBytes(d.Ruta));
        Assert.Empty(Directory.GetFiles(_destino, "*.part"));
        Assert.Equal("DeskWallpaper/1.0 (+uso personal)", _red.Peticiones.Single().Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task CarpetaInexistente_SeCrea()
    {
        _red.Responder(_png, "image/png");
        var carpeta = Path.Combine(_destino, "nueva", "más");

        var d = await Nuevo().DescargarAsync(new Uri("https://x.test/a.png"), carpeta, CancellationToken.None);

        Assert.True(File.Exists(d.Ruta));
    }

    [Fact]
    public async Task MismoNombre_NoSobrescribe()
    {
        _red.PorDefecto = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(_png) { Headers = { ContentType = new("image/png") } },
        };
        Directory.CreateDirectory(_destino);
        File.WriteAllText(Path.Combine(_destino, "x.png"), "del usuario");

        var d = await Nuevo().DescargarAsync(new Uri("https://x.test/x.png"), _destino, CancellationToken.None);
        var d2 = await Nuevo().DescargarAsync(new Uri("https://x.test/x.png"), _destino, CancellationToken.None);

        Assert.Equal(Path.Combine(_destino, "x (2).png"), d.Ruta);
        Assert.Equal(Path.Combine(_destino, "x (3).png"), d2.Ruta);
        Assert.Equal("del usuario", File.ReadAllText(Path.Combine(_destino, "x.png")));
    }

    [Fact]
    public async Task HtmlConEstado200_NoDejaArchivo()
    {
        _red.Responder(HttpStatusCode.OK, "<html>No encontrado</html>", "text/html");

        var ex = await Assert.ThrowsAsync<ExcepcionDescarga>(() =>
            Nuevo().DescargarAsync(new Uri("https://x.test/a.jpg"), _destino, CancellationToken.None));

        Assert.Equal("El archivo descargado no es una imagen", ex.Message);
        Assert.False(Directory.Exists(_destino) && Directory.GetFiles(_destino).Length > 0);
    }

    [Fact]
    public async Task Error404_NoDejaArchivo()
    {
        _red.Responder(HttpStatusCode.NotFound, "x", "text/plain");

        var ex = await Assert.ThrowsAsync<ExcepcionDescarga>(() =>
            Nuevo().DescargarAsync(new Uri("https://x.test/a.jpg"), _destino, CancellationToken.None));

        Assert.Equal("No se ha podido descargar: error 404", ex.Message);
        Assert.False(Directory.Exists(_destino) && Directory.GetFiles(_destino).Length > 0);
    }

    [Fact]
    public async Task SinConexion_ErrorClaro()
    {
        _red.Responder(_ => throw new HttpRequestException("sin red"));

        var ex = await Assert.ThrowsAsync<ExcepcionDescarga>(() =>
            Nuevo().DescargarAsync(new Uri("https://x.test/a.jpg"), _destino, CancellationToken.None));

        Assert.Equal("No se ha podido descargar: sin conexión", ex.Message);
    }

    /// <summary>Contenido que falla a mitad de lectura, como un corte de red.</summary>
    private sealed class ContenidoCortado : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new IOException("conexión cerrada");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    [Fact]
    public async Task CorteDeRedAMitad_ErrorClaroYSinArchivo()
    {
        _red.Responder(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ContenidoCortado() });

        var ex = await Assert.ThrowsAsync<ExcepcionDescarga>(() =>
            Nuevo().DescargarAsync(new Uri("https://x.test/a.jpg"), _destino, CancellationToken.None));

        Assert.Equal("No se ha podido descargar: se ha cortado la conexión", ex.Message);
        Assert.False(Directory.Exists(_destino) && Directory.GetFiles(_destino).Length > 0);
    }

    [Fact]
    public async Task ArchivoDemasiadoGrande_SeRechazaSinLeerlo()
    {
        _red.Responder(_ =>
        {
            var c = new ByteArrayContent(_png);
            c.Headers.ContentLength = DescargadorImagenes.TamanoMaximo + 1;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = c };
        });

        var ex = await Assert.ThrowsAsync<ExcepcionDescarga>(() =>
            Nuevo().DescargarAsync(new Uri("https://x.test/a.jpg"), _destino, CancellationToken.None));

        Assert.Equal("No se ha podido descargar: el archivo es demasiado grande", ex.Message);
    }

    [Fact]
    public async Task DataUri_Base64()
    {
        var uri = new Uri("data:image/png;base64," + Convert.ToBase64String(_png));

        var d = await Nuevo().DescargarAsync(uri, _destino, CancellationToken.None);

        Assert.EndsWith(".png", d.Ruta);
        Assert.StartsWith("imagen-", Path.GetFileName(d.Ruta));
        Assert.Equal(_png, File.ReadAllBytes(d.Ruta));
        Assert.Empty(_red.Peticiones);
    }

    [Fact]
    public async Task Extension_SegunElContenidoReal()
    {
        _red.Responder(_png, "image/webp"); // el servidor dice webp, pero es PNG

        var d = await Nuevo().DescargarAsync(new Uri("https://x.test/foto?id=3"), _destino, CancellationToken.None);

        Assert.Equal("foto.png", Path.GetFileName(d.Ruta));
    }

    [Fact]
    public async Task Truncado_SeRechaza()
    {
        // Cabecera válida pero le falta el final: el renderizador lo da por malo al usarlo.
        _red.Responder(_png[..(_png.Length / 2)], "image/png");

        var ex = await Assert.ThrowsAsync<ExcepcionDescarga>(() =>
            Nuevo().DescargarAsync(new Uri("https://x.test/a.png"), _destino, CancellationToken.None));

        Assert.Equal("El archivo descargado está dañado o incompleto", ex.Message);
        Assert.Empty(Directory.GetFiles(_destino));
    }

    [Fact]
    public async Task Gif_SeRechaza()
    {
        _red.Responder(Convert.FromBase64String("R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw=="), "image/gif");

        var ex = await Assert.ThrowsAsync<ExcepcionDescarga>(() =>
            Nuevo().DescargarAsync(new Uri("https://x.test/a.gif"), _destino, CancellationToken.None));

        Assert.Equal("Es un GIF: DeskWallpaper usa imágenes JPG, PNG, WebP o BMP", ex.Message);
        Assert.False(Directory.Exists(_destino) && Directory.GetFiles(_destino).Length > 0);
    }

    [Fact]
    public async Task NombreConCaracteresNoValidos()
    {
        _red.Responder(_png, "image/jpeg");

        var d = await Nuevo().DescargarAsync(new Uri("https://x.test/a%3Ab%2Ac%3F.jpg"), _destino, CancellationToken.None);

        var nombre = Path.GetFileName(d.Ruta);
        Assert.DoesNotContain(nombre, c => Path.GetInvalidFileNameChars().Contains(c));
        Assert.Equal("a_b_c_.png", nombre); // la extensión la pone el contenido (PNG)
    }

    [Fact]
    public void RecortarTemporales_DejaLosMasRecientes()
    {
        Directory.CreateDirectory(_destino);
        for (var i = 0; i < 5; i++)
        {
            var r = Path.Combine(_destino, $"t{i}.jpg");
            File.WriteAllText(r, "x");
            File.SetLastWriteTimeUtc(r, DateTime.UtcNow.AddMinutes(i));
        }

        DescargadorImagenes.RecortarTemporales(_destino, 3);

        Assert.Equal(["t2.jpg", "t3.jpg", "t4.jpg"], Directory.GetFiles(_destino).Select(Path.GetFileName).Order());
    }
}
