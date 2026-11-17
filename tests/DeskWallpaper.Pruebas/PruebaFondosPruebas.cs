using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class PruebaFondosPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly ServicioFondosFalso _servicio = new([M1.Id, M2.Id, M3.Id]);
    private readonly AplicadorFondos _aplicador;
    private readonly LienzoFisico _lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());

    public PruebaFondosPruebas() =>
        _aplicador = new AplicadorFondos(_servicio, new RenderizadorSkia(), Path.Combine(_img.Carpeta, "datos"));

    public void Dispose() => _img.Dispose();

    private Combinacion Comb(SKColor color) => new("x",
    [
        new AsignacionZona([M2.Id, M1.Id], _img.Franjas($"pano-{color}.png", 384, 108, color), new Encuadre()),
        new AsignacionZona([M3.Id], _img.Franjas($"m3-{color}.png", 192, 120, color), new Encuadre()),
    ]);

    [Fact]
    public void Volver_RestauraExactamente()
    {
        _aplicador.Aplicar(Comb(SKColors.Red), Usuario, _lienzo);
        _servicio.PonerPosicion(PosicionFondo.Centrar);
        var antes = new Dictionary<string, string>(_servicio.Fondos);
        var prueba = new PruebaFondos(_servicio, _aplicador);

        Assert.True(prueba.Empezar(Comb(SKColors.Blue), Usuario, _lienzo).Exito);
        Assert.NotEqual(antes[M1.Id], _servicio.Fondos[M1.Id]);
        Assert.True(prueba.Volver());

        Assert.Equal(antes, _servicio.Fondos);
        Assert.Equal(PosicionFondo.Centrar, _servicio.Posicion);
        Assert.Null(prueba.Anterior);
    }

    [Fact]
    public void Volver_LosArchivosAnterioresSiguenExistiendo()
    {
        _aplicador.Aplicar(Comb(SKColors.Red), Usuario, _lienzo);
        var anteriores = _servicio.Fondos.Values.ToList();
        var prueba = new PruebaFondos(_servicio, _aplicador);

        prueba.Empezar(Comb(SKColors.Blue), Usuario, _lienzo);

        Assert.All(anteriores, ruta => Assert.True(File.Exists(ruta), $"Se ha borrado {ruta}"));
    }

    [Fact]
    public void Mantener_DejaLoNuevo()
    {
        _aplicador.Aplicar(Comb(SKColors.Red), Usuario, _lienzo);
        var prueba = new PruebaFondos(_servicio, _aplicador);
        prueba.Empezar(Comb(SKColors.Blue), Usuario, _lienzo);
        var nuevos = new Dictionary<string, string>(_servicio.Fondos);

        prueba.Mantener();

        Assert.Equal(nuevos, _servicio.Fondos);
        Assert.Null(prueba.Anterior);
        Assert.False(prueba.Volver()); // ya no hay nada a lo que volver
        Assert.Equal(nuevos, _servicio.Fondos);
    }

    [Fact]
    public void Empezar_Fallido_NoGuardaAnterior()
    {
        _aplicador.Aplicar(Comb(SKColors.Red), Usuario, _lienzo);
        var antes = new Dictionary<string, string>(_servicio.Fondos);
        var prueba = new PruebaFondos(_servicio, _aplicador);
        var corrupta = new Combinacion("x", [new AsignacionZona([M3.Id], _img.Texto("mala.jpg", "no"), new Encuadre())]);

        var r = prueba.Empezar(corrupta, Usuario, _lienzo);

        Assert.False(r.Exito);
        Assert.Null(prueba.Anterior);
        Assert.Equal(antes, _servicio.Fondos);
    }
}
