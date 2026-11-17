using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas.IA;

/// <summary>Lo pintado con el pincel, en píxeles de la imagen (centro del píxel en x+0,5).</summary>
public class MascaraPruebas
{
    [Fact]
    public void Nueva_Vacia()
    {
        var m = new Mascara(100, 100);
        Assert.True(m.Vacia);
        Assert.Null(m.Limites);
    }

    [Fact]
    public void Punto_PintaUnCirculo()
    {
        var m = new Mascara(100, 100);

        m.PintarTrazo(50, 50, 50, 50, 10);

        Assert.True(m[50, 50]);
        Assert.True(m[58, 50]);   // centro a 8,5
        Assert.False(m[61, 50]);  // a 11,5
        Assert.True(m[56, 56]);   // a 9,2
        Assert.False(m[58, 58]);  // a 11,3
        Assert.False(m.Vacia);
    }

    [Fact]
    public void Trazo_CubreElSegmento()
    {
        var m = new Mascara(100, 30);

        m.PintarTrazo(10, 10, 90, 10, 3);

        Assert.True(m[50, 10]);
        Assert.True(m[50, 12]);
        Assert.False(m[50, 13]);
        Assert.False(m[50, 14]);
        Assert.True(m[88, 10]);
        Assert.False(m[95, 10]);
    }

    [Fact]
    public void EnLaEsquina_SeRecorta()
    {
        var m = new Mascara(100, 100);

        m.PintarTrazo(0, 0, 0, 0, 20);

        Assert.Equal(new RectPx(0, 0, 20, 20), m.Limites);
        Assert.True(m[0, 0]);
    }

    [Fact]
    public void FueraDeLaImagen_NoFalla()
    {
        var m = new Mascara(50, 50);

        m.PintarTrazo(-100, -100, -80, -80, 5);
        m.PintarTrazo(200, 10, 300, 10, 5);

        Assert.True(m.Vacia);
    }

    [Fact]
    public void Limites_DeVariosTrazos()
    {
        var m = new Mascara(200, 100);

        m.PintarTrazo(20, 20, 20, 20, 2);
        m.PintarTrazo(150, 80, 150, 80, 2);

        Assert.Equal(new RectPx(18, 18, 134, 64), m.Limites);
    }

    [Fact]
    public void Alfa_UnoDentroCeroLejos()
    {
        var m = new Mascara(100, 100);
        m.PintarTrazo(50, 50, 50, 50, 5);   // lo pintado llega hasta x = 54

        var a = m.Alfa(new RectPx(0, 0, 100, 100));
        float A(int x, int y) => a[y * 100 + x];

        Assert.Equal(1f, A(50, 50));
        Assert.Equal(1f, A(54, 50));
        Assert.True(A(57, 50) > 0);         // dentro de la dilatación
        Assert.True(A(60, 50) > 0);         // borde suavizado
        Assert.Equal(0f, A(61, 50));        // 54 + 3 + 3 + 1: intacto
        Assert.Equal(0f, A(10, 10));
    }

    [Fact]
    public void Alfa_DeUnaRegion_TieneEnCuentaLoDeFuera()
    {
        var m = new Mascara(100, 100);
        m.PintarTrazo(50, 50, 50, 50, 5);

        var completa = m.Alfa(new RectPx(0, 0, 100, 100));
        var region = m.Alfa(new RectPx(56, 40, 10, 20));

        Assert.Equal(10 * 20, region.Length);
        for (var y = 0; y < 20; y++)
            for (var x = 0; x < 10; x++)
                Assert.Equal(completa[(40 + y) * 100 + 56 + x], region[y * 10 + x], 5);
    }

    [Fact]
    public void Limpiar_DejaVacia()
    {
        var m = new Mascara(100, 100);
        m.PintarTrazo(10, 10, 40, 40, 4);

        m.Limpiar();

        Assert.True(m.Vacia);
        Assert.Null(m.Limites);
        Assert.False(m[20, 20]);
    }
}
