using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas.Online;

/// <summary>Fuentes, plantillas de dirección y encaje de resolución.</summary>
public class PlantillasPruebas
{
    private static IEnumerable<FuenteBusqueda> DeImagenes => FuentesPredeterminadas.Todas().Where(f => f.Contenido == ContenidoFuente.Imagenes);
    private static FuenteBusqueda Wallhaven => DeImagenes.Single(f => f.Tipo == TipoFuente.Api);
    private static FuenteBusqueda Google => DeImagenes.Single(f => f.Tipo == TipoFuente.Web);

    [Fact]
    public void Rellenar_CodificaElTexto()
    {
        var uri = PlantillaUrl.Rellenar("https://x.test/?q={texto}", "ñandú & co #1+2", null, 1);
        Assert.Contains("q=%C3%B1and%C3%BA%20%26%20co%20%231%2B2", uri.AbsoluteUri);
    }

    [Fact]
    public void Rellenar_ResolucionYPagina()
    {
        var uri = PlantillaUrl.Rellenar(Wallhaven.PlantillaMayor, "mountain", new SizePx(3840, 1080), 2).AbsoluteUri;
        Assert.Contains("atleast=3840x1080", uri);
        Assert.Contains("ratios=32x9", uri);
        Assert.Contains("page=2", uri);
        Assert.Contains("q=mountain", uri);
    }

    [Fact]
    public void SinResolucion_Google() =>
        Assert.Equal("https://www.google.com/search?udm=2&q={texto}", PlantillaUrl.SinResolucion(Google.PlantillaExacta));

    [Fact]
    public void SinResolucion_WallhavenMayor()
    {
        var p = PlantillaUrl.SinResolucion(Wallhaven.PlantillaMayor);
        Assert.DoesNotContain("atleast", p);
        Assert.DoesNotContain("ratios", p);
        Assert.Contains("q={texto}", p);
        Assert.Contains("page={pagina}", p);
    }

    [Theory]
    [InlineData(3840, 1080, "32x9")]
    [InlineData(1920, 1200, "16x10")]
    [InlineData(5120, 1440, "32x9")]
    public void Proporcion(int ancho, int alto, string esperada) =>
        Assert.Equal(esperada, PlantillaUrl.Proporcion(new SizePx(ancho, alto)));

    [Theory]
    [InlineData(3840, 1080, 3840, 1080, Encaje.Exacta)]
    [InlineData(5120, 1440, 3840, 1080, Encaje.Mayor)]
    [InlineData(3893, 1080, 3840, 1080, Encaje.Mayor)]
    [InlineData(1920, 1200, 1920, 1200, Encaje.Exacta)]
    public void Encaja(int ancho, int alto, int pedidoAncho, int pedidoAlto, Encaje esperado) =>
        Assert.Equal(esperado, EncajeResolucion.De(new SizePx(ancho, alto), new SizePx(pedidoAncho, pedidoAlto)));

    [Theory]
    [InlineData(4000, 1080)] // forma ≈4 % distinta
    [InlineData(2560, 720)]  // más pequeña
    [InlineData(1920, 1080)]
    public void NoEncaja(int ancho, int alto) =>
        Assert.Null(EncajeResolucion.De(new SizePx(ancho, alto), new SizePx(3840, 1080)));

    [Fact]
    public void DeZonas_Usuario()
    {
        var r = ResolucionesBusqueda.DeZonas(ZonasPorDefecto.Calcular(Usuario), Usuario);
        Assert.Equal(2, r.Count);
        Assert.Contains(("3840×1080 (2+1)", new SizePx(3840, 1080)), r);
        Assert.Contains(("1920×1200 (3)", new SizePx(1920, 1200)), r);
    }

    [Fact]
    public void DeZonas_SinRepetirTamanos()
    {
        var sueltas = EditorZonas.Separar(Usuario, ZonasPorDefecto.Calcular(Usuario), "2+1")!;
        var r = ResolucionesBusqueda.DeZonas(sueltas, Usuario);
        Assert.Equal(2, r.Count); // 1920×1080 una sola vez, y 1920×1200
    }

    [Fact]
    public void FuentesPredeterminadas_Validas()
    {
        var todas = FuentesPredeterminadas.Todas();
        Assert.Equal(2, DeImagenes.Count());   // y las de vídeo (búsqueda de fondos animados)
        Assert.All(todas, f => Assert.True(f.Activa));
        Assert.Equal("Wallhaven", Wallhaven.Nombre);
        Assert.Equal("Google Imágenes", Google.Nombre);
        foreach (var f in todas)
        {
            var uri = PlantillaUrl.Rellenar(f.PlantillaExacta, "x", new SizePx(3840, 1080), 1);
            Assert.True(uri.IsAbsoluteUri);
            Assert.Equal("https", uri.Scheme);
        }
    }
}
