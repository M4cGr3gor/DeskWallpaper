using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas.Video;

/// <summary>animado.json: qué trozo del vídeo va a cada monitor.</summary>
public class ConfigAnimadoPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private static readonly SizePx Pano = new(3840, 1080);
    private static readonly OpcionesReproduccion Opciones = new();

    public void Dispose() => _img.Dispose();

    private static LienzoFisico Lienzo(double marcoMm = 0) => LienzoFisico.DesdeDisposicion(Usuario,
        marcoMm == 0 ? new Dictionary<string, double>() : new Dictionary<string, double> { [LienzoFisico.ClaveMarco(M2.Id, M1.Id)] = marcoMm });

    private static ConfigAnimado Desde(Combinacion c, LienzoFisico l, SizePx? tamano = null) =>
        GeneradorAnimado.Desde(c, Usuario, l, _ => tamano ?? Pano, Opciones);

    [Fact]
    public void Desde_VideoEn2y1_DosPantallasConMitades()
    {
        var c = new Combinacion("x", [new AsignacionZona([M2.Id, M1.Id], @"C:\v\pano.mp4", new Encuadre())]);

        var cfg = Desde(c, Lienzo());

        var zona = Assert.Single(cfg.Zonas);
        Assert.Equal(@"C:\v\pano.mp4", zona.Video);
        var m2 = zona.Pantallas.Single(p => p.MonitorId == M2.Id);
        var m1 = zona.Pantallas.Single(p => p.MonitorId == M1.Id);
        // Mitades a lo ancho; a lo alto casi entero (las medidas físicas de los LG no son exactamente 32:9).
        Assert.Equal(0, m2.U0, 2); Assert.Equal(0, m2.V0, 2); Assert.Equal(0.5, m2.U1, 2); Assert.Equal(1, m2.V1, 2);
        Assert.Equal(0.5, m1.U0, 2); Assert.Equal(0, m1.V0, 2); Assert.Equal(1, m1.U1, 2); Assert.Equal(1, m1.V1, 2);
        Assert.Equal(m2.U1, m1.U0, 6);
        Assert.Equal(30, cfg.TopeFps);
        Assert.True(cfg.PausarMaximizadas);
        Assert.False(cfg.PausaManual);
    }

    [Fact]
    public void Desde_ConMarco_ElTrozoSaltaElHueco()
    {
        var l = Lienzo(20);
        var c = new Combinacion("x", [new AsignacionZona([M2.Id, M1.Id], @"C:\v\pano.mp4", new Encuadre())]);

        var zona = Assert.Single(Desde(c, l).Zonas);

        var m2 = zona.Pantallas.Single(p => p.MonitorId == M2.Id);
        var m1 = zona.Pantallas.Single(p => p.MonitorId == M1.Id);
        Assert.True(m2.U1 < m1.U0);
        var plan = PlanRecorte.Calcular(Pano, [M2.Id, M1.Id], Usuario, l, new Encuadre());
        var r1 = plan.Single(p => p.MonitorId == M1.Id).Region;
        Assert.Equal(r1.X / Pano.Ancho, m1.U0, 6);
        Assert.Equal((r1.X + r1.Ancho) / Pano.Ancho, m1.U1, 6);
    }

    [Fact]
    public void Desde_SoloImagenes_SinZonas()
    {
        var c = new Combinacion("x", [new AsignacionZona([M2.Id, M1.Id], @"C:\i\pano.jpg", new Encuadre())]);

        Assert.Empty(Desde(c, Lienzo()).Zonas);
    }

    [Fact]
    public void Desde_Mezcla_SoloLaZonaDeVideo()
    {
        var c = new Combinacion("x",
        [
            new AsignacionZona([M2.Id, M1.Id], @"C:\v\pano.mp4", new Encuadre()),
            new AsignacionZona([M3.Id], @"C:\i\abajo.jpg", new Encuadre()),
        ]);

        var zona = Assert.Single(Desde(c, Lienzo()).Zonas);
        Assert.Equal(2, zona.Pantallas.Count);
    }

    [Fact]
    public void Desde_ValoresFueraDeRango_SeRecortanA01()
    {
        // Un vídeo muy alto en la zona ancha con zoom: el plan pide trozos que pueden salirse por redondeo.
        var c = new Combinacion("x", [new AsignacionZona([M2.Id, M1.Id], @"C:\v\alto.mp4", new Encuadre(0, 1, 1))]);

        var zona = Assert.Single(Desde(c, Lienzo(), new SizePx(1000, 3000)).Zonas);

        Assert.All(zona.Pantallas, p =>
        {
            foreach (var v in new[] { p.U0, p.V0, p.U1, p.V1 }) Assert.InRange(v, 0, 1);
            Assert.True(p.U1 > p.U0 && p.V1 > p.V0);
        });
    }

    [Fact]
    public void Almacen_RutasConAcentosYAmpersand_SeLeenIguales()
    {
        var datos = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "Prueba & ñ á")).FullName;
        var cfg = new ConfigAnimado(30, true, false,
            [new ZonaAnimada(Path.Combine(datos, "Fondo «río» & mar.mp4"), [new PantallaAnimada(M2.Id, 0, 0, 0.5, 1)])]);

        AlmacenAnimado.Guardar(datos, cfg);
        var leido = AlmacenAnimado.Leer(datos);

        Assert.NotNull(leido);
        Assert.Equal(cfg.Zonas[0].Video, leido.Zonas[0].Video);
        Assert.Equal(cfg.Zonas[0].Pantallas, leido.Zonas[0].Pantallas);
        Assert.Equal((cfg.TopeFps, cfg.PausarMaximizadas, cfg.PausaManual), (leido.TopeFps, leido.PausarMaximizadas, leido.PausaManual));
        var bytes = File.ReadAllBytes(Path.Combine(datos, AlmacenAnimado.Archivo));
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.Contains("\"topeFps\"", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Almacen_ArchivoDanado_LeerDevuelveNull()
    {
        var datos = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "datos")).FullName;
        File.WriteAllText(Path.Combine(datos, AlmacenAnimado.Archivo), "{ esto no es json");

        Assert.Null(AlmacenAnimado.Leer(datos));
    }
}
