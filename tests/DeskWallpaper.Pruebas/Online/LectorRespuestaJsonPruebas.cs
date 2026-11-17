using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.Online;

/// <summary>Lectura de respuestas de fuentes Api según sus rutas configuradas.</summary>
public class LectorRespuestaJsonPruebas
{
    internal static FuenteBusqueda Wallhaven => FuentesPredeterminadas.Todas().Single(f => f.Nombre == "Wallhaven");

    internal static string RespuestaReal() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "wallhaven-busqueda.json"));

    [Fact]
    public void Wallhaven_Real()
    {
        var p = LectorRespuestaJson.Leer(RespuestaReal(), Wallhaven);

        Assert.Equal(24, p.Resultados.Count);
        Assert.True(p.UltimaPagina > 0);
        var primero = p.Resultados[0];
        Assert.Equal("th.wallhaven.cc", primero.Miniatura.Host);
        Assert.StartsWith("https://w.wallhaven.cc/full/", primero.Imagen.AbsoluteUri);
        Assert.True(primero.Tamano.Ancho >= 3840);
        Assert.Equal("Wallhaven", primero.Fuente);
        Assert.NotNull(primero.Ficha);
    }

    [Fact]
    public void ElementoIncompleto_SeSalta()
    {
        const string json = """
            {"data":[
              {"path":"https://x.test/a.jpg","thumbs":{"large":"https://x.test/a_t.jpg"},"dimension_x":3840,"dimension_y":1080},
              {"thumbs":{"large":"https://x.test/b_t.jpg"},"dimension_x":3840,"dimension_y":1080}
            ]}
            """;
        Assert.Single(LectorRespuestaJson.Leer(json, Wallhaven).Resultados);
    }

    [Fact]
    public void DimensionComoTexto_SeLee()
    {
        const string json = """{"data":[{"path":"https://x.test/a.jpg","thumbs":{"large":"https://x.test/t.jpg"},"dimension_x":"3840","dimension_y":"1080"}]}""";
        Assert.Equal(new SizePx(3840, 1080), LectorRespuestaJson.Leer(json, Wallhaven).Resultados.Single().Tamano);
    }

    [Fact]
    public void SinUltimaPagina_EsNull()
    {
        const string json = """{"data":[]}""";
        Assert.Null(LectorRespuestaJson.Leer(json, Wallhaven).UltimaPagina);
    }

    [Fact]
    public void NoEsJson_Lanza()
    {
        var ex = Assert.Throws<FormatException>(() => LectorRespuestaJson.Leer("<html><body>Error</body></html>", Wallhaven));
        Assert.Equal("Formato de respuesta no reconocido", ex.Message);
    }

    [Fact]
    public void CamposRenombrados_Lanza()
    {
        // La API cambia "path" por "url_full": hay elementos, pero no se puede leer ninguno.
        const string json = """{"data":[{"url_full":"https://x.test/a.jpg","thumbs":{"large":"https://x.test/t.jpg"},"dimension_x":3840,"dimension_y":1080}]}""";
        Assert.Throws<FormatException>(() => LectorRespuestaJson.Leer(json, Wallhaven));
    }

    [Fact]
    public void ClaveRepetida_Lanza() =>
        Assert.Throws<FormatException>(() => LectorRespuestaJson.Leer("""{"data":[],"data":[]}""", Wallhaven));

    [Fact]
    public void SinLista_Lanza() =>
        Assert.Throws<FormatException>(() => LectorRespuestaJson.Leer("""{"error":"x"}""", Wallhaven));

    [Fact]
    public void UrlRelativa_SeSalta()
    {
        const string json = """
            {"data":[
              {"path":"/full/a.jpg","thumbs":{"large":"https://x.test/t.jpg"},"dimension_x":3840,"dimension_y":1080},
              {"path":"https://x.test/b.jpg","thumbs":{"large":"https://x.test/t.jpg"},"dimension_x":3840,"dimension_y":1080}
            ]}
            """;
        Assert.Equal("https://x.test/b.jpg", Assert.Single(LectorRespuestaJson.Leer(json, Wallhaven).Resultados).Imagen.AbsoluteUri);
    }
}
