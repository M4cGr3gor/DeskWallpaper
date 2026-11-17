using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace DeskWallpaper.Nucleo;

/// <summary>Un monitor recibirá una imagen ampliada (Factor &gt; 1 significa que se amplía).</summary>
public sealed record AvisoCalidad(string MonitorId, double Factor);

/// <summary>Resultado de aplicar o previsualizar una combinación.</summary>
public sealed record ResultadoAplicacion(bool Exito, IReadOnlyList<string> Errores,
    IReadOnlyList<AvisoCalidad> Avisos, IReadOnlyDictionary<string, string> FondosPorMonitor);

/// <summary>
/// Genera un PNG por monitor, a su resolución nativa, y los aplica. Todo o nada: si una imagen
/// falla no se toca ningún monitor. Un monitor encendido que no está en ninguna zona se pone en negro
///; uno apagado conserva su fondo, porque Windows no deja cambiarlo.
/// </summary>
public sealed class AplicadorFondos(IServicioFondos servicio, IRenderizador renderizador, string carpetaDatos)
{
    /// <summary>A partir de cuánto se avisa de que una imagen se amplía.</summary>
    public const double UmbralAviso = 1.05;

    private string CarpetaCache => Path.Combine(carpetaDatos, "cache", "fondos");

    public ResultadoAplicacion Aplicar(Combinacion c, Disposicion d, LienzoFisico l,
                                       IReadOnlyCollection<string>? rutasProtegidas = null)
    {
        if (c.Zonas.Count == 0) return Fallo(["Elige al menos una imagen"]);

        // La disposición en memoria puede estar desfasada: se comprueba contra lo que Windows tiene activo.
        var activos = servicio.MonitorIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ausentes = c.Zonas.SelectMany(z => z.MonitorIds).Where(id => !activos.Contains(id)).ToList();
        if (ausentes.Count > 0) return Fallo(MensajesDesconectados(ausentes, d));

        var marca = $"{DateTime.Now:yyyyMMddHHmmssfff}{Guid.NewGuid().ToString("N")[..4]}";
        var resultado = Generar(c, d, l, escala: 1.0, CarpetaCache, id => $"fondo_{Hash8(id)}_{marca}.png");
        if (!resultado.Exito) return resultado;
        resultado = AnadirNegros(resultado, c, d, activos, id => Path.Combine(CarpetaCache, $"fondo_{Hash8(id)}_{marca}.png"));
        if (!resultado.Exito) return resultado;

        // Todo o nada también al ponerlos: si Windows falla a mitad, se vuelve a lo que había.
        var posicionAnterior = servicio.ObtenerPosicion();
        var anteriores = activos.ToDictionary(id => id, id => servicio.ObtenerFondo(id) ?? "", StringComparer.OrdinalIgnoreCase);
        try
        {
            servicio.PonerPosicion(PosicionFondo.Rellenar);
            foreach (var (id, ruta) in resultado.FondosPorMonitor)
                servicio.PonerFondo(id, ruta);
        }
        catch (Exception ex)
        {
            Restaurar(posicionAnterior, anteriores);
            foreach (var ruta in resultado.FondosPorMonitor.Values) BorrarSinFallar(ruta);
            return Fallo([$"No se han podido poner los fondos: {ex.Message}"]);
        }

        // Los fondos ya están puestos: un fallo al limpiar la caché no convierte el éxito en error.
        try { Limpiar(rutasProtegidas ?? []); }
        catch (Exception) { /* se limpiará la próxima vez */ }
        return resultado;
    }

    private void Restaurar(PosicionFondo posicion, IReadOnlyDictionary<string, string> fondos)
    {
        try { servicio.PonerPosicion(posicion); } catch (Exception) { /* se intenta el resto igualmente */ }
        if (fondos.Count > 0 && fondos.Values.All(f => f.Length == 0))
        {
            try { servicio.QuitarImagenes(); } catch (Exception) { /* era modo color sólido */ }
            return;
        }
        foreach (var (id, ruta) in fondos.Where(f => f.Value.Length > 0))
        {
            try { servicio.PonerFondo(id, ruta); } catch (Exception) { /* idem */ }
        }
    }

