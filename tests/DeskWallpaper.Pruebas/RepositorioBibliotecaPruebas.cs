using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas;

public class RepositorioBibliotecaPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    public void Dispose() => _img.Dispose();

    private RepositorioBiblioteca Nuevo() => new(Path.Combine(_img.Carpeta, "datos"));
    private static readonly DateTime Mod = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    [Fact]
    public void Guardar_Y_Listar()
    {
        var repo = Nuevo();
        repo.Guardar("h1", @"D:\Imagenes\a.jpg", new SizePx(3840, 1080), [ColorNombre.Azul, ColorNombre.Naranja], 1234, Mod);

        var img = Assert.Single(repo.Listar());
        Assert.Equal("h1", img.Huella);
        Assert.Equal(@"D:\Imagenes\a.jpg", img.Ruta);
        Assert.Equal(new SizePx(3840, 1080), img.Tamano);
        Assert.Equal([ColorNombre.Azul, ColorNombre.Naranja], img.Colores);
        Assert.False(img.Favorita);
        Assert.Empty(img.Etiquetas);
    }

    [Fact]
    public void Disponible_SegunExistaAlgunaRuta()
    {
        var repo = Nuevo();
        var existe = _img.Texto("existe.jpg", "x");
        repo.Guardar("h1", existe, new SizePx(10, 10), [], 1, Mod);
        repo.Guardar("h2", @"Z:\Disco desconectado\b.jpg", new SizePx(10, 10), [], 1, Mod);

        var lista = repo.Listar();
        Assert.True(lista.Single(i => i.Huella == "h1").Disponible);
        Assert.False(lista.Single(i => i.Huella == "h2").Disponible);
    }

    [Fact]
    public void BaseDeDatosAntigua_ReleeSoloLosBmp()
    {
        Nuevo().Guardar("h1", @"D:\A\a.bmp", new SizePx(10, 10), [], 1234, Mod);
        Nuevo().Guardar("h2", @"D:\A\b.jpg", new SizePx(10, 10), [], 5678, Mod);
        // Como la dejaba la fase 3 antes de versionar el esquema.
        using (var con = new Microsoft.Data.Sqlite.SqliteConnection(
                   $"Data Source={Path.Combine(_img.Carpeta, "datos", "biblioteca.db")};Pooling=False"))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 0";
            cmd.ExecuteNonQuery();
        }

        var repo = Nuevo();

        Assert.Equal(-1, repo.FirmaDeRuta(@"D:\A\a.bmp")!.Value.Bytes);
        Assert.Equal(5678, repo.FirmaDeRuta(@"D:\A\b.jpg")!.Value.Bytes);
    }

    [Fact]
    public void EtiquetasConMayusculasNoAscii_CuentanComoUna()
    {
        var repo = Nuevo();
        var a = repo.Guardar("h1", @"D:\A\a.jpg", new SizePx(10, 10), [], 1, Mod);
        var b = repo.Guardar("h2", @"D:\A\b.jpg", new SizePx(10, 10), [], 1, Mod);
        repo.PonerEtiquetas(a.Id, ["Ñoño"]);
        repo.PonerEtiquetas(b.Id, ["ñoño"]);

        var (etiqueta, usos) = Assert.Single(repo.Etiquetas());
        Assert.Equal(2, usos);
        Assert.Equal("Ñoño", etiqueta, ignoreCase: true);
    }

    [Fact]
    public void MismaHuellaDosRutas_UnaSolaImagen()
    {
        var repo = Nuevo();
        repo.Guardar("h1", @"D:\A\a.jpg", new SizePx(10, 10), [], 1, Mod);
        repo.Guardar("h1", @"D:\B\copia.jpg", new SizePx(10, 10), [], 1, Mod);
        Assert.Single(repo.Listar());

        repo.QuitarRuta(@"D:\A\a.jpg");
        var img = Assert.Single(repo.Listar());
        Assert.Equal(@"D:\B\copia.jpg", img.Ruta);
    }

    [Fact]
    public void QuitarUltimaRuta_QuedaAusente_YConservaEtiquetasYFavorita()
    {
        var repo = Nuevo();
        var img = repo.Guardar("h1", @"D:\A\a.jpg", new SizePx(10, 10), [], 1, Mod);
        repo.PonerEtiquetas(img.Id, ["Paisaje"]);
        repo.PonerFavorita(img.Id, true);

        repo.QuitarRuta(@"D:\A\a.jpg");
        Assert.Empty(repo.Listar());

        repo.Guardar("h1", @"D:\B\renombrada.jpg", new SizePx(10, 10), [], 1, Mod);
        var vuelta = Assert.Single(repo.Listar());
        Assert.Equal(@"D:\B\renombrada.jpg", vuelta.Ruta);
        Assert.Equal(["Paisaje"], vuelta.Etiquetas);
        Assert.True(vuelta.Favorita);
    }

    [Fact]
    public void Etiquetas_NormalizaYCuenta()
    {
        var repo = Nuevo();
        var a = repo.Guardar("h1", @"D:\a.jpg", new SizePx(10, 10), [], 1, Mod);
        var b = repo.Guardar("h2", @"D:\b.jpg", new SizePx(10, 10), [], 1, Mod);
        repo.PonerEtiquetas(a.Id, [" Paisaje", "paisaje", "", "Noche"]);
        repo.PonerEtiquetas(b.Id, ["noche"]);

        Assert.Equal(["Paisaje", "Noche"], repo.Listar().Single(i => i.Id == a.Id).Etiquetas);
        var usos = repo.Etiquetas().ToDictionary(e => e.Etiqueta, e => e.Usos, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, usos["Noche"]);
        Assert.Equal(1, usos["Paisaje"]);
    }

    [Fact]
    public void Favorita_IdaYVuelta()
    {
        var repo = Nuevo();
        var a = repo.Guardar("h1", @"D:\a.jpg", new SizePx(10, 10), [], 1, Mod);
        repo.PonerFavorita(a.Id, true);
        Assert.True(repo.Listar()[0].Favorita);
        repo.PonerFavorita(a.Id, false);
        Assert.False(repo.Listar()[0].Favorita);
    }

    [Fact]
    public void FirmaDeRuta_SinDistinguirMayusculas()
    {
        var repo = Nuevo();
        repo.Guardar("h1", @"D:\Imagenes\A.JPG", new SizePx(10, 10), [], 777, Mod);
        Assert.Equal((777L, Mod), repo.FirmaDeRuta(@"d:\imagenes\a.jpg"));
        Assert.Null(repo.FirmaDeRuta(@"D:\otra.jpg"));
        Assert.Equal("h1", repo.PorRuta(@"d:\IMAGENES\a.jpg")?.Huella);
    }

    [Fact]
    public void RutasDentroDe_SoloLasDeEsaCarpeta()
    {
        var repo = Nuevo();
        repo.Guardar("h1", @"D:\Imagenes\Dual\a.jpg", new SizePx(10, 10), [], 1, Mod);
        repo.Guardar("h2", @"D:\Imagenes\Dual\sub\b.jpg", new SizePx(10, 10), [], 1, Mod);
        repo.Guardar("h3", @"D:\Imagenes\Dual monitor\c.jpg", new SizePx(10, 10), [], 1, Mod);
        Assert.Equal([@"D:\Imagenes\Dual\a.jpg", @"D:\Imagenes\Dual\sub\b.jpg"], repo.RutasDentroDe(@"D:\Imagenes\Dual").Order());
    }
}
