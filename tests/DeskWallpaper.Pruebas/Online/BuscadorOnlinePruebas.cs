using System.Net;
using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.Online;

/// <summary>Peticiones, límites y errores por fuente. Nunca sale a internet.</summary>
public class BuscadorOnlinePruebas
{
    private static readonly SizePx Panoramica = new(3840, 1080);
    private readonly ManejadorFalso _red = new();
    private readonly List<TimeSpan> _esperas = [];

    private BuscadorOnline Nuevo() => new(_red, (t, _) => { _esperas.Add(t); return Task.CompletedTask; });
    private static FuenteBusqueda Wallhaven => LectorRespuestaJsonPruebas.Wallhaven;

    [Fact]
    public async Task UnaResolucion_UsaPlantillaMayor_YEncaja()
    {
        _red.Responder(HttpStatusCode.OK, LectorRespuestaJsonPruebas.RespuestaReal());

        var r = await Nuevo().BuscarAsync(Wallhaven, "mountain", [Panoramica], 1, CancellationToken.None);

        Assert.Null(r.Error);
        Assert.Contains("atleast=3840x1080", _red.Peticiones.Single().RequestUri!.AbsoluteUri);
        Assert.NotEmpty(r.Resultados);
        Assert.All(r.Resultados, e =>
        {
            Assert.NotNull(e.Encaje);
            Assert.Equal(Panoramica, e.Pedida);
        });
        Assert.True(r.HayMas); // la respuesta real dice last_page 3
    }

    [Fact]
    public async Task DosResoluciones_DosPeticiones_ConIntervalo()
    {
        _red.PorDefecto = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":[]}""") };

        await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica, new SizePx(1920, 1200)], 1, CancellationToken.None);

        Assert.Equal(2, _red.Peticiones.Count);
        Assert.Contains(_esperas, t => t >= TimeSpan.FromSeconds(0.9));
    }

    [Fact]
    public async Task SinResoluciones_SinFiltro()
    {
        _red.Responder(HttpStatusCode.OK, LectorRespuestaJsonPruebas.RespuestaReal());

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [], 1, CancellationToken.None);

        var url = _red.Peticiones.Single().RequestUri!.AbsoluteUri;
        Assert.DoesNotContain("atleast", url);
        Assert.DoesNotContain("resolutions", url);
        Assert.Equal(24, r.Resultados.Count);
        Assert.All(r.Resultados, e => Assert.Null(e.Encaje));
    }

    [Fact]
    public async Task ResultadoQueNoEncaja_SeDescarta()
    {
        const string json = """
            {"data":[
              {"path":"https://x.test/a.jpg","thumbs":{"large":"https://x.test/a.jpg"},"dimension_x":3840,"dimension_y":1080},
              {"path":"https://x.test/b.jpg","thumbs":{"large":"https://x.test/b.jpg"},"dimension_x":1920,"dimension_y":1080}
            ]}
            """;
        _red.Responder(HttpStatusCode.OK, json);

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Equal(Encaje.Exacta, Assert.Single(r.Resultados).Encaje);
    }

    [Fact]
    public async Task Responde429_EsperaYReintenta()
    {
        _red.Responder(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.FromSeconds(2)) } })
            .Responder(HttpStatusCode.OK, LectorRespuestaJsonPruebas.RespuestaReal());

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Null(r.Error);
        Assert.NotEmpty(r.Resultados);
        Assert.Contains(TimeSpan.FromSeconds(2), _esperas);
    }

    [Fact]
    public async Task Responde429DosVeces_Error()
    {
        _red.PorDefecto = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Equal("Wallhaven: demasiadas peticiones; espera un momento", r.Error);
        Assert.Equal(2, _red.Peticiones.Count);
    }

    [Fact]
    public async Task FormatoCambiado_ErrorDeEsaFuente()
    {
        _red.Responder(HttpStatusCode.OK, "<html>Mantenimiento</html>", "text/html");

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Equal("Wallhaven: formato de respuesta no reconocido", r.Error);
        Assert.Empty(r.Resultados);
    }

    [Fact]
    public async Task CharsetNoValido_ErrorDeEsaFuente()
    {
        _red.Responder(_ =>
        {
            var c = new StringContent("{}");
            c.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json; charset=no-existe");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = c };
        });

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Equal("Wallhaven: formato de respuesta no reconocido", r.Error);
    }

    [Fact]
    public async Task RetryAfterEnorme_NoEsperaMasDelTope()
    {
        _red.PorDefecto = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.FromHours(1)) } };

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Equal("Wallhaven: demasiadas peticiones; espera un momento", r.Error);
        Assert.DoesNotContain(_esperas, t => t > TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task SinConexion_Error()
    {
        _red.Responder(_ => throw new HttpRequestException("sin red"));

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Equal("Wallhaven: sin conexión", r.Error);
    }

    [Fact]
    public async Task ErrorHttp_Error()
    {
        _red.Responder(HttpStatusCode.InternalServerError, "x");

        var r = await Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Equal("Wallhaven: error 500", r.Error);
    }

    [Fact]
    public async Task AgenteUsuario_YCabeceras()
    {
        _red.Responder(HttpStatusCode.OK, """{"data":[]}""");
        var conClave = Wallhaven with { Cabeceras = new Dictionary<string, string> { ["X-API-Key"] = "abc" } };

        await Nuevo().BuscarAsync(conClave, "x", [], 1, CancellationToken.None);

        var p = _red.Peticiones.Single();
        Assert.Equal("DeskWallpaper/1.0 (+uso personal)", p.Headers.UserAgent.ToString());
        Assert.Equal("abc", p.Headers.GetValues("X-API-Key").Single());
    }

    [Fact]
    public async Task PlantillaNoValida_Error()
    {
        var rota = Wallhaven with { PlantillaMayor = "wallhaven sin http {texto}" };

        var r = await Nuevo().BuscarAsync(rota, "x", [Panoramica], 1, CancellationToken.None);

        Assert.Equal("Wallhaven: la dirección de la plantilla no es válida", r.Error);
        Assert.Empty(_red.Peticiones);
    }

    [Fact]
    public async Task Cancelado_Lanza()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Nuevo().BuscarAsync(Wallhaven, "x", [Panoramica], 1, cts.Token));
    }
}
