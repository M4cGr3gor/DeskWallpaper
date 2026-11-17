using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

/// <summary>Pruebas de los fallos menores que dejó pendientes la revisión final de la fase 1.</summary>
public class MenoresPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly LienzoFisico _lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());

    public void Dispose() => _img.Dispose();

    private (ServicioFondosFalso Servicio, AplicadorFondos Aplicador, string Datos) Nuevo()
    {
        var servicio = new ServicioFondosFalso([M1.Id, M2.Id, M3.Id]);
        var datos = Path.Combine(_img.Carpeta, "datos");
        return (servicio, new AplicadorFondos(servicio, new RenderizadorSkia(), datos), datos);
    }

    // 7: un error de disco al generar los fondos es un fallo controlado, sin restos.
    [Fact]
    public void ErrorDeDisco_FalloControladoSinTocarNada()
    {
        var (servicio, aplicador, datos) = Nuevo();
        Directory.CreateDirectory(Path.Combine(datos, "cache"));
        File.WriteAllText(Path.Combine(datos, "cache", "fondos"), "un archivo donde debería haber una carpeta");
        var pano = _img.Franjas("pano.png", 384, 108, SKColors.Red);

        var r = aplicador.Aplicar(new Combinacion("x", [new AsignacionZona([M2.Id, M1.Id], pano, new Encuadre())]), Usuario, _lienzo);

        Assert.False(r.Exito);
        Assert.Contains(r.Errores, e => e.Contains("No se han podido guardar"));
        Assert.Equal(0, servicio.LlamadasPonerFondo);
    }

    // 8: aplicar sin imágenes no cambia nada.
    [Fact]
    public void SinImagenes_ErrorSinTocarLaPosicion()
    {
        var (servicio, aplicador, _) = Nuevo();

        var r = aplicador.Aplicar(new Combinacion("x", []), Usuario, _lienzo);

        Assert.False(r.Exito);
        Assert.Contains(r.Errores, e => e.Contains("Elige al menos una imagen"));
        Assert.Equal(PosicionFondo.Centrar, servicio.Posicion);
    }

    // 9: cerrar la ventana minimizada no guarda la posición -32000.
    [Fact]
    public void VentanaMinimizada_ConservaLaPosicionAnterior()
    {
        var anterior = new EstadoVentana(100, 50, 1280, 860, false);
        Assert.Equal(anterior, EstadoVentana.AlCerrar(anterior, -32000, -32000, 160, 28, maximizada: false, minimizada: true));
    }

    [Fact]
    public void VentanaMaximizada_ConservaElTamanoNormal()
    {
        var anterior = new EstadoVentana(100, 50, 1280, 860, false);
        Assert.Equal(anterior with { Maximizada = true },
            EstadoVentana.AlCerrar(anterior, -8, -8, 3856, 1096, maximizada: true, minimizada: false));
    }

    [Fact]
    public void VentanaNormal_GuardaLaPosicionActual()
    {
        Assert.Equal(new EstadoVentana(10, 20, 900, 700, false),
            EstadoVentana.AlCerrar(null, 10, 20, 900, 700, maximizada: false, minimizada: false));
    }

    [Fact]
    public void VentanaMinimizadaSinAnterior_NoGuardaNada()
    {
        Assert.Null(EstadoVentana.AlCerrar(null, -32000, -32000, 160, 28, maximizada: false, minimizada: true));
    }

    // 10: las imágenes elegidas para un monitor desconectado no se pierden al guardar.
    [Fact]
    public void Pendiente_ConservaZonasDeMonitoresAusentes()
    {
        var guardada = new Combinacion("a",
        [
            new AsignacionZona([M2.Id, M1.Id], "pano-vieja.jpg", new Encuadre()),
            new AsignacionZona([M3.Id], "m3.jpg", new Encuadre()),
        ]);
        var sinM3 = new Disposicion([M1, M2]);
        var actual = new Combinacion("b", [new AsignacionZona([M2.Id, M1.Id], "pano-nueva.jpg", new Encuadre())]);

        var fusion = Combinacion.FusionarPendiente(guardada, actual, sinM3);

        Assert.Equal(2, fusion!.Zonas.Count);
        Assert.Contains(fusion.Zonas, z => z.RutaImagen == "pano-nueva.jpg");
        Assert.Contains(fusion.Zonas, z => z.RutaImagen == "m3.jpg");
        Assert.DoesNotContain(fusion.Zonas, z => z.RutaImagen == "pano-vieja.jpg");
    }

    [Fact]
    public void Pendiente_VacioSiNoQuedaNada()
    {
        Assert.Null(Combinacion.FusionarPendiente(null, new Combinacion("b", []), Usuario));
    }

    // 11: el mensaje de monitor desconectado no muestra la ruta técnica del dispositivo.
    [Fact]
    public void MonitorDesconectado_MensajeLegible()
    {
        var (_, aplicador, _) = Nuevo();
        var m3 = _img.Franjas("m3.png", 192, 120, SKColors.Green);
        var sinM3 = new Disposicion([M1, M2]);

        var r = aplicador.Aplicar(new Combinacion("x", [new AsignacionZona([M3.Id], m3, new Encuadre())]),
            sinM3, LienzoFisico.DesdeDisposicion(sinM3, new Dictionary<string, double>()));

        Assert.False(r.Exito);
        Assert.Contains(r.Errores, e => e.Contains("ya no está conectado"));
        Assert.DoesNotContain(r.Errores, e => e.Contains(@"\\?\"));
    }

    // 12: varios recortes de la misma imagen en una sola pasada.
    [Fact]
    public void RenderizarVarios_CadaRecorteCorrecto()
    {
        var origen = _img.Franjas("rojo azul.png", 400, 100, SKColors.Red, SKColors.Blue);
        var izq = Path.Combine(_img.Carpeta, "izq.png");
        var der = Path.Combine(_img.Carpeta, "der.png");

        new RenderizadorSkia().Renderizar(origen,
        [
            new Recorte(new RectD(0, 0, 200, 100), new SizePx(40, 20), izq),
            new Recorte(new RectD(200, 0, 200, 100), new SizePx(40, 20), der),
        ]);

        Assert.True(ImagenesPrueba.EsRojo(ImagenesPrueba.Pixel(izq, 20, 10)));
        Assert.True(ImagenesPrueba.EsAzul(ImagenesPrueba.Pixel(der, 20, 10)));
    }
}
