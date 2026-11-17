namespace DeskWallpaper.Nucleo;

/// <summary>
/// Colocación de los monitores en el mundo real, en milímetros. Se deduce de la disposición
/// de Windows (píxeles) y del tamaño físico de cada monitor, añadiendo el hueco de los marcos.
/// </summary>
public sealed class LienzoFisico
{
    private readonly Dictionary<string, RectMm> _rects;

    private LienzoFisico(Dictionary<string, RectMm> rects) => _rects = rects;

    /// <summary>Clave del diccionario de marcos para un par de monitores (independiente del orden).</summary>
    public static string ClaveMarco(string idA, string idB)
    {
        var ids = new[] { idA.ToUpperInvariant(), idB.ToUpperInvariant() };
        Array.Sort(ids, StringComparer.Ordinal);
        return $"{ids[0]}|{ids[1]}";
    }

    public static LienzoFisico DesdeDisposicion(Disposicion d, IReadOnlyDictionary<string, double> marcosMm)
    {
        var monitores = d.Monitores;
        var rects = new Dictionary<string, RectMm>(StringComparer.OrdinalIgnoreCase);
        if (monitores.Count == 0) return new LienzoFisico(rects);

        // Normalizamos las claves de los marcos por si vienen en otro orden o con otras mayúsculas.
        var marcos = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (clave, valor) in marcosMm)
        {
            var partes = clave.Split('|');
            marcos[partes.Length == 2 ? ClaveMarco(partes[0], partes[1]) : clave] = valor;
        }
        double Marco(DatosMonitor a, DatosMonitor b) => marcos.GetValueOrDefault(ClaveMarco(a.Id, b.Id));

        var raiz = monitores.FirstOrDefault(m => m.Principal)
                   ?? monitores.FirstOrDefault(m => m.Escritorio is { X: <= 0, Y: <= 0 } e && e.Derecha > 0 && e.Abajo > 0)
                   ?? monitores[0];

        rects[raiz.Id] = new RectMm(0, 0, raiz.TamanoFisico.Ancho, raiz.TamanoFisico.Alto);
        var cola = new Queue<DatosMonitor>([raiz]);
        while (cola.Count > 0)
        {
            var a = cola.Dequeue();
            var ra = rects[a.Id];
            foreach (var b in monitores)
            {
                if (rects.ContainsKey(b.Id)) continue;
                var pa = a.Escritorio;
                var pb = b.Escritorio;
                var solapaVertical = pa.Y < pb.Abajo && pb.Y < pa.Abajo;
                var solapaHorizontal = pa.X < pb.Derecha && pb.X < pa.Derecha;
                double? x = null, y = null;

                // A lo largo del borde compartido se alinean los CENTROS (desplazamiento entre centros en px,
                // pasado a mm con la densidad del monitor ya colocado). Alinear los bordes desplazaría a un
                // monitor físicamente más pequeño: el usuario lo ve centrado en Windows y debe verlo centrado aquí.
                var centroY = ra.Y + ra.Alto / 2 + ((pb.Y + pb.Alto / 2.0) - (pa.Y + pa.Alto / 2.0)) * a.MmPorPxY - b.TamanoFisico.Alto / 2;
                var centroX = ra.X + ra.Ancho / 2 + ((pb.X + pb.Ancho / 2.0) - (pa.X + pa.Ancho / 2.0)) * a.MmPorPxX - b.TamanoFisico.Ancho / 2;

                if (solapaVertical && pa.Derecha == pb.X)
                {
                    x = ra.Derecha + Marco(a, b);
                    y = centroY;
                }
                else if (solapaVertical && pb.Derecha == pa.X)
                {
                    x = ra.X - Marco(a, b) - b.TamanoFisico.Ancho;
                    y = centroY;
                }
                else if (solapaHorizontal && pa.Abajo == pb.Y)
                {
                    y = ra.Abajo + Marco(a, b);
                    x = centroX;
                }
                else if (solapaHorizontal && pb.Abajo == pa.Y)
                {
                    y = ra.Y - Marco(a, b) - b.TamanoFisico.Alto;
                    x = centroX;
                }

                if (x is null || y is null) continue;
                rects[b.Id] = new RectMm(x.Value, y.Value, b.TamanoFisico.Ancho, b.TamanoFisico.Alto);
                cola.Enqueue(b);
            }
        }

        // Monitores sin contacto con el resto: posición en px convertida con la densidad del principal.
        foreach (var m in monitores.Where(m => !rects.ContainsKey(m.Id)))
        {
            rects[m.Id] = new RectMm(
                (m.Escritorio.X - raiz.Escritorio.X) * raiz.MmPorPxX,
                (m.Escritorio.Y - raiz.Escritorio.Y) * raiz.MmPorPxY,
                m.TamanoFisico.Ancho, m.TamanoFisico.Alto);
        }

        var minX = rects.Values.Min(r => r.X);
        var minY = rects.Values.Min(r => r.Y);
        foreach (var id in rects.Keys.ToList())
            rects[id] = rects[id] with { X = rects[id].X - minX, Y = rects[id].Y - minY };

        return new LienzoFisico(rects);
    }

    /// <summary>Rectángulo físico de un monitor.</summary>
    public RectMm Monitor(string monitorId) =>
        _rects.TryGetValue(monitorId, out var r) ? r : throw new KeyNotFoundException($"Monitor desconocido: {monitorId}");

    /// <summary>Rectángulo que envuelve todos los monitores de la zona.</summary>
    public RectMm Envolvente(Zona zona)
    {
        var rs = zona.MonitorIds.Select(Monitor).ToList();
        var x = rs.Min(r => r.X);
        var y = rs.Min(r => r.Y);
        return new RectMm(x, y, rs.Max(r => r.Derecha) - x, rs.Max(r => r.Abajo) - y);
    }
}
