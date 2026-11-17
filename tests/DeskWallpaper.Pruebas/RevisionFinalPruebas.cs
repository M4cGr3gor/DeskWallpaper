using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

/// <summary>Pruebas de los hallazgos de la revisión final de la fase 1.</summary>
public class RevisionFinalPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();

    public void Dispose() => _img.Dispose();

    // Hallazgo 1: el EDID del ANMITE (350×210 mm) no tiene la proporción de sus píxeles (1920×1200).

    [Fact]
    public void PixelesCuadrados_ProporcionDeLosPixelesYMismaDiagonal()
    {
        var t = TamanoPantalla.PixelesCuadrados(new SizeMm(350, 210), 1920, 1200);
        Assert.Equal(1.6, t.Ancho / t.Alto, 9);
        Assert.Equal(Math.Sqrt(350 * 350 + 210 * 210), Math.Sqrt(t.Ancho * t.Ancho + t.Alto * t.Alto), 6);
    }

    [Fact]
    public void ImagenNativaDelMonitor3_SeUsaEntera()
    {
        var m = new DatosMonitor("M3", 3, "ANMITE", new RectPx(0, 0, 1920, 1200), 1.5,
            TamanoPantalla.PixelesCuadrados(new SizeMm(350, 210), 1920, 1200), true);
        var d = new Disposicion([m]);
        var l = LienzoFisico.DesdeDisposicion(d, new Dictionary<string, double>());
        var env = l.Envolvente(ZonasPorDefecto.Calcular(d)[0]);
        var r = Recortador.RegionZona(new SizePx(1920, 1200), env, new Encuadre());
        Assert.Equal(0, r.X, 3);
        Assert.Equal(0, r.Y, 3);
        Assert.Equal(1920, r.Ancho, 3);
        Assert.Equal(1200, r.Alto, 3);
    }

    [Fact]
    public void FactorAmpliacion_UsaElEjeQueMasAmplia()
    {
        Assert.Equal(1.2, Recortador.FactorAmpliacion(new RectD(0, 0, 1920, 1000), new SizePx(1920, 1200)), 9);
    }

    // Hallazgo 2: la limpieza no debe borrar el fondo de un monitor apagado.

    [Fact]
    public void Limpieza_ConservaElUltimoFondoDeUnMonitorInactivo()
    {
        var servicio = new ServicioFondosFalso([M1.Id, M2.Id, M3.Id]);
        var aplicador = new AplicadorFondos(servicio, new RenderizadorSkia(), Path.Combine(_img.Carpeta, "datos"));
        var pano = _img.Franjas("pano.png", 384, 108, SKColors.Red);
        var m3 = _img.Franjas("m3.png", 192, 120, SKColors.Green);
        var lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());
        aplicador.Aplicar(new Combinacion("x",
        [
            new AsignacionZona([M2.Id, M1.Id], pano, new Encuadre()),
            new AsignacionZona([M3.Id], m3, new Encuadre()),
        ]), Usuario, lienzo);
        var fondoM3 = servicio.Fondos[M3.Id];

        servicio.Inactivos.Add(M3.Id);
        var sinM3 = new Disposicion([M1, M2]);
        var r = aplicador.Aplicar(new Combinacion("y", [new AsignacionZona([M2.Id, M1.Id], pano, new Encuadre())]),
            sinM3, LienzoFisico.DesdeDisposicion(sinM3, new Dictionary<string, double>()));

        Assert.True(r.Exito, string.Join("; ", r.Errores));
        Assert.True(File.Exists(fondoM3));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(_img.Carpeta, "datos", "cache", "fondos")).Length);
    }

    // Hallazgo 3: poner los fondos también es todo o nada.

    [Fact]
    public void MonitorQueWindowsNoTieneActivo_ErrorSinTocarNada()
    {
        var servicio = new ServicioFondosFalso([M1.Id, M2.Id]);
        var aplicador = new AplicadorFondos(servicio, new RenderizadorSkia(), Path.Combine(_img.Carpeta, "datos"));
        var m3 = _img.Franjas("m3.png", 192, 120, SKColors.Green);

        var r = aplicador.Aplicar(new Combinacion("x", [new AsignacionZona([M3.Id], m3, new Encuadre())]),
            Usuario, LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>()));

        Assert.False(r.Exito);
        Assert.Contains(r.Errores, e => e.Contains("ya no está conectado"));
        Assert.Equal(0, servicio.LlamadasPonerFondo);
        Assert.Equal(PosicionFondo.Centrar, servicio.Posicion);
    }

    [Fact]
    public void FalloAlPonerUnFondo_RestauraLoAnterior()
    {
        var servicio = new ServicioFondosFalso([M1.Id, M2.Id, M3.Id]);
        servicio.Fondos[M1.Id] = "antes1.jpg";
        servicio.Fondos[M2.Id] = "antes2.jpg";
        servicio.Fondos[M3.Id] = "antes3.jpg";
        servicio.FallarUnaVezEn = M1.Id;
        var aplicador = new AplicadorFondos(servicio, new RenderizadorSkia(), Path.Combine(_img.Carpeta, "datos"));
        var pano = _img.Franjas("pano.png", 384, 108, SKColors.Red);

        var r = aplicador.Aplicar(new Combinacion("x", [new AsignacionZona([M2.Id, M1.Id], pano, new Encuadre())]),
            Usuario, LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>()));

        Assert.False(r.Exito);
        Assert.Contains(r.Errores, e => e.Contains("No se han podido poner los fondos"));
        Assert.Equal("antes1.jpg", servicio.Fondos[M1.Id]);
        Assert.Equal("antes2.jpg", servicio.Fondos[M2.Id]);
        Assert.Equal("antes3.jpg", servicio.Fondos[M3.Id]);
        Assert.Equal(PosicionFondo.Centrar, servicio.Posicion);
    }

    // Hallazgo 5: un JPEG a medio descargar es un error, no un fondo con una franja negra.

    [Fact]
    public void JpegTruncado_LanzaExcepcionImagen()
    {
        var completo = _img.Franjas("completo.jpg", 400, 300, SKColors.Red, SKColors.Blue);
        var bytes = File.ReadAllBytes(completo);
        var truncado = Path.Combine(_img.Carpeta, "truncado.jpg");
        File.WriteAllBytes(truncado, bytes[..(bytes.Length * 6 / 10)]);

        Assert.Throws<ExcepcionImagen>(() => new RenderizadorSkia().Renderizar(
            truncado, new RectD(0, 0, 400, 300), new SizePx(40, 30), Path.Combine(_img.Carpeta, "s.png")));
    }

    // Hallazgo 6 (riesgo 5 del plan): configuración ilegible o con valores nulos.

    [Fact]
    public void ConfiguracionBloqueada_PorDefectoSinFallar()
    {
        var carpeta = Path.Combine(_img.Carpeta, "cfg");
        AlmacenConfiguracion.Guardar(carpeta, Configuracion.PorDefecto());
        using var bloqueo = new FileStream(Path.Combine(carpeta, AlmacenConfiguracion.NombreArchivo),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var c = AlmacenConfiguracion.Cargar(carpeta);

        Assert.Equal(2, c.CarpetasVigiladas.Count);
    }

    [Fact]
    public void LienzoPendienteConNulos_SeDescartanLasZonasInvalidas()
    {
        var carpeta = Path.Combine(_img.Carpeta, "cfg2");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, AlmacenConfiguracion.NombreArchivo),
            """{"CarpetasVigiladas":null,"LienzoPendiente":{"Zonas":[{},{"MonitorIds":["A"],"RutaImagen":"x.jpg"}]}}""");

        var c = AlmacenConfiguracion.Cargar(carpeta);

        Assert.NotNull(c.CarpetasVigiladas);
        var zona = Assert.Single(c.LienzoPendiente!.Zonas);
        Assert.Equal("x.jpg", zona.RutaImagen);
        Assert.Equal(new Encuadre(), zona.Encuadre);
    }
}
