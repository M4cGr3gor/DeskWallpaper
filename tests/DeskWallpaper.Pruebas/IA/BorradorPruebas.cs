using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>Borrar con LaMa solo en una zona de contexto alrededor de lo pintado.</summary>
public class BorradorPruebas
{
    private static ImagenRgba Lisa(int ancho, int alto, byte gris = 128, Func<int, int, byte>? alfa = null)
    {
        var p = new byte[ancho * alto * 4];
        for (var y = 0; y < alto; y++)
            for (var x = 0; x < ancho; x++)
            {
                var i = (y * ancho + x) * 4;
                p[i] = p[i + 1] = p[i + 2] = gris;
                p[i + 3] = alfa?.Invoke(x, y) ?? 255;
            }
        return new ImagenRgba(ancho, alto, p);
    }

    private static byte[] Pixel(ImagenRgba img, int x, int y) => img.Pixeles.AsSpan((y * img.Ancho + x) * 4, 4).ToArray();

    [Fact]
    public void Contexto_MarcaPequena_UsaVentanaDe512SinEscalar()
    {
        var c = Borrador.Contexto(new RectPx(3700, 1000, 80, 40), new SizePx(3840, 1080));

        Assert.Equal(512, c.Ancho);
        Assert.Equal(512, c.Alto);
        Assert.Equal(3840, c.X + c.Ancho);
        Assert.Equal(1080, c.Y + c.Alto);
    }

    [Fact]
    public void Contexto_EnLaEsquinaSuperiorIzquierda_NoSeSale()
    {
        var c = Borrador.Contexto(new RectPx(0, 0, 30, 20), new SizePx(3840, 1080));

        Assert.Equal(new RectPx(0, 0, 512, 512), c);
    }

    [Fact]
    public void Contexto_ImagenPequena_EsLaImagenEntera() =>
        Assert.Equal(new RectPx(0, 0, 400, 300), Borrador.Contexto(new RectPx(10, 10, 20, 20), new SizePx(400, 300)));

    [Fact]
    public void Contexto_MarcaGrande_AnadeMargen()
    {
        // margen = max(32, 1000/2) = 500 por lado → 2000 de ancho; el alto se limita a la imagen.
        var c = Borrador.Contexto(new RectPx(500, 400, 1000, 200), new SizePx(3840, 1080));

        Assert.Equal(2000, c.Ancho);
        Assert.Equal(1080, c.Alto);
        Assert.True(c.X <= 500 && c.X + c.Ancho >= 1500);
    }

    [Fact]
    public void Borrar_SoloCambiaLoPintado()
    {
        var img = Lisa(1000, 600);
        var m = new Mascara(1000, 600);
        m.PintarTrazo(900, 500, 900, 500, 10);

        var r = Borrador.Borrar(img, m, new RellenoFalso());

        Assert.Equal([200, 0, 0, 255], Pixel(r.Imagen, 900, 500));
        Assert.Equal(Pixel(img, 700, 300), Pixel(r.Imagen, 700, 300));
        Assert.Equal(Pixel(img, 920, 500), Pixel(r.Imagen, 920, 500));   // a más de 10 + 3 + 3 px
        Assert.False(r.ZonaGrande);
        Assert.Equal([128, 128, 128, 255], Pixel(img, 900, 500));          // la imagen de entrada no cambia
    }

    [Fact]
    public void Borrar_ConservaLaTransparencia()
    {
        var img = Lisa(300, 200, alfa: (x, _) => (byte)(x % 256));
        var m = new Mascara(300, 200);
        m.PintarTrazo(100, 100, 150, 100, 8);

        var r = Borrador.Borrar(img, m, new RellenoFalso());

        for (var i = 3; i < img.Pixeles.Length; i += 4) Assert.Equal(img.Pixeles[i], r.Imagen.Pixeles[i]);
    }

    [Fact]
    public void Borrar_ImagenPequena_FuncionaYMantieneTamano()
    {
        var img = Lisa(400, 300);
        var m = new Mascara(400, 300);
        m.PintarTrazo(380, 280, 380, 280, 6);

        var r = Borrador.Borrar(img, m, new RellenoFalso());

        Assert.Equal(new SizePx(400, 300), r.Imagen.Tamano);
        var p = Pixel(r.Imagen, 380, 280);
        Assert.InRange(p[0], 190, 210);
        Assert.InRange(p[1], 0, 10);
        Assert.Equal(Pixel(img, 50, 50), Pixel(r.Imagen, 50, 50));
    }

    [Fact]
    public void Borrar_MarcaGrande_AvisaZonaGrande()
    {
        var m = new Mascara(3840, 1080);
        m.PintarTrazo(100, 500, 2200, 500, 5);

        var r = Borrador.Borrar(Lisa(3840, 1080), m, new RellenoFalso());

        Assert.True(r.ZonaGrande);
    }

    [Fact]
    public void Borrar_MascaraVacia_Lanza() =>
        Assert.Throws<InvalidOperationException>(() => Borrador.Borrar(Lisa(100, 100), new Mascara(100, 100), new RellenoFalso()));

    [Fact]
    public void Borrar_PasaAlModeloLaMascaraYLaImagenEnSuFormato()
    {
        var m = new Mascara(600, 600);
        m.PintarTrazo(300, 300, 300, 300, 3);
        var modelo = new RellenoFalso();

        Borrador.Borrar(Lisa(600, 600, gris: 255), m, modelo);

        Assert.Contains(1f, modelo.UltimaMascara!);
        Assert.Contains(0f, modelo.UltimaMascara!);
        Assert.All(modelo.UltimaMascara!, v => Assert.True(v is 0f or 1f));
        Assert.Equal(1f, modelo.UltimaImagen![0], 3);   // RGB en 0–1
    }

    [Fact]
    public void Borrar_TrazoFino_EnContextoReducido_NoDesaparece()
    {
        // Contexto de 2000 px reducido a 512: un trazo de 1 px de radio debe seguir llegando al modelo.
        var m = new Mascara(3840, 1080);
        m.PintarTrazo(500, 500, 1500, 500, 1);
        var modelo = new RellenoFalso();

        Borrador.Borrar(Lisa(3840, 1080), m, modelo);

        Assert.True(modelo.UltimaMascara!.Count(v => v == 1f) >= 100);
    }
}
