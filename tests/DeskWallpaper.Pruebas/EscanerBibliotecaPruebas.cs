using DeskWallpaper.Nucleo;
using SkiaSharp;

namespace DeskWallpaper.Pruebas;

/// <summary>Cuenta cuántas veces se leen imágenes (para comprobar que el escaneo es incremental).</summary>
internal sealed class RenderizadorContador(IRenderizador interior) : IRenderizador
{
    public int Lecturas;
    public SizePx LeerTamano(string ruta) { Interlocked.Increment(ref Lecturas); return interior.LeerTamano(ruta); }
    public void Renderizar(string rutaOrigen, IReadOnlyList<Recorte> recortes) => interior.Renderizar(rutaOrigen, recortes);
    public (byte[] Png, SizePx Original) Reducida(string ruta, int ladoMaximo) => interior.Reducida(ruta, ladoMaximo);
    public byte[] PixelesReducidos(string ruta, int lado) => interior.PixelesReducidos(ruta, lado);
    public byte[] Miniatura(string ruta, int lado) => interior.Miniatura(ruta, lado);
    public byte[] ComponerMiniatura(Combinacion c, Disposicion d, LienzoFisico l, int ancho) => interior.ComponerMiniatura(c, d, l, ancho);
}

public class EscanerBibliotecaPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();
    private readonly RepositorioBiblioteca _repo;
    private readonly RenderizadorContador _render = new(new RenderizadorSkia());
    private readonly EscanerBiblioteca _escaner;
    private readonly string _a;
    private readonly string _b;

    public EscanerBibliotecaPruebas()
    {
        _repo = new RepositorioBiblioteca(Path.Combine(_img.Carpeta, "datos"));
        _escaner = new EscanerBiblioteca(_repo, _render);
        _a = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "A")).FullName;
        _b = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "B")).FullName;
    }

    public void Dispose() => _img.Dispose();

    private string Crear(string carpeta, string nombre, SKColor color) =>
        _img.Franjas(Path.Combine(Path.GetFileName(carpeta), nombre), 300, 100, color);

    private Task<ProgresoEscaneo> Escanear(params CarpetaVigilada[] carpetas) =>
        _escaner.EscanearAsync(carpetas, null, CancellationToken.None);

    [Fact]
    public async Task PrimerEscaneo_AnadeTodas()
    {
        Crear(_a, "uno.png", SKColors.Red);
        Crear(_a, "dos.jpg", SKColors.Blue);
        Crear(_a, "tres.png", SKColors.Green);
        File.WriteAllText(Path.Combine(_a, "nota.txt"), "no es imagen");

        var p = await Escanear(new CarpetaVigilada(_a, false));

        Assert.Equal(3, _repo.Listar().Count);
        Assert.Equal(3, p.Total);
        Assert.Equal(0, p.Omitidas);
        Assert.Contains(_repo.Listar(), i => i.Colores.Contains(ColorNombre.Rojo));
    }

    [Fact]
    public async Task SegundoEscaneo_NoRecalcula()
    {
        Crear(_a, "uno.png", SKColors.Red);
        await Escanear(new CarpetaVigilada(_a, false));
        var antes = _render.Lecturas;

        await Escanear(new CarpetaVigilada(_a, false));

        Assert.Equal(antes, _render.Lecturas);
    }

    [Fact]
    public async Task Renombrar_ConservaEtiquetas()
    {
        var ruta = Crear(_a, "uno.png", SKColors.Red);
        await Escanear(new CarpetaVigilada(_a, false));
        _repo.PonerEtiquetas(_repo.Listar()[0].Id, ["Atardecer"]);

        var nueva = Path.Combine(_a, "renombrada.png");
        File.Move(ruta, nueva);
        await Escanear(new CarpetaVigilada(_a, false));

        var img = Assert.Single(_repo.Listar());
        Assert.Equal(nueva, img.Ruta, ignoreCase: true);
        Assert.Equal(["Atardecer"], img.Etiquetas);
    }

    [Fact]
    public async Task ModificarArchivo_ConservaEtiquetasYFavorita()
    {
        // Girar o retocar una imagen cambia su huella: sigue siendo "la misma" para el usuario.
        var ruta = Crear(_a, "uno.png", SKColors.Red);
        await Escanear(new CarpetaVigilada(_a, false));
        var antes = _repo.Listar()[0];
        _repo.PonerEtiquetas(antes.Id, ["Atardecer"]);
        _repo.PonerFavorita(antes.Id, true);

        Crear(_a, "uno.png", SKColors.Blue);
        File.SetLastWriteTimeUtc(ruta, File.GetLastWriteTimeUtc(ruta).AddMinutes(1));
        await Escanear(new CarpetaVigilada(_a, false));

        var img = Assert.Single(_repo.Listar());
        Assert.NotEqual(antes.Huella, img.Huella);
        Assert.Equal(["Atardecer"], img.Etiquetas);
        Assert.True(img.Favorita);
        Assert.Contains(ColorNombre.Azul, img.Colores);
    }

    [Fact]
    public async Task ModificarUnaCopia_LaCopiaHeredaEtiquetas()
    {
        var uno = Crear(_a, "uno.png", SKColors.Red);
        File.Copy(uno, Path.Combine(_a, "copia.png"));
        await Escanear(new CarpetaVigilada(_a, false));
        _repo.PonerEtiquetas(Assert.Single(_repo.Listar()).Id, ["Atardecer"]);

        Crear(_a, "uno.png", SKColors.Blue);
        File.SetLastWriteTimeUtc(uno, File.GetLastWriteTimeUtc(uno).AddMinutes(1));
        await Escanear(new CarpetaVigilada(_a, false));

        var lista = _repo.Listar();
        Assert.Equal(2, lista.Count);
        Assert.All(lista, i => Assert.Equal(["Atardecer"], i.Etiquetas));
    }

    [Fact]
    public async Task CarpetaDeDatosDentroDeUnaVigilada_NoSeIndexa()
    {
        // Si el usuario vigila una carpeta que contiene la de datos, las miniaturas y los fondos
        // generados no entran en la biblioteca.
        var datos = Path.Combine(_a, "datos");
        var escaner = new EscanerBiblioteca(_repo, _render, datos);
        Crear(_a, "uno.png", SKColors.Red);
        Directory.CreateDirectory(Path.Combine(datos, "cache"));
        _img.Franjas(Path.Combine("A", "datos", "cache", "fondo.png"), 300, 100, SKColors.Blue);

        await escaner.EscanearAsync([new CarpetaVigilada(_a, true)], null, CancellationToken.None);

        Assert.Single(_repo.Listar());
    }

    [Fact]
    public async Task EscaneoCancelado_NoQuitaRutas()
    {
        Crear(_a, "uno.png", SKColors.Red);
        await Escanear(new CarpetaVigilada(_a, false));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _escaner.EscanearAsync([], null, cts.Token));

        Assert.Single(_repo.Listar());
    }

    [Fact]
    public async Task MoverEntreCarpetasVigiladas_ConservaFavorita()
    {
        var ruta = Crear(_a, "uno.png", SKColors.Red);
        await Escanear(new CarpetaVigilada(_a, false), new CarpetaVigilada(_b, false));
        _repo.PonerFavorita(_repo.Listar()[0].Id, true);

        File.Move(ruta, Path.Combine(_b, "uno.png"));
        await Escanear(new CarpetaVigilada(_a, false), new CarpetaVigilada(_b, false));

        var img = Assert.Single(_repo.Listar());
        Assert.True(img.Favorita);
        Assert.StartsWith(_b, img.Ruta, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ArchivoDanado_SeOmite()
    {
        Crear(_a, "buena.png", SKColors.Red);
        File.WriteAllText(Path.Combine(_a, "mala.jpg"), "esto no es un jpeg");

        var p = await Escanear(new CarpetaVigilada(_a, false));

        Assert.Equal(1, p.Omitidas);
        Assert.Single(_repo.Listar());
    }

    [Fact]
    public async Task CarpetaInexistente_NoBorraNada()
    {
        Crear(_a, "uno.png", SKColors.Red);
        await Escanear(new CarpetaVigilada(_a, false));

        Directory.Move(_a, _a + "-desconectada"); // como un disco desconectado
        await Escanear(new CarpetaVigilada(_a, false));

        Assert.Single(_repo.Listar());
    }

    [Fact]
    public async Task QuitarCarpeta_SusImagenesDesaparecen()
    {
        Crear(_a, "uno.png", SKColors.Red);
        Crear(_b, "dos.png", SKColors.Blue);
        await Escanear(new CarpetaVigilada(_a, false), new CarpetaVigilada(_b, false));

        await Escanear(new CarpetaVigilada(_b, false));

        var img = Assert.Single(_repo.Listar());
        Assert.StartsWith(_b, img.Ruta, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(_a, "uno.png"))); // el original sigue ahí
    }

    [Fact]
    public async Task Subcarpetas_SoloSiSePide()
    {
        Directory.CreateDirectory(Path.Combine(_a, "sub"));
        Crear(_a, "uno.png", SKColors.Red);
        _img.Franjas(Path.Combine("A", "sub", "dos.png"), 300, 100, SKColors.Blue);

        await Escanear(new CarpetaVigilada(_a, false));
        Assert.Single(_repo.Listar());

        await Escanear(new CarpetaVigilada(_a, true));
        Assert.Equal(2, _repo.Listar().Count);

        await Escanear(new CarpetaVigilada(_a, false));
        Assert.Single(_repo.Listar());
    }

    [Fact]
    public async Task Cancelacion_Para()
    {
        for (var i = 0; i < 5; i++) Crear(_a, $"{i}.png", SKColors.Red);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _escaner.EscanearAsync([new CarpetaVigilada(_a, false)], null, cts.Token));
    }
}
