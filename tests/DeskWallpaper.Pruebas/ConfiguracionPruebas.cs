using DeskWallpaper.Nucleo;

namespace DeskWallpaper.Pruebas;

public class ConfiguracionPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "dw-pruebas", $"Config ñ & {Guid.NewGuid():N}");

    public ConfiguracionPruebas() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    [Fact]
    public void SinArchivo_DevuelvePorDefecto()
    {
        var c = AlmacenConfiguracion.Cargar(_carpeta);
        Assert.Equal(
            [new CarpetaVigilada(@"D:\Imagenes\Dual monitor", false), new CarpetaVigilada(@"D:\Imagenes\1920x1200", false)],
            c.CarpetasVigiladas);
        Assert.Null(c.UltimaFirma);
        Assert.Null(c.LienzoPendiente);
    }

    [Fact]
    public void Configuracion_SinPestanas_SeLeeConValoresPorDefecto()
    {
        // Un configuracion.json de antes de los fondos animados: sin pestaña, sin biblioteca de vídeos ni reproducción.
        File.WriteAllText(Path.Combine(_carpeta, "configuracion.json"), """
            { "Version": 1, "CarpetasVigiladas": [], "Biblioteca": { "Seccion": "Panoramica", "TamanoMiniatura": 200 },
              "Reproduccion": null, "BibliotecaAnimados": null, "PestanaActiva": null }
            """);

        var c = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal("Imagenes", c.PestanaActiva);
        Assert.Equal(SeccionBiblioteca.Panoramica, c.Biblioteca.SeccionActual);
        Assert.Equal(new EstadoBiblioteca(), c.BibliotecaAnimados);
        Assert.Equal(new OpcionesReproduccion(true, 30), c.Reproduccion);
    }

    [Fact]
    public void Reproduccion_SinOpcionDeReducir_SeLeeActivada()
    {
        // Un configuracion.json de la fase B, antes de la opción «Reducir los vídeos más grandes que sus monitores».
        File.WriteAllText(Path.Combine(_carpeta, "configuracion.json"), """
            { "Version": 1, "CarpetasVigiladas": [], "Reproduccion": { "PausarMaximizadas": false, "TopeFps": 0 } }
            """);

        var c = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal(new OpcionesReproduccion(false, 0, true), c.Reproduccion);
    }

    [Fact]
    public void AnchosDePaneles_SeGuardanYSeAcotan()
    {
        // Los paneles izquierdo y derecho se pueden ensanchar; 0 = el de siempre.
        var c = Configuracion.PorDefecto();
        Assert.Equal((0d, 0d), (c.AnchoNavegacion, c.AnchoPanel));
        c.AnchoNavegacion = 260;
        c.AnchoPanel = 5000;   // un valor imposible (editado a mano o de otra pantalla)
        AlmacenConfiguracion.Guardar(_carpeta, c);

        var leida = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal(260, leida.AnchoNavegacion);
        Assert.Equal(Configuracion.AnchoPanelMaximo, leida.AnchoPanel);
    }

    [Fact]
    public void GuardarYCargar_IdaYVuelta()
    {
        var c = Configuracion.PorDefecto();
        c.CarpetasVigiladas.Add(new CarpetaVigilada(@"D:\Imagenes\Fantasía", true));
        c.MarcosMm["A|B"] = 18.5;
        c.TamanosCorregidos["MON3"] = new SizeMm(301, 188);
        c.UltimaFirma = "0123456789abcdef";
        c.Ventana = new EstadoVentana(10, 20, 1200, 800, false);
        c.LienzoPendiente = new Combinacion("firma",
            [new AsignacionZona(["A", "B"], @"D:\Imagenes\Dual monitor\Batman & Joker.jpg", new Encuadre(0.4, 0.5, 1.2))]);

        AlmacenConfiguracion.Guardar(_carpeta, c);
        var leida = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal(c.CarpetasVigiladas, leida.CarpetasVigiladas);
        Assert.Equal(18.5, leida.MarcosMm["A|B"]);
        Assert.Equal(new SizeMm(301, 188), leida.TamanosCorregidos["MON3"]);
        Assert.Equal("0123456789abcdef", leida.UltimaFirma);
        Assert.Equal(c.Ventana, leida.Ventana);
        Assert.NotNull(leida.LienzoPendiente);
        var zona = Assert.Single(leida.LienzoPendiente!.Zonas);
        Assert.Equal(["A", "B"], zona.MonitorIds);
        Assert.Equal(@"D:\Imagenes\Dual monitor\Batman & Joker.jpg", zona.RutaImagen);
        Assert.Equal(new Encuadre(0.4, 0.5, 1.2), zona.Encuadre);
    }

    [Fact]
    public void EstadoBiblioteca_IdaYVuelta()
    {
        var c = Configuracion.PorDefecto();
        Assert.Equal(new EstadoBiblioteca(), c.Biblioteca);
        c.Biblioteca = new EstadoBiblioteca("Favoritas", "Noche", ColorNombre.Azul, "amanecer", 200);
        AlmacenConfiguracion.Guardar(_carpeta, c);
        Assert.Equal(c.Biblioteca, AlmacenConfiguracion.Cargar(_carpeta).Biblioteca);
        Assert.Contains("\"Azul\"", File.ReadAllText(Path.Combine(_carpeta, "configuracion.json")));
    }

    [Fact]
    public void ArchivoDeLaFase2_SinBiblioteca_TomaValoresPorDefecto()
    {
        File.WriteAllText(Path.Combine(_carpeta, "configuracion.json"),
            """{ "Version": 1, "CarpetasVigiladas": [ { "Ruta": "D:\\Imagenes\\Dual monitor", "IncluirSubcarpetas": false } ] }""");

        var c = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal(new EstadoBiblioteca(), c.Biblioteca);
        Assert.Equal(200, c.TopeMiniaturasMb);
        Assert.Single(c.CarpetasVigiladas);
    }

    [Fact]
    public void ArchivoAntiguo_FuentesPredeterminadas()
    {
        File.WriteAllText(Path.Combine(_carpeta, "configuracion.json"), """{ "Version": 1 }""");

        var c = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal(["Wallhaven", "Google Imágenes"], c.FuentesBusqueda!.Where(f => f.Contenido == ContenidoFuente.Imagenes).Select(f => f.Nombre));
        Assert.Equal(4, c.FuentesBusqueda!.Count(f => f.Contenido == ContenidoFuente.Videos));
        Assert.Equal(Configuracion.CarpetaDescargasPorDefecto, c.CarpetaDescargas);
        // Dentro de la carpeta Descargas real de Windows (aunque el usuario la haya movido de sitio).
        Assert.Equal("DeskWallpaper", Path.GetFileName(c.CarpetaDescargas));
        Assert.True(Directory.Exists(Path.GetDirectoryName(c.CarpetaDescargas)));
        Assert.Equal(Configuracion.CarpetaDescargasWindows(), Path.GetDirectoryName(c.CarpetaDescargas));
        Assert.Equal("", c.Busqueda.Texto);
    }

    [Fact]
    public void FuentesEditadas_IdaYVuelta()
    {
        var c = Configuracion.PorDefecto();
        var nueva = FuentesPredeterminadas.Todas()[0] with
        {
            Nombre = "Mi API",
            Activa = false,
            Cabeceras = new Dictionary<string, string> { ["X-API-Key"] = "clave" },
        };
        c.FuentesBusqueda = [.. FuentesPredeterminadas.Todas(), nueva];
        c.CarpetaDescargas = @"D:\Fondos nuevos";

        AlmacenConfiguracion.Guardar(_carpeta, c);
        var leida = AlmacenConfiguracion.Cargar(_carpeta);

        var f = leida.FuentesBusqueda!.Last();
        Assert.Equal(FuentesPredeterminadas.Todas().Count + 1, leida.FuentesBusqueda!.Count);
        Assert.Equal("Mi API", f.Nombre);
        Assert.Equal(TipoFuente.Api, f.Tipo);
        Assert.False(f.Activa);
        Assert.Equal("clave", f.Cabeceras["X-API-Key"]);
        Assert.Equal(nueva.PlantillaMayor, f.PlantillaMayor);
        Assert.Equal("meta.last_page", f.RutaUltimaPagina);
        Assert.Equal(@"D:\Fondos nuevos", leida.CarpetaDescargas);
        Assert.Contains("\"Api\"", File.ReadAllText(Path.Combine(_carpeta, "configuracion.json")));
    }

    [Fact]
    public void FuentesBorradas_ListaVaciaSeRespeta()
    {
        var c = Configuracion.PorDefecto();
        c.FuentesBusqueda = [];

        AlmacenConfiguracion.Guardar(_carpeta, c);

        Assert.Empty(AlmacenConfiguracion.Cargar(_carpeta).FuentesBusqueda!);
    }

    [Fact]
    public void FuenteSinCabeceras_SeNormaliza()
    {
        File.WriteAllText(Path.Combine(_carpeta, "configuracion.json"),
            """{ "Version": 1, "FuentesBusqueda": [ { "Nombre": "X", "Tipo": "Web", "Activa": true, "PlantillaExacta": "https://x.test/?q={texto}" } ] }""");

        var f = AlmacenConfiguracion.Cargar(_carpeta).FuentesBusqueda!.Single(f => f.Nombre == "X");

        Assert.NotNull(f.Cabeceras);
        Assert.Equal("", f.PlantillaMayor);
        Assert.Equal("", f.RutaResultados);
    }

    [Fact]
    public void EstadoBusqueda_IdaYVuelta()
    {
        var c = Configuracion.PorDefecto();
        c.Busqueda = new EstadoBusqueda("montañas nevadas", ["1920x1200"]);

        AlmacenConfiguracion.Guardar(_carpeta, c);
        var leida = AlmacenConfiguracion.Cargar(_carpeta).Busqueda;

        Assert.Equal("montañas nevadas", leida.Texto);
        Assert.Equal(["1920x1200"], leida.Desmarcadas!);
    }

    [Fact]
    public void SeccionActual_NoSeGuarda()
    {
        // Es un valor calculado a partir de "Seccion": no debe ensuciar el JSON.
        AlmacenConfiguracion.Guardar(_carpeta, Configuracion.PorDefecto());
        Assert.DoesNotContain("SeccionActual", File.ReadAllText(Path.Combine(_carpeta, "configuracion.json")));
    }

    [Fact]
    public void SeccionPorDefecto_Todas()
    {
        // "Todas" es la sección por defecto, también si la guardada no se entiende.
        Assert.Equal(SeccionBiblioteca.Todas, new EstadoBiblioteca().SeccionActual);
        Assert.Equal(SeccionBiblioteca.Todas, new EstadoBiblioteca("Inventada").SeccionActual);
    }

    [Fact]
    public void TamanoMiniatura_FueraDeRango_SeAjusta()
    {
        File.WriteAllText(Path.Combine(_carpeta, "configuracion.json"),
            """{ "Version": 1, "Biblioteca": { "Seccion": "Todas", "TamanoMiniatura": 0 }, "TopeMiniaturasMb": 99999 }""");

        var c = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal(120, c.Biblioteca.TamanoMiniatura);
        Assert.Equal(2000, c.TopeMiniaturasMb);
        Assert.Equal("Todas", c.Biblioteca.Seccion);
    }

    [Fact]
    public void ZonasPorFirma_IdaYVuelta()
    {
        var c = Configuracion.PorDefecto();
        c.ZonasPorFirma["firma1"] = [["A", "B", "C"]];
        AlmacenConfiguracion.Guardar(_carpeta, c);
        var leida = AlmacenConfiguracion.Cargar(_carpeta);
        Assert.Equal(["A", "B", "C"], leida.ZonasPorFirma["FIRMA1"][0]);
    }

    [Fact]
    public void Json_EsLegibleConAcentos()
    {
        var c = Configuracion.PorDefecto();
        c.CarpetasVigiladas.Add(new CarpetaVigilada(@"D:\Imagenes\Fantasía", true));
        AlmacenConfiguracion.Guardar(_carpeta, c);
        var texto = File.ReadAllText(Path.Combine(_carpeta, "configuracion.json"));
        Assert.Contains("Fantasía", texto);
        Assert.Contains("CarpetasVigiladas", texto);
    }

    [Fact]
    public void CarpetasDelUsuario_NoSeSobrescribenConLasPorDefecto()
    {
        var c = Configuracion.PorDefecto();
        c.CarpetasVigiladas.Clear();
        c.CarpetasVigiladas.Add(new CarpetaVigilada(@"E:\Fondos", false));
        AlmacenConfiguracion.Guardar(_carpeta, c);

        var leida = AlmacenConfiguracion.Cargar(_carpeta);
        Assert.Equal([new CarpetaVigilada(@"E:\Fondos", false)], leida.CarpetasVigiladas);
    }

    [Fact]
    public void ListaVaciaGuardada_SeRespeta()
    {
        var c = Configuracion.PorDefecto();
        c.CarpetasVigiladas.Clear();
        AlmacenConfiguracion.Guardar(_carpeta, c);
        Assert.Empty(AlmacenConfiguracion.Cargar(_carpeta).CarpetasVigiladas);
    }

    [Fact]
    public void JsonCorrupto_PorDefectoYCopiaBak()
    {
        var ruta = Path.Combine(_carpeta, "configuracion.json");
        File.WriteAllText(ruta, "{ esto no es json");

        var c = AlmacenConfiguracion.Cargar(_carpeta);

        Assert.Equal(2, c.CarpetasVigiladas.Count);
        var bak = Assert.Single(Directory.GetFiles(_carpeta, "configuracion.json.bak-*"));
        Assert.Equal("{ esto no es json", File.ReadAllText(bak));
    }

    [Fact]
    public void Guardar_NoDejaTemporales()
    {
        AlmacenConfiguracion.Guardar(_carpeta, Configuracion.PorDefecto());
        AlmacenConfiguracion.Guardar(_carpeta, Configuracion.PorDefecto());
        Assert.Equal(["configuracion.json"], Directory.GetFiles(_carpeta).Select(Path.GetFileName));
    }

    [Fact]
    public void Resolver_ConParametroData()
    {
        var destino = Path.Combine(_carpeta, "x y");
        var r = CarpetaDatos.Resolver(["--otra", $"--data={destino}"], () => throw new InvalidOperationException());
        Assert.Equal(destino, r);
        Assert.True(Directory.Exists(destino));
    }

    [Fact]
    public void Resolver_SinParametro()
    {
        var r = CarpetaDatos.Resolver([], () => _carpeta);
        Assert.Equal(Path.Combine(_carpeta, "DeskWallpaper"), r);
        Assert.True(Directory.Exists(r));
    }
}
