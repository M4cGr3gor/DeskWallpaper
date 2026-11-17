namespace DeskWallpaper.Nucleo;

/// <summary>
/// Zonas que se proponen sin intervención del usuario: se unen los monitores que están
/// lado a lado con la misma Y y el mismo alto en píxeles (unión transitiva); el resto va solo.
/// </summary>
public static class ZonasPorDefecto
{
    public static IReadOnlyList<Zona> Calcular(Disposicion d)
    {
        var monitores = d.Monitores.ToList();
        var padre = Enumerable.Range(0, monitores.Count).ToArray();
        int Raiz(int i) => padre[i] == i ? i : padre[i] = Raiz(padre[i]);

        for (var i = 0; i < monitores.Count; i++)
        for (var j = 0; j < monitores.Count; j++)
        {
            var a = monitores[i].Escritorio;
            var b = monitores[j].Escritorio;
            if (i != j && a.Derecha == b.X && a.Y == b.Y && a.Alto == b.Alto)
                padre[Raiz(j)] = Raiz(i);
        }

        return monitores
            .Select((m, i) => (m, grupo: Raiz(i)))
            .GroupBy(t => t.grupo, t => t.m)
            .Select(g => g.OrderBy(m => m.Escritorio.X).ThenBy(m => m.Escritorio.Y).ToList())
            .OrderBy(g => g[0].Escritorio.Y).ThenBy(g => g[0].Escritorio.X)
            .Select(g => new Zona(string.Join('+', g.Select(m => m.Numero)), g.Select(m => m.Id).ToList()))
            .ToList();
    }
}
