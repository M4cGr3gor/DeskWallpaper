namespace DeskWallpaper.Nucleo;

/// <summary>Zonas elegidas por el usuario: unir y separar monitores contiguos.</summary>
public static class EditorZonas
{
    /// <summary>
    /// Zonas guardadas para esta disposición si son válidas (cada monitor exactamente una vez y todos
    /// presentes); si no, las zonas por defecto.
    /// </summary>
    public static IReadOnlyList<Zona> ParaDisposicion(Disposicion d, IReadOnlyList<IReadOnlyList<string>>? guardadas)
    {
        if (guardadas is null || guardadas.Count == 0) return ZonasPorDefecto.Calcular(d);

        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var grupos = new List<List<DatosMonitor>>();
        foreach (var grupo in guardadas)
        {
            var monitores = new List<DatosMonitor>();
            foreach (var id in grupo ?? [])
            {
                if (d.Buscar(id) is not { } m || !vistos.Add(m.Id)) return ZonasPorDefecto.Calcular(d);
                monitores.Add(m);
            }
            if (monitores.Count == 0) continue;
            if (!Conexos(monitores)) return ZonasPorDefecto.Calcular(d); // una zona debe ser de monitores que se tocan
            grupos.Add(monitores);
        }
        if (vistos.Count != d.Monitores.Count) return ZonasPorDefecto.Calcular(d);
        return Ordenar(grupos.Select(Crear), d);
    }

    /// <summary>Une las zonas de dos monitores contiguos. Null si no se tocan o ya están en la misma zona.</summary>
    public static IReadOnlyList<Zona>? Unir(Disposicion d, IReadOnlyList<Zona> zonas, string monitorA, string monitorB)
    {
        if (d.Buscar(monitorA) is not { } a || d.Buscar(monitorB) is not { } b || !SonContiguos(a, b)) return null;
        var za = ZonaDe(zonas, a.Id);
        var zb = ZonaDe(zonas, b.Id);
        if (za is null || zb is null || za == zb) return null;

        var unida = Crear(za.MonitorIds.Concat(zb.MonitorIds).Select(id => d.Buscar(id)!));
        return Ordenar(zonas.Where(z => z != za && z != zb).Append(unida), d);
    }

    /// <summary>Separa la zona en monitores sueltos. Null si ya es de un solo monitor o no existe.</summary>
    public static IReadOnlyList<Zona>? Separar(Disposicion d, IReadOnlyList<Zona> zonas, string zonaId)
    {
        var zona = zonas.FirstOrDefault(z => z.Id == zonaId);
        if (zona is null || zona.MonitorIds.Count < 2) return null;
        var sueltas = zona.MonitorIds.Select(id => Crear([d.Buscar(id)!]));
        return Ordenar(zonas.Where(z => z != zona).Concat(sueltas), d);
    }

    /// <summary>Pares de monitores contiguos y si están en la misma zona (para los botones de enlace).</summary>
    public static IReadOnlyList<(string A, string B, bool Unidos)> Contiguos(Disposicion d, IReadOnlyList<Zona> zonas)
    {
        var pares = new List<(string, string, bool)>();
        var ms = d.Monitores;
        for (var i = 0; i < ms.Count; i++)
        for (var j = i + 1; j < ms.Count; j++)
            if (SonContiguos(ms[i], ms[j]))
                pares.Add((ms[i].Id, ms[j].Id, ZonaDe(zonas, ms[i].Id) is { } z && z == ZonaDe(zonas, ms[j].Id)));
        return pares;
    }

    /// <summary>Bordes que se tocan y que se solapan en el otro eje (misma regla que <see cref="LienzoFisico"/>).</summary>
    public static bool SonContiguos(DatosMonitor a, DatosMonitor b)
    {
        var pa = a.Escritorio;
        var pb = b.Escritorio;
        var solapaVertical = pa.Y < pb.Abajo && pb.Y < pa.Abajo;
        var solapaHorizontal = pa.X < pb.Derecha && pb.X < pa.Derecha;
        return (solapaVertical && (pa.Derecha == pb.X || pb.Derecha == pa.X))
            || (solapaHorizontal && (pa.Abajo == pb.Y || pb.Abajo == pa.Y));
    }

    /// <summary>¿Se puede ir de cualquier monitor del grupo a cualquier otro pasando por monitores contiguos?</summary>
    private static bool Conexos(IReadOnlyList<DatosMonitor> grupo)
    {
        var alcanzados = new HashSet<DatosMonitor> { grupo[0] };
        var cola = new Queue<DatosMonitor>([grupo[0]]);
        while (cola.Count > 0)
        {
            var m = cola.Dequeue();
            foreach (var otro in grupo.Where(o => !alcanzados.Contains(o) && SonContiguos(m, o)))
            {
                alcanzados.Add(otro);
                cola.Enqueue(otro);
            }
        }
        return alcanzados.Count == grupo.Count;
    }

    private static Zona? ZonaDe(IReadOnlyList<Zona> zonas, string monitorId) =>
        zonas.FirstOrDefault(z => z.MonitorIds.Contains(monitorId, StringComparer.OrdinalIgnoreCase));

    // Orden de lectura: de arriba abajo y, en la misma altura, de izquierda a derecha.
    private static Zona Crear(IEnumerable<DatosMonitor> monitores)
    {
        var orden = monitores.OrderBy(m => m.Escritorio.Y).ThenBy(m => m.Escritorio.X).ToList();
        return new Zona(string.Join('+', orden.Select(m => m.Numero)), orden.Select(m => m.Id).ToList());
    }

