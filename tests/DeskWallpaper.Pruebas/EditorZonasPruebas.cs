using DeskWallpaper.Nucleo;
using static DeskWallpaper.Pruebas.Ejemplos;

namespace DeskWallpaper.Pruebas;

public class EditorZonasPruebas
{
    private static readonly IReadOnlyList<Zona> PorDefecto = ZonasPorDefecto.Calcular(Usuario);

    [Fact]
    public void Unir_2y1con3_UnaZonaDeTres()
    {
        var z = EditorZonas.Unir(Usuario, PorDefecto, M1.Id, M3.Id);
        var unica = Assert.Single(z!);
        Assert.Equal("2+1+3", unica.Id);
        Assert.Equal([M2.Id, M1.Id, M3.Id], unica.MonitorIds);
    }

    [Fact]
    public void Unir_NoContiguos_Null()
    {
        var m3Lejos = M3 with { Escritorio = new RectPx(1000, 1080, 1920, 1200) }; // toca a M1 solo en una esquina
        var d = new Disposicion([M1, M2, m3Lejos]);
        Assert.Null(EditorZonas.Unir(d, ZonasPorDefecto.Calcular(d), M2.Id, m3Lejos.Id));
    }

    [Fact]
    public void Unir_YaJuntos_Null()
    {
        Assert.Null(EditorZonas.Unir(Usuario, PorDefecto, M2.Id, M1.Id));
    }

    [Fact]
    public void Separar_2mas1_DosZonas()
    {
        var z = EditorZonas.Separar(Usuario, PorDefecto, "2+1");
        Assert.Equal(["2", "1", "3"], z!.Select(x => x.Id));
    }

    [Fact]
    public void Separar_ZonaDeUno_Null()
    {
        Assert.Null(EditorZonas.Separar(Usuario, PorDefecto, "3"));
    }

    [Fact]
    public void ParaDisposicion_GuardadasInvalidas_UsaPorDefecto()
    {
        var faltaUno = EditorZonas.ParaDisposicion(Usuario, [[M2.Id, M1.Id]]);
        Assert.Equal(PorDefecto, faltaUno);
        var desconocido = EditorZonas.ParaDisposicion(Usuario, [[M2.Id, M1.Id], [M3.Id], ["OTRO"]]);
        Assert.Equal(PorDefecto, desconocido);
        var repetido = EditorZonas.ParaDisposicion(Usuario, [[M2.Id, M1.Id], [M1.Id, M3.Id]]);
        Assert.Equal(PorDefecto, repetido);
    }

    [Fact]
    public void ParaDisposicion_GuardadasValidas_SeRespetan()
    {
        var z = EditorZonas.ParaDisposicion(Usuario, [[M1.Id.ToLowerInvariant(), M3.Id, M2.Id]]);
        var unica = Assert.Single(z);
        Assert.Equal("2+1+3", unica.Id);
    }

    [Fact]
    public void Contiguos_Usuario()
    {
        var pares = EditorZonas.Contiguos(Usuario, PorDefecto);
        Assert.Equal(3, pares.Count);
        Assert.Contains(pares, p => Par(p, M2.Id, M1.Id) && p.Unidos);
        Assert.Contains(pares, p => Par(p, M2.Id, M3.Id) && !p.Unidos);
        Assert.Contains(pares, p => Par(p, M1.Id, M3.Id) && !p.Unidos);
    }

    // ── Marcos agrupados por borde ────────────────────────────────

    private static IReadOnlyList<Zona> Mural => EditorZonas.ParaDisposicion(Usuario, [[M2.Id, M1.Id, M3.Id]]);

    [Fact]
    public void GruposMarco_Mural_UnaFilaParaElTres()
    {
        var g = EditorZonas.GruposMarco(Usuario, Mural);

        Assert.Equal(2, g.Count);
        var delTres = Assert.Single(g, x => x.Otros.Count == 2);
        Assert.Equal(M3.Id, delTres.Monitor);
        Assert.Equal([M2.Id, M1.Id], delTres.Otros);              // de izquierda a derecha
        Assert.Equal([LienzoFisico.ClaveMarco(M3.Id, M2.Id), LienzoFisico.ClaveMarco(M3.Id, M1.Id)], delTres.Claves);
        var arriba = Assert.Single(g, x => x.Otros.Count == 1);
        Assert.Equal([LienzoFisico.ClaveMarco(M1.Id, M2.Id)], arriba.Claves);
        Assert.Equal(M1.Id, arriba.Monitor);                      // el de menor número
    }

