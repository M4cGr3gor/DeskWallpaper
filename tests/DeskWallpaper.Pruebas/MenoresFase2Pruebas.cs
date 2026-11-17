using DeskWallpaper.Nucleo;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

/// <summary>Fallos menores que dejó pendientes la revisión final de la fase 2.</summary>
public class MenoresFase2Pruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();

    public void Dispose() => _img.Dispose();

    // Modo "color sólido" de Windows: ningún monitor tiene imagen. "Volver" debe dejarlo igual.
    [Fact]
    public void Volver_RestauraModoColorSolido()
    {
        var servicio = new ServicioFondosFalso([M1.Id, M2.Id, M3.Id]) { Color = 0x00336699 };
        var prueba = new PruebaFondos(servicio, new AplicadorFondos(servicio, new RenderizadorSkia(), Path.Combine(_img.Carpeta, "datos")));

        Assert.True(prueba.Empezar(Panoramica(), Usuario, Lienzo()).Exito);
        Assert.NotEqual("", servicio.Fondos[M1.Id]);
        servicio.Color = 0; // cualquier cambio de color durante la prueba también se deshace

        Assert.True(prueba.Volver());
        Assert.Equal(1, servicio.LlamadasQuitarImagenes);
        Assert.All(servicio.Fondos.Values, f => Assert.Equal("", f));
        Assert.Equal(0x00336699u, servicio.Color);
    }

    // Un monitor sin imagen y otros con imagen no es un estado que Windows mantenga: se avisa.
    [Fact]
    public void Volver_MonitorSueltoSinImagen_AvisaDeQueNoPudo()
    {
        var servicio = new ServicioFondosFalso([M1.Id, M2.Id, M3.Id]);
        servicio.Fondos[M3.Id] = "tres.jpg";
        var prueba = new PruebaFondos(servicio, new AplicadorFondos(servicio, new RenderizadorSkia(), Path.Combine(_img.Carpeta, "datos")));

        Assert.True(prueba.Empezar(Panoramica(), Usuario, Lienzo()).Exito);

        Assert.False(prueba.Volver());
        Assert.Equal("tres.jpg", servicio.Fondos[M3.Id]);
        Assert.Equal(0, servicio.LlamadasQuitarImagenes);
    }

    private Combinacion Panoramica() =>
        new("x", [new AsignacionZona([M2.Id, M1.Id], _img.Franjas("pano.png", 384, 108, SKColors.Green), new Encuadre())]);

    private static LienzoFisico Lienzo() => LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());

    // El último uso del historial se guarda en UTC: el cambio de hora no altera el orden.
    [Fact]
    public void Historial_GuardaFechasEnUtc()
    {
        var datos = Path.Combine(_img.Carpeta, "datos");
        var h = new Historial(datos);
        var local = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Local);
        var c = new Combinacion("f", [new AsignacionZona([M3.Id], @"D:\a.jpg", new Encuadre())]);
        h.Registrar(c, [1], local);

        using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(datos, "biblioteca.db"), Pooling = false }.ToString()))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT ultimo_uso, creada FROM historial";
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.EndsWith("Z", r.GetString(0));
            Assert.EndsWith("Z", r.GetString(1));
        }

        var e = Assert.Single(h.Listar());
        Assert.Equal(DateTimeKind.Local, e.UltimoUso.Kind);
        Assert.Equal(local, e.UltimoUso);
    }

    // Unas zonas guardadas con monitores que no se tocan no se aceptan.
    [Fact]
    public void ParaDisposicion_ZonaNoContigua_UsaPorDefecto()
    {
        var m3Lejos = M3 with { Escritorio = new RectPx(1000, 1080, 1920, 1200) }; // solo toca a M1 en una esquina
        var d = new Disposicion([M1, M2, m3Lejos]);
        var zonas = EditorZonas.ParaDisposicion(d, [[M2.Id, m3Lejos.Id], [M1.Id]]);
        Assert.Equal(ZonasPorDefecto.Calcular(d), zonas);
    }
}
