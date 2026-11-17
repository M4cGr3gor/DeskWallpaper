using DeskWallpaper.Nucleo;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas.Video;

/// <summary>Vídeos en la biblioteca y pestañas.</summary>
public class BibliotecaVideosPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly LectorVideoFalso _lector = new(384, 108, 20);
    private readonly string _datos;
    private readonly string _carpeta;

    public BibliotecaVideosPruebas()
    {
        _datos = Path.Combine(_img.Carpeta, "datos");
        _carpeta = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "A")).FullName;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _img.Dispose();
    }

    private string Video(string nombre)
    {
        var ruta = Path.Combine(_carpeta, nombre);
        File.WriteAllBytes(ruta, System.Text.Encoding.UTF8.GetBytes("vídeo " + nombre));
        return ruta;
    }

    private Task<ProgresoEscaneo> Escanear(RepositorioBiblioteca repo, bool conFotogramas = true) =>
        new EscanerBiblioteca(repo, new RenderizadorSkia(), null, conFotogramas ? new FotogramasVideo(_datos, _lector) : null)
            .EscanearAsync([new CarpetaVigilada(_carpeta, false)], null, CancellationToken.None);

    [Fact]
    public async Task Escaner_RecogeVideosConDuracionYTamano()
    {
        Video("a.mp4");
        _img.Franjas(Path.Combine("A", "foto.png"), 300, 100, SKColors.Blue);
        _lector.Color = SKColors.Red;
        var repo = new RepositorioBiblioteca(_datos);

        var p = await Escanear(repo);

        Assert.Equal(0, p.Omitidas);
        var video = Assert.Single(repo.Listar(), i => i.EsVideo);
        Assert.Equal(20000, video.DuracionMs);
        Assert.Equal(new SizePx(384, 108), video.Tamano);
        Assert.Contains(ColorNombre.Rojo, video.Colores);   // los colores salen de su fotograma
        Assert.Single(repo.Listar(), i => !i.EsVideo);
    }

    [Fact]
    public async Task Escaner_VideoGuardadoSinDuracion_LaVuelveALeer()
    {
        // Un MP4 fragmentado de Pexels entró con duración 0 (Media Foundation no la daba) y
        // el escáner lo saltaba siempre por no haber cambiado el archivo: «▶ 0:00» para siempre.
        Video("pexels.mp4");
        var repo = new RepositorioBiblioteca(_datos);
        await new EscanerBiblioteca(repo, new RenderizadorSkia(), null, new FotogramasVideo(_datos, new LectorVideoFalso(384, 108, 0)))
            .EscanearAsync([new CarpetaVigilada(_carpeta, false)], null, CancellationToken.None);
        Assert.Equal(0, Assert.Single(repo.Listar()).DuracionMs);

        await Escanear(repo);   // el ayudante ya la sabe sacar (20 s en el lector de prueba)

        Assert.Equal(20000, Assert.Single(repo.Listar()).DuracionMs);
    }

    [Theory]
    [InlineData(@"D:\Videos\olas.mp4", true)]      // Un vídeo nuevo no llegaba a la biblioteca
    [InlineData(@"D:\Videos\nubes.WEBM", true)]
    [InlineData(@"D:\Imagenes\foto.jpg", true)]
    [InlineData(@"D:\Imagenes\Nueva carpeta", true)] // sin extensión: puede ser una carpeta
    [InlineData(@"D:\Imagenes\notas.txt", false)]
    [InlineData(@"D:\Datos\cache\videos\x.mp4", false)] // lo que genera la propia app: escaneos sin fin
    public void Vigilante_AvisaDeImagenesYVideosPeroNoDeLaCarpetaDeDatos(string ruta, bool interesa)
    {
        Assert.Equal(interesa, EscanerBiblioteca.CambioInteresa(ruta, @"D:\Datos"));
    }

    [Fact]
    public async Task Escaner_VideoIlegible_SeOmite()
    {
        Video("roto.mp4");
        Video("bueno.mp4");
        var repo = new RepositorioBiblioteca(_datos);

        var p = await Escanear(repo);

        Assert.Equal(1, p.Omitidas);
        Assert.Equal("bueno.mp4", Assert.Single(repo.Listar()).Nombre);
    }

    [Fact]
    public async Task Escaner_SinFotogramas_IgnoraVideos()
    {
        Video("a.mp4");
        var repo = new RepositorioBiblioteca(_datos);

        var p = await Escanear(repo, conFotogramas: false);

        Assert.Equal(0, p.Total);
        Assert.Empty(repo.Listar());
    }

    [Fact]
    public void Repositorio_MigraDeLaVersion2SinPerderNada()
    {
        Directory.CreateDirectory(_datos);
        var db = Path.Combine(_datos, "biblioteca.db");
        using (var con = new SqliteConnection($"Data Source={db}"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE imagenes (id INTEGER PRIMARY KEY AUTOINCREMENT, huella TEXT NOT NULL UNIQUE, ancho INTEGER NOT NULL, alto INTEGER NOT NULL,
                  colores TEXT NOT NULL, favorita INTEGER NOT NULL, anadida TEXT NOT NULL);
                CREATE TABLE rutas (ruta TEXT PRIMARY KEY COLLATE NOCASE, imagen_id INTEGER NOT NULL, bytes INTEGER NOT NULL, modificado TEXT NOT NULL);
                CREATE TABLE etiquetas (imagen_id INTEGER NOT NULL, etiqueta TEXT NOT NULL COLLATE NOCASE, UNIQUE(imagen_id, etiqueta));
                INSERT INTO imagenes VALUES (1, 'h1', 3840, 1080, 'Azul', 1, '2026-11-01T10:00:00.0000000Z');
                INSERT INTO rutas VALUES ('D:\Imagenes\a.jpg', 1, 100, '2026-11-01T10:00:00.0000000Z');
                INSERT INTO etiquetas VALUES (1, 'Mar');
                PRAGMA user_version = 2;
                """;
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();

        var imagen = Assert.Single(new RepositorioBiblioteca(_datos).Listar());

        Assert.True(imagen.Favorita);
        Assert.Equal(["Mar"], imagen.Etiquetas);
        Assert.Equal(0, imagen.DuracionMs);
        using var con2 = new SqliteConnection($"Data Source={db}");
        con2.Open();
        using var version = con2.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(3L, (long)version.ExecuteScalar()!);
    }

    [Fact]
    public void Filtro_PorPestana()
    {
        var lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());
        var zonas = ZonasPorDefecto.Calcular(Usuario);
        IReadOnlyList<ImagenBiblioteca> todas =
        [
            new(1, "h1", @"D:\a.jpg", new SizePx(3840, 1080), [], false, [], DateTime.UtcNow),
            new(2, "h2", @"D:\b.png", new SizePx(1920, 1080), [], false, [], DateTime.UtcNow),
            new(3, "h3", @"D:\c.mp4", new SizePx(3840, 1080), [], false, [], DateTime.UtcNow) { DuracionMs = 20000 },
        ];
        int Cuantas(Pestana p, SeccionBiblioteca s = SeccionBiblioteca.Todas) =>
            FiltroBiblioteca.Aplicar(todas, new CriterioBiblioteca(s, "", null, null, p), zonas, lienzo).Count;

        Assert.Equal(2, Cuantas(Pestana.Imagenes));
        Assert.Equal(1, Cuantas(Pestana.Animados));
        Assert.Equal(1, Cuantas(Pestana.Imagenes, SeccionBiblioteca.Panoramica));
        Assert.Equal(1, Cuantas(Pestana.Animados, SeccionBiblioteca.Panoramica));
        Assert.Equal(0, Cuantas(Pestana.Animados, SeccionBiblioteca.UnMonitor));
    }
}
