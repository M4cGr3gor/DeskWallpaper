using DeskWallpaper.Nucleo;
using SkiaSharp;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

/// <summary>Lanza una excepción cualquiera (como la de SkiaSharp al no poder reservar memoria) con un archivo.</summary>
internal sealed class RenderizadorQueFalla(IRenderizador interior, string nombreQueFalla) : IRenderizador
{
    private void Comprobar(string ruta)
    {
        if (Path.GetFileName(ruta) == nombreQueFalla) throw new Exception("Unable to allocate pixels for the bitmap.");
    }

    public SizePx LeerTamano(string ruta) { Comprobar(ruta); return interior.LeerTamano(ruta); }
    public void Renderizar(string rutaOrigen, IReadOnlyList<Recorte> recortes) => interior.Renderizar(rutaOrigen, recortes);
    public (byte[] Png, SizePx Original) Reducida(string ruta, int ladoMaximo) => interior.Reducida(ruta, ladoMaximo);
    public byte[] PixelesReducidos(string ruta, int lado) { Comprobar(ruta); return interior.PixelesReducidos(ruta, lado); }
    public byte[] Miniatura(string ruta, int lado) => interior.Miniatura(ruta, lado);
    public byte[] ComponerMiniatura(Combinacion c, Disposicion d, LienzoFisico l, int ancho) => interior.ComponerMiniatura(c, d, l, ancho);
}

/// <summary>Servicio cuya lista de monitores falla a partir de la llamada número <c>n</c>.</summary>
internal sealed class ServicioQueFallaAl(IServicioFondos interior, int n) : IServicioFondos
{
    private int _llamadas;

    public IReadOnlyList<string> MonitorIds() =>
        ++_llamadas >= n ? throw new InvalidOperationException("Error simulado") : interior.MonitorIds();

    public string? ObtenerFondo(string monitorId) => interior.ObtenerFondo(monitorId);
    public void PonerFondo(string monitorId, string ruta) => interior.PonerFondo(monitorId, ruta);
    public PosicionFondo ObtenerPosicion() => interior.ObtenerPosicion();
    public void PonerPosicion(PosicionFondo posicion) => interior.PonerPosicion(posicion);
    public void QuitarImagenes() => interior.QuitarImagenes();
    public uint ObtenerColorFondo() => interior.ObtenerColorFondo();
    public void PonerColorFondo(uint color) => interior.PonerColorFondo(color);
}

/// <summary>Errores encontrados en una revisión del código.</summary>
public class RevisionErroresPruebas : IDisposable
{
    private readonly ImagenesPrueba _img = new();

    public void Dispose() => _img.Dispose();

    [Fact]
    public async Task Escaner_UnaExcepcionCualquiera_SoloOmiteEseArchivo()
    {
        var carpeta = Directory.CreateDirectory(Path.Combine(_img.Carpeta, "A")).FullName;
        _img.Franjas(Path.Combine("A", "buena.png"), 300, 100, SKColors.Red);
        _img.Franjas(Path.Combine("A", "enorme.png"), 300, 100, SKColors.Blue);
        _img.Franjas(Path.Combine("A", "otra.png"), 300, 100, SKColors.Green);
        var repo = new RepositorioBiblioteca(Path.Combine(_img.Carpeta, "datos"));
        var escaner = new EscanerBiblioteca(repo, new RenderizadorQueFalla(new RenderizadorSkia(), "enorme.png"));

        var p = await escaner.EscanearAsync([new CarpetaVigilada(carpeta, false)], null, CancellationToken.None);

        Assert.Equal(1, p.Omitidas);
        Assert.Equal(2, repo.Listar().Count);
    }

    [Fact]
    public void Probar_SiFallaLaLimpiezaTrasPonerLosFondos_SePuedeVolver()
    {
        var falso = new ServicioFondosFalso([M1.Id, M2.Id, M3.Id]);
        var aplicadorPrevio = new AplicadorFondos(falso, new RenderizadorSkia(), Path.Combine(_img.Carpeta, "datos"));
        var lienzo = LienzoFisico.DesdeDisposicion(Usuario, new Dictionary<string, double>());
        Combinacion Comb(SKColor color) => new("x",
        [
            new AsignacionZona([M2.Id, M1.Id], _img.Franjas($"pano-{color}.png", 384, 108, color), new Encuadre()),
            new AsignacionZona([M3.Id], _img.Franjas($"m3-{color}.png", 192, 120, color), new Encuadre()),
        ]);
        aplicadorPrevio.Aplicar(Comb(SKColors.Red), Usuario, lienzo);
        var antes = new Dictionary<string, string>(falso.Fondos);

        // Empezar (1) y Aplicar (2) leen los monitores; la limpieza de la caché, tras ponerlos, es la tercera.
        var servicio = new ServicioQueFallaAl(falso, 3);
        var prueba = new PruebaFondos(servicio, new AplicadorFondos(servicio, new RenderizadorSkia(), Path.Combine(_img.Carpeta, "datos")));
        var r = prueba.Empezar(Comb(SKColors.Blue), Usuario, lienzo);

        Assert.True(r.Exito);
        Assert.NotNull(prueba.Anterior);
        prueba.Volver();
        Assert.Equal(antes, falso.Fondos);
    }
}
