// Herramienta de consola para diagnóstico y verificación (dwcli).
using System.Text.Encodings.Web;
using System.Text.Json;
using DeskWallpaper.Nucleo;
using DeskWallpaper.Windows;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var json = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

if (args.Length == 0)
{
    Console.Error.WriteLine("Uso: dwcli monitores");
    return 2;
}

switch (args[0].ToLowerInvariant())
{
    case "monitores":
    {
        var d = DetectorMonitores.Detectar();
        var lienzo = LienzoFisico.DesdeDisposicion(d, new Dictionary<string, double>());
        var zonas = ZonasPorDefecto.Calcular(d);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Firma = FirmaDisposicion.Calcular(d),
            d.Monitores,
            Zonas = zonas,
            Fisico = d.Monitores.ToDictionary(m => m.Id, m => lienzo.Monitor(m.Id)),
        }, json));
        return 0;
    }
    case "guardar-fondos" when args.Length == 2:
    {
        using var servicio = new ServicioFondosWindows();
        var copia = new CopiaFondos(servicio.ObtenerPosicion(),
            servicio.MonitorIds().ToDictionary(id => id, id => servicio.ObtenerFondo(id) ?? ""),
            servicio.ObtenerColorFondo());
        File.WriteAllText(args[1], JsonSerializer.Serialize(copia, json));
        Console.WriteLine($"Guardados {copia.Fondos.Count} fondos (posición {copia.Posicion}) en {args[1]}");
        foreach (var (id, ruta) in copia.Fondos) Console.WriteLine($"  {id}\n    → {ruta}");
        // Windows sigue mostrando un fondo aunque su archivo ya no exista, pero entonces no se puede volver a poner:
        // una prueba que cambie ese monitor no podría restaurarlo.
        var perdidos = copia.Fondos.Where(f => f.Value.Length > 0 && !File.Exists(f.Value)).ToList();
        foreach (var (id, ruta) in perdidos)
            Console.WriteLine($"ATENCIÓN: el fondo de {id} ya no existe ({ruta}): no se podrá restaurar");
        return perdidos.Count == 0 ? 0 : 3;
    }
    case "restaurar-fondos" when args.Length == 2:
    {
        var copia = JsonSerializer.Deserialize<CopiaFondos>(File.ReadAllText(args[1]), json)
                    ?? throw new InvalidOperationException("Copia vacía");
        // Todo o nada: si falta algún archivo no se cambia ningún monitor.
        var faltan = copia.Fondos.Where(f => f.Value.Length > 0 && !File.Exists(f.Value)).ToList();
        if (faltan.Count > 0)
        {
            foreach (var (id, ruta) in faltan) Console.WriteLine($"No se puede restaurar {id}: no existe {ruta}");
            return 2;
        }
        using var servicio = new ServicioFondosWindows();
        if (copia.Color is { } color) servicio.PonerColorFondo(color);
        servicio.PonerPosicion(copia.Posicion);
        if (copia.Fondos.Count > 0 && copia.Fondos.Values.All(f => f.Length == 0))
            servicio.QuitarImagenes(); // modo color sólido
        else
            foreach (var (id, ruta) in copia.Fondos.Where(f => f.Value.Length > 0))
                servicio.PonerFondo(id, ruta);

        var diferencias = new List<string>();
        if (servicio.ObtenerPosicion() != copia.Posicion)
            diferencias.Add($"Posición: {servicio.ObtenerPosicion()} en vez de {copia.Posicion}");
        if (copia.Color is { } c && servicio.ObtenerColorFondo() != c)
            diferencias.Add($"Color: {servicio.ObtenerColorFondo():X6} en vez de {c:X6}");
        foreach (var (id, ruta) in copia.Fondos)
        {
            var actual = servicio.ObtenerFondo(id) ?? "";
            if (!string.Equals(actual, ruta, StringComparison.OrdinalIgnoreCase))
                diferencias.Add($"{id}: {actual} en vez de {ruta}");
        }

        if (diferencias.Count == 0)
        {
            Console.WriteLine("OK");
            return 0;
        }
        diferencias.ForEach(Console.WriteLine);
        return 1;
    }
    case "aplicar":
    {
        // dwcli aplicar --data=<dir> --imagen 2+1="ruta" --imagen 3="ruta" [--marco-mm 20]
        var carpetaDatos = CarpetaDatos.Resolver(args, () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var cfg = AlmacenConfiguracion.Cargar(carpetaDatos);
        var d = DetectorMonitores.Detectar().ConCorrecciones(cfg.TamanosCorregidos);
        var zonas = ZonasPorDefecto.Calcular(d);
        var marcos = MarcosConParametro(cfg.MarcosMm, zonas, args);
        var lienzo = LienzoFisico.DesdeDisposicion(d, marcos);

        var asignaciones = new List<AsignacionZona>();
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] != "--imagen") continue;
            var partes = args[i + 1].Split('=', 2);
            var zona = zonas.FirstOrDefault(z => z.Id == partes[0]);
            if (partes.Length != 2 || zona is null)
            {
                Console.Error.WriteLine($"Zona desconocida en «{args[i + 1]}». Zonas: {string.Join(", ", zonas.Select(z => z.Id))}");
                return 2;
            }
            asignaciones.Add(new AsignacionZona(zona.MonitorIds, partes[1].Trim('"'), new Encuadre()));
        }

        using var servicio = new ServicioFondosWindows();
        var r = new AplicadorFondos(servicio, new RenderizadorSkia(), carpetaDatos)
            .Aplicar(new Combinacion(FirmaDisposicion.Calcular(d), asignaciones), d, lienzo);
        foreach (var e in r.Errores) Console.WriteLine($"Error: {e}");
        foreach (var a in r.Avisos) Console.WriteLine($"Aviso: el monitor {d.Buscar(a.MonitorId)?.Numero} amplía la imagen un {(a.Factor - 1) * 100:0} %");
        foreach (var (id, ruta) in r.FondosPorMonitor) Console.WriteLine($"Monitor {d.Buscar(id)?.Numero}: {ruta}");
        if (r.Exito) cfg.UltimaFirma = FirmaDisposicion.Calcular(d);
        if (r.Exito) AlmacenConfiguracion.Guardar(carpetaDatos, cfg);
        return r.Exito ? 0 : 1;
    }
    case "vista-previa" when args.Length >= 2:
    {
        // Compone los fondos actuales sobre el lienzo físico (0,5 px/mm) para comprobar la alineación.
        var d = DetectorMonitores.Detectar();
        var marcos = MarcosConParametro(new Dictionary<string, double>(), ZonasPorDefecto.Calcular(d), args);
        var lienzo = LienzoFisico.DesdeDisposicion(d, marcos);
        const float pxPorMm = 0.5f;
        var total = d.Monitores.Select(m => lienzo.Monitor(m.Id)).ToList();
        var ancho = (int)Math.Ceiling(total.Max(r => r.Derecha) * pxPorMm) + 20;
        var alto = (int)Math.Ceiling(total.Max(r => r.Abajo) * pxPorMm) + 20;

        using var servicio = new ServicioFondosWindows();
        using var superficie = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(ancho, alto));
        superficie.Canvas.Clear(new SkiaSharp.SKColor(40, 40, 40));
        foreach (var m in d.Monitores)
        {
            var ruta = servicio.ObtenerFondo(m.Id);
            if (string.IsNullOrEmpty(ruta) || !File.Exists(ruta)) continue;
            using var bmp = SkiaSharp.SKBitmap.Decode(ruta);
            if (bmp is null) continue;
            // Igual que "Rellenar": recorte centrado con la proporción del monitor.
            var region = Recortador.RegionZona(new SizePx(bmp.Width, bmp.Height),
                new RectMm(0, 0, m.Escritorio.Ancho, m.Escritorio.Alto), new Encuadre());
            var mm = lienzo.Monitor(m.Id);
            using var img = SkiaSharp.SKImage.FromBitmap(bmp);
            superficie.Canvas.DrawImage(img,
                SkiaSharp.SKRect.Create((float)region.X, (float)region.Y, (float)region.Ancho, (float)region.Alto),
                SkiaSharp.SKRect.Create(10 + (float)mm.X * pxPorMm, 10 + (float)mm.Y * pxPorMm, (float)mm.Ancho * pxPorMm, (float)mm.Alto * pxPorMm),
                new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.CatmullRom), null);
        }
        using var instantanea = superficie.Snapshot();
        using var datos = instantanea.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using (var f = File.Create(args[1])) datos.SaveTo(f);
        Console.WriteLine($"Vista previa: {args[1]} ({ancho}×{alto})");
        return 0;
    }
    case "biblioteca":
    {
        // dwcli biblioteca --data=<dir> <carpeta> [<carpeta>…] — escanea (solo lee las imágenes) y resume.
        // --data es obligatorio: con la biblioteca real quitaría las rutas de las carpetas no indicadas.
        if (!args.Any(a => a.StartsWith("--data=", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Uso: dwcli biblioteca --data=<carpeta de prueba> <carpeta> [<carpeta>…]");
            return 2;
        }
        var carpetaDatos = CarpetaDatos.Resolver(args, () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var carpetas = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => new CarpetaVigilada(a, false)).ToList();
        var repo = new RepositorioBiblioteca(carpetaDatos);
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var p = new EscanerBiblioteca(repo, new RenderizadorSkia()).EscanearAsync(carpetas, null, CancellationToken.None).GetAwaiter().GetResult();
        var d = DetectorMonitores.Detectar();
        var zonas = ZonasPorDefecto.Calcular(d);
        var l = LienzoFisico.DesdeDisposicion(d, new Dictionary<string, double>());
        var lista = repo.Listar();
        Console.WriteLine($"Escaneadas {p.Hechas} ({p.Omitidas} omitidas) en {reloj.Elapsed.TotalSeconds:0.0} s; en la biblioteca: {lista.Count}");
        foreach (var g in lista.GroupBy(i => ClasificadorProporcion.Clasificar(i.Tamano, zonas, l)))
            Console.WriteLine($"  {g.Key}: {g.Count()}");
        foreach (var c in lista.SelectMany(i => i.Colores).GroupBy(c => c).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  color {c.Key}: {c.Count()}");
        return 0;
    }
    default:
        Console.Error.WriteLine($"Comando desconocido o argumentos incorrectos: {string.Join(' ', args)}");
        return 2;
}

// --marco-mm N: aplica N mm entre cada par de monitores contiguos de las zonas (sin guardarlo).
static Dictionary<string, double> MarcosConParametro(IReadOnlyDictionary<string, double> guardados, IReadOnlyList<Zona> zonas, string[] args)
{
    var marcos = new Dictionary<string, double>(guardados, StringComparer.OrdinalIgnoreCase);
    var i = Array.IndexOf(args, "--marco-mm");
    if (i >= 0 && i + 1 < args.Length && double.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var mm))
        foreach (var z in zonas)
            for (var k = 0; k + 1 < z.MonitorIds.Count; k++)
                marcos[LienzoFisico.ClaveMarco(z.MonitorIds[k], z.MonitorIds[k + 1])] = mm;
    return marcos;
}

/// <summary>Copia de los fondos de todos los monitores, para restaurarlos después de una prueba.</summary>
/// <remarks>Color es null en las copias guardadas antes de que existiera (no se toca).</remarks>
internal sealed record CopiaFondos(PosicionFondo Posicion, Dictionary<string, string> Fondos, uint? Color = null);