    private ResultadoAplicacion Generar(Combinacion c, Disposicion d, LienzoFisico l, double escala,
                                        string carpeta, Func<string, string> nombreArchivo)
    {
        var desconocidos = c.Zonas.SelectMany(z => z.MonitorIds).Where(id => d.Buscar(id) is null).ToList();
        if (desconocidos.Count > 0) return Fallo(MensajesDesconectados(desconocidos, d));

        var generados = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var avisos = new List<AvisoCalidad>();
        try
        {
            Directory.CreateDirectory(carpeta);
            foreach (var asignacion in c.Zonas)
            {
                var tamano = renderizador.LeerTamano(asignacion.RutaImagen);
                var recortes = new List<Recorte>();

                foreach (var p in PlanRecorte.Calcular(tamano, asignacion.MonitorIds, d, l, asignacion.Encuadre))
                {
                    if (p.Factor > UmbralAviso) avisos.Add(new AvisoCalidad(p.MonitorId, p.Factor));

                    var salida = new SizePx(Math.Max(1, (int)Math.Round(p.Nativa.Ancho * escala)),
                                            Math.Max(1, (int)Math.Round(p.Nativa.Alto * escala)));
                    var ruta = Path.Combine(carpeta, nombreArchivo(p.MonitorId));
                    recortes.Add(new Recorte(p.Region, salida, ruta));
                    generados[p.MonitorId] = ruta; // antes de renderizar: si falla a medias, también se borra
                }

                // Una sola decodificación por imagen, aunque la zona tenga varios monitores.
                renderizador.Renderizar(asignacion.RutaImagen, recortes);
            }
        }
        catch (ExcepcionImagen ex)
        {
            foreach (var ruta in generados.Values) BorrarSinFallar(ruta);
            return Fallo([ex.Message]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var ruta in generados.Values) BorrarSinFallar(ruta);
            return Fallo([$"No se han podido guardar los fondos generados: {ex.Message}"]);
        }

        return new ResultadoAplicacion(true, [], avisos, generados);
    }

    /// <summary>Un PNG negro, a su resolución nativa, para cada monitor encendido que no está en ninguna zona.</summary>
    private static ResultadoAplicacion AnadirNegros(ResultadoAplicacion r, Combinacion c, Disposicion d,
                                                    IReadOnlySet<string> activos, Func<string, string> ruta)
    {
        var cubiertos = c.Zonas.SelectMany(z => z.MonitorIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fondos = new Dictionary<string, string>(r.FondosPorMonitor, StringComparer.OrdinalIgnoreCase);
        var nuevos = new List<string>();
        try
        {
            foreach (var m in d.Monitores.Where(m => activos.Contains(m.Id) && !cubiertos.Contains(m.Id)))
            {
                var destino = ruta(m.Id);
                nuevos.Add(destino);
                using var negro = new SKBitmap(Math.Max(1, m.Escritorio.Ancho), Math.Max(1, m.Escritorio.Alto));
                negro.Erase(SKColors.Black);
                using var png = negro.Encode(SKEncodedImageFormat.Png, 100)
                                ?? throw new IOException("No se ha podido codificar el fondo negro");
                using (var archivo = File.Create(destino)) png.SaveTo(archivo);
                fondos[m.Id] = destino;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var f in r.FondosPorMonitor.Values.Concat(nuevos)) BorrarSinFallar(f);
            return Fallo([$"No se han podido guardar los fondos generados: {ex.Message}"]);
        }
        return r with { FondosPorMonitor = fondos };
    }

    private static List<string> MensajesDesconectados(IEnumerable<string> ids, Disposicion d) =>
        ids.Select(id => d.Buscar(id)?.Nombre is { } nombre
                ? $"El monitor «{nombre}» ya no está conectado"
                : "Uno de los monitores elegidos ya no está conectado. Vuelve a elegir la imagen de su zona.")
           .Distinct()
           .ToList();

    /// <summary>
    /// Borra los fondos de la caché que ningún monitor usa y que no están protegidos. Siempre conserva
    /// el más reciente de cada monitor: un monitor apagado no aparece como activo, pero Windows
    /// volverá a leer su fondo al encenderlo.
    /// </summary>
    private void Limpiar(IReadOnlyCollection<string> protegidas)
    {
        if (!Directory.Exists(CarpetaCache)) return;
        var archivos = Directory.EnumerateFiles(CarpetaCache, "fondo_*.png").Select(Path.GetFullPath).ToList();

        var ultimos = archivos
            .GroupBy(a => Path.GetFileName(a)[..Math.Min(15, Path.GetFileName(a).Length)], StringComparer.OrdinalIgnoreCase) // "fondo_" + hash8 + "_"
            .Select(g => g.OrderBy(Path.GetFileName, StringComparer.Ordinal).Last());

        var conservar = servicio.MonitorIds()
            .Select(servicio.ObtenerFondo)
            .Concat(protegidas)
            .Where(r => !string.IsNullOrEmpty(r))
            .Select(r => Path.GetFullPath(r!))
            .Concat(ultimos)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var archivo in archivos.Where(a => !conservar.Contains(a)))
            BorrarSinFallar(archivo);
    }

    private static ResultadoAplicacion Fallo(IReadOnlyList<string> errores) =>
        new(false, errores, [], new Dictionary<string, string>());

    private static void BorrarSinFallar(string ruta)
    {
        try { File.Delete(ruta); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string Hash8(string texto) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToUpperInvariant())))[..8];
}