    /// <summary>
    /// Marcos de los pares unidos agrupados por borde: si un monitor toca a varios por el mismo lado
    /// (el 3 bajo el 2 y el 1), es un solo grupo con un solo valor. Los demás pares van cada uno en su grupo.
    /// </summary>
    public static IReadOnlyList<GrupoMarco> GruposMarco(Disposicion d, IReadOnlyList<Zona> zonas)
    {
        // Se agrupan todos los pares contiguos y luego se muestran los grupos con algún par unido: así, con el 3
        // unido solo al 2, la fila lleva también la clave 1–3, que es la que usa la geometría para colocar el 3.
        var contiguos = Contiguos(d, zonas);
        var unidos = contiguos.Where(p => p.Unidos).Select(p => LienzoFisico.ClaveMarco(p.A, p.B)).ToHashSet(StringComparer.Ordinal);
        var pares = contiguos.Select(p => (A: d.Buscar(p.A)!, B: d.Buscar(p.B)!)).ToList();

        // Para cada par, el lado por el que se toca cada monitor.
        var porLado = new Dictionary<(string Monitor, Lado Lado), List<DatosMonitor>>();
        void Anadir(DatosMonitor m, Lado lado, DatosMonitor otro)
        {
            if (!porLado.TryGetValue((m.Id, lado), out var lista)) porLado[(m.Id, lado)] = lista = [];
            lista.Add(otro);
        }
        foreach (var (a, b) in pares)
        {
            var (ladoA, ladoB) = Lados(a.Escritorio, b.Escritorio);
            Anadir(a, ladoA, b);
            Anadir(b, ladoB, a);
        }

        var grupos = new List<GrupoMarco>();
        var usados = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ((id, lado), otros) in porLado.Where(g => g.Value.Count >= 2).OrderByDescending(g => g.Value.Count))
        {
            var libres = otros.Where(o => !usados.Contains(LienzoFisico.ClaveMarco(id, o.Id))).ToList();
            if (libres.Count < 2) continue;
            var horizontal = lado is Lado.Arriba or Lado.Abajo;
            var ordenados = libres.OrderBy(o => horizontal ? o.Escritorio.X : o.Escritorio.Y).ToList();
            grupos.Add(new GrupoMarco(id, ordenados.Select(o => o.Id).ToList()));
            foreach (var o in ordenados) usados.Add(LienzoFisico.ClaveMarco(id, o.Id));
        }
        foreach (var (a, b) in pares.Where(p => !usados.Contains(LienzoFisico.ClaveMarco(p.A.Id, p.B.Id))))
        {
            var (menor, mayor) = a.Numero <= b.Numero ? (a, b) : (b, a);
            grupos.Add(new GrupoMarco(menor.Id, [mayor.Id]));
        }
        return grupos.Where(g => g.Claves.Any(unidos.Contains)).ToList();
    }

    /// <summary>
    /// Pone <paramref name="valor"/> en todas las claves del grupo, quitando antes las equivalentes guardadas con
    /// otro orden u otras mayúsculas. Con 0 las quita todas (sin marco).
    /// </summary>
    public static void AplicarValorGrupo(IDictionary<string, double> marcos, IEnumerable<string> claves, double valor)
    {
        static string Normal(string k) => k.Split('|') is [var x, var y] ? LienzoFisico.ClaveMarco(x, y) : k.ToUpperInvariant();
        foreach (var clave in claves)
        {
            var normal = Normal(clave);
            foreach (var vieja in marcos.Keys.Where(k => Normal(k) == normal).ToList()) marcos.Remove(vieja);
            if (valor > 0) marcos[normal] = valor;
        }
    }

    /// <summary>El mayor marco guardado en las claves del grupo (0 si no hay ninguno).</summary>
    public static double ValorDeGrupo(GrupoMarco g, IReadOnlyDictionary<string, double> marcosMm)
    {
        var porClave = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (clave, valor) in marcosMm)
        {
            var partes = clave.Split('|');
            porClave[partes.Length == 2 ? LienzoFisico.ClaveMarco(partes[0], partes[1]) : clave] = valor;
        }
        return g.Claves.Select(c => porClave.GetValueOrDefault(c)).DefaultIfEmpty(0).Max();
    }

    private enum Lado { Izquierda, Derecha, Arriba, Abajo }

    /// <summary>Lado por el que se tocan dos monitores contiguos (misma regla que <see cref="SonContiguos"/>).</summary>
    private static (Lado A, Lado B) Lados(RectPx a, RectPx b) =>
        a.Derecha == b.X ? (Lado.Derecha, Lado.Izquierda)
        : b.Derecha == a.X ? (Lado.Izquierda, Lado.Derecha)
        : a.Abajo == b.Y ? (Lado.Abajo, Lado.Arriba)
        : (Lado.Arriba, Lado.Abajo);


    private static IReadOnlyList<Zona> Ordenar(IEnumerable<Zona> zonas, Disposicion d) =>
        zonas.OrderBy(z => Primero(z, d).Y).ThenBy(z => Primero(z, d).X).ToList();

    private static RectPx Primero(Zona z, Disposicion d) => d.Buscar(z.MonitorIds[0])?.Escritorio ?? default;
}

/// <summary>
/// Un borde compartido: <see cref="Monitor"/> toca a <see cref="Otros"/> por el mismo lado. Otros va de izquierda a
/// derecha (borde horizontal) o de arriba abajo (vertical). Un par suelto tiene un solo «otro».
/// </summary>
public sealed record GrupoMarco(string Monitor, IReadOnlyList<string> Otros)
{
    /// <summary>Claves de <see cref="Configuracion.MarcosMm"/> de cada par del grupo, en el orden de <see cref="Otros"/>.</summary>
    public IReadOnlyList<string> Claves { get; } = Otros.Select(o => LienzoFisico.ClaveMarco(Monitor, o)).ToList();

}