    [Fact]
    public void GruposMarco_PorDefecto_SoloUnoYDos()
    {
        var g = Assert.Single(EditorZonas.GruposMarco(Usuario, PorDefecto));

        Assert.Equal([LienzoFisico.ClaveMarco(M1.Id, M2.Id)], g.Claves);
    }

    [Fact]
    public void GruposMarco_TresUnidoSoloAlDos_FilaDelBordeEntero()
    {
        // Se llega con los enlaces: «Separar» y luego unir solo el 2 y el 3. La geometría coloca el 3 desde el 1
        // (clave 1–3), así que una fila solo con la clave 2–3 no haría nada: la fila es la del borde entero.
        var zonas = EditorZonas.ParaDisposicion(Usuario, [[M2.Id, M3.Id], [M1.Id]]);

        var g = Assert.Single(EditorZonas.GruposMarco(Usuario, zonas));

        Assert.Equal(M3.Id, g.Monitor);
        Assert.Equal([M2.Id, M1.Id], g.Otros);
        Assert.Contains(LienzoFisico.ClaveMarco(M1.Id, M3.Id), g.Claves);
    }

    [Fact]
    public void GruposMarco_BordesDistintos_NoSeAgrupan()
    {
        DatosMonitor M(string id, int n, int x, int y) => new(id, n, "X", new RectPx(x, y, 100, 100), 1, new SizeMm(50, 50), n == 1);
        var d = new Disposicion([M("a", 1, 0, 0), M("b", 2, 100, 0), M("c", 3, 0, 100)]);
        var zonas = EditorZonas.ParaDisposicion(d, [["a", "b", "c"]]);

        var g = EditorZonas.GruposMarco(d, zonas);

        Assert.Equal(2, g.Count);
        Assert.All(g, x => Assert.Single(x.Otros));
    }

    [Fact]
    public void AplicarValorGrupo_ReemplazaLasClavesEquivalentes()
    {
        var delTres = EditorZonas.GruposMarco(Usuario, Mural).Single(x => x.Otros.Count == 2);
        // Guardadas a mano con otro orden y otras mayúsculas: deben quedar sustituidas, no duplicadas.
        var marcos = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{M2.Id.ToLowerInvariant()}|{M3.Id}"] = 2,
            [$"{M3.Id}|{M1.Id}"] = 4,
            [LienzoFisico.ClaveMarco(M1.Id, M2.Id)] = 10,
        };

        EditorZonas.AplicarValorGrupo(marcos, delTres.Claves, 6);

        Assert.Equal(3, marcos.Count);
        Assert.Equal(6, marcos[LienzoFisico.ClaveMarco(M2.Id, M3.Id)]);
        Assert.Equal(6, marcos[LienzoFisico.ClaveMarco(M1.Id, M3.Id)]);
        Assert.Equal(10, marcos[LienzoFisico.ClaveMarco(M1.Id, M2.Id)]);
    }

    [Fact]
    public void AplicarValorGrupo_Cero_BorraElGrupo()
    {
        var delTres = EditorZonas.GruposMarco(Usuario, Mural).Single(x => x.Otros.Count == 2);
        var marcos = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            [LienzoFisico.ClaveMarco(M2.Id, M3.Id)] = 2,
            [LienzoFisico.ClaveMarco(M1.Id, M3.Id)] = 2,
            [LienzoFisico.ClaveMarco(M1.Id, M2.Id)] = 10,
        };

        EditorZonas.AplicarValorGrupo(marcos, delTres.Claves, 0);

        Assert.Equal([LienzoFisico.ClaveMarco(M1.Id, M2.Id)], marcos.Keys);
    }

    [Fact]
    public void ValorDeGrupo_ElMayor()
    {
        var delTres = EditorZonas.GruposMarco(Usuario, Mural).Single(x => x.Otros.Count == 2);
        var marcos = new Dictionary<string, double>
        {
            [LienzoFisico.ClaveMarco(M3.Id, M2.Id)] = 2,
            [LienzoFisico.ClaveMarco(M1.Id, M3.Id)] = 5,
        };

        Assert.Equal(5, EditorZonas.ValorDeGrupo(delTres, marcos));
        Assert.Equal(0, EditorZonas.ValorDeGrupo(delTres, new Dictionary<string, double>()));
    }

    private static bool Par((string A, string B, bool Unidos) p, string x, string y) =>
        (p.A == x && p.B == y) || (p.A == y && p.B == x);
}
