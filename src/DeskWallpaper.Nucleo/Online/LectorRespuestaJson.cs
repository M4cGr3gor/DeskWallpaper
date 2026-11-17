using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeskWallpaper.Nucleo;

/// <summary>Un archivo de un vídeo (las APIs de vídeo dan varios tamaños del mismo).</summary>
public sealed record ArchivoVideo(Uri Url, SizePx Tamano);

/// <summary>
/// Un resultado de una fuente con API. En los vídeos, <see cref="Imagen"/> y <see cref="Tamano"/> son los del archivo
/// elegido (al leer, el mayor; al buscar, el que mejor cubre la casilla) y <see cref="Archivos"/>, todos.
/// </summary>
public sealed record ResultadoOnline(string Fuente, Uri Miniatura, Uri Imagen, SizePx Tamano, Uri? Ficha,
                                     TimeSpan? Duracion = null, IReadOnlyList<ArchivoVideo>? Archivos = null);

/// <summary>Una página de resultados y, si la fuente lo dice, cuál es la última.</summary>
public sealed record PaginaResultados(IReadOnlyList<ResultadoOnline> Resultados, int? UltimaPagina);

/// <summary>Lee la respuesta JSON de una fuente con API usando las rutas con puntos de su configuración.</summary>
public static class LectorRespuestaJson
{
    /// <exception cref="FormatException">
    /// "Formato de respuesta no reconocido": no es JSON, no está la lista, o hay elementos pero no se puede leer
    /// ninguno (la API ha cambiado los nombres de los campos o una ruta está mal escrita).
    /// </exception>
    public static PaginaResultados Leer(string json, FuenteBusqueda f)
    {
        try
        {
            return LeerSinProteger(json, f);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // JSON con claves repetidas o valores de un tipo inesperado.
            throw new FormatException("Formato de respuesta no reconocido", ex);
        }
    }

    private static PaginaResultados LeerSinProteger(string json, FuenteBusqueda f)
    {
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FormatException("Formato de respuesta no reconocido", ex); }

        if (Buscar(raiz, f.RutaResultados) is not JsonArray lista)
            throw new FormatException("Formato de respuesta no reconocido");

        var resultados = new List<ResultadoOnline>();
        var conArchivos = !string.IsNullOrWhiteSpace(f.RutaArchivos);
        foreach (var e in lista)
        {
            // Un elemento al que le falta algo imprescindible se salta: no estropea el resto de la página.
            if (Url(Buscar(e, f.RutaMiniatura)) is not { } miniatura) continue;
            var ficha = Url(Buscar(e, f.RutaFicha));
            var duracion = Segundos(Buscar(e, f.RutaDuracion));
            if (conArchivos)
            {
                var archivos = Archivos(Buscar(e, f.RutaArchivos), f);
                if (archivos.Count == 0) continue;
                var mayor = archivos.MaxBy(a => (long)a.Tamano.Ancho * a.Tamano.Alto)!;
                resultados.Add(new ResultadoOnline(f.Nombre, miniatura, mayor.Url, mayor.Tamano, ficha, duracion, archivos));
                continue;
            }
            if (Url(Buscar(e, f.RutaImagen)) is not { } imagen
                || Entero(Buscar(e, f.RutaAncho)) is not { } ancho || Entero(Buscar(e, f.RutaAlto)) is not { } alto)
                continue;
            resultados.Add(new ResultadoOnline(f.Nombre, miniatura, imagen, new SizePx(ancho, alto), ficha, duracion));
        }
        if (lista.Count > 0 && resultados.Count == 0) throw new FormatException("Formato de respuesta no reconocido");
        return new PaginaResultados(resultados, Entero(Buscar(raiz, f.RutaUltimaPagina)));
    }

    /// <summary>Los archivos de un vídeo: una lista, o un objeto cuyos valores son los archivos. Sin los que no tienen tamaño.</summary>
    private static List<ArchivoVideo> Archivos(JsonNode? nodo, FuenteBusqueda f)
    {
        IEnumerable<JsonNode?> elementos = nodo switch
        {
            JsonArray a => a,
            JsonObject o => o.Select(p => p.Value),
            _ => [],
        };
        var archivos = new List<ArchivoVideo>();
        foreach (var a in elementos)
        {
            if (Url(Buscar(a, f.RutaArchivoUrl)) is { } url
                && Entero(Buscar(a, f.RutaArchivoAncho)) is > 0 and var ancho && Entero(Buscar(a, f.RutaArchivoAlto)) is > 0 and var alto)
                archivos.Add(new ArchivoVideo(url, new SizePx(ancho, alto)));
        }
        return archivos;
    }

    private static TimeSpan? Segundos(JsonNode? nodo) =>
        nodo is JsonValue v && v.TryGetValue<double>(out var s) && s > 0 && s < 86400 ? TimeSpan.FromSeconds(s) : null;

    /// <summary>Sigue una ruta "a.b.c" por objetos; null si falta algún tramo o la ruta está vacía.</summary>
    private static JsonNode? Buscar(JsonNode? nodo, string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return null;
        foreach (var tramo in ruta.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (nodo is not JsonObject o || !o.TryGetPropertyValue(tramo, out nodo)) return null;
        }
        return nodo;
    }

    private static Uri? Url(JsonNode? nodo) =>
        nodo is JsonValue v && v.TryGetValue<string>(out var s)
        && Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
            ? u : null;

    private static int? Entero(JsonNode? nodo)
    {
        if (nodo is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var n)) return n;
        if (v.TryGetValue<long>(out var l) && l is > 0 and <= int.MaxValue) return (int)l;
        if (v.TryGetValue<double>(out var d) && d is > 0 and <= int.MaxValue) return (int)d;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : null;
    }
}
