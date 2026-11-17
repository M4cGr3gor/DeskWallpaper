using System.Diagnostics;
using System.Net;

namespace DeskWallpaper.Nucleo;

/// <summary>Un resultado con cómo encaja en la resolución pedida (null si se buscó sin filtro de tamaño).</summary>
public sealed record ResultadoEncajado(ResultadoOnline Resultado, Encaje? Encaje, SizePx? Pedida);

/// <summary>Lo que ha devuelto una fuente en una página; si algo falló, <see cref="Error"/> lo dice en español.</summary>
public sealed record ResultadoFuente(string Fuente, IReadOnlyList<ResultadoEncajado> Resultados, bool HayMas, string? Error);

/// <summary>
/// Busca en las fuentes con API: como mucho una petición por segundo y por fuente,
/// 15 s por petición, un reintento tras un 429, y los fallos de una fuente nunca afectan a las demás.
/// </summary>
public sealed class BuscadorOnline
{
    public const string AgenteUsuario = "DeskWallpaper/1.0 (+uso personal)";
    public static readonly TimeSpan TiempoMaximo = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EsperaPor429 = TimeSpan.FromSeconds(5);
    /// <summary>Si el servicio pide esperar más que esto, se da el error en vez de dejar la búsqueda colgada.</summary>
    private static readonly TimeSpan EsperaMaxima429 = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _esperar;
    private readonly Stopwatch _reloj = Stopwatch.StartNew();
    private readonly Dictionary<string, TimeSpan> _siguientePeticion = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _turnos = new();

    /// <param name="esperar">Para las pruebas: sustituye a Task.Delay.</param>
    public BuscadorOnline(HttpMessageHandler manejador, Func<TimeSpan, CancellationToken, Task>? esperar = null)
    {
        _http = new HttpClient(manejador, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        _esperar = esperar ?? Task.Delay;
    }

    /// <summary>
    /// Una página de una fuente con API, por cada resolución pedida (o sin filtro si la lista está vacía).
    /// No lanza salvo por cancelación: los fallos van en <see cref="ResultadoFuente.Error"/>.
    /// </summary>
    public async Task<ResultadoFuente> BuscarAsync(FuenteBusqueda f, string texto, IReadOnlyList<SizePx> resoluciones, int pagina, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FaltaClave(f))
            return new ResultadoFuente(f.Nombre, [], false, $"{f.Nombre}: falta tu clave personal; escríbela en Ajustes › Búsqueda online");
        var resultados = new List<ResultadoEncajado>();
        var vistas = new HashSet<Uri>();
        // Si la dirección no depende de la resolución (Pexels), una sola petición sirve para todas las casillas.
        var leidas = new Dictionary<Uri, PaginaResultados>();
        var esVideo = f.Contenido == ContenidoFuente.Videos;
        var hayMas = false;
        IReadOnlyList<SizePx?> pedidas = resoluciones.Count == 0 ? [null] : [.. resoluciones.Select(r => (SizePx?)r)];

        foreach (var pedida in pedidas)
        {
            string plantilla;
            if (pedida is null)
                plantilla = string.IsNullOrWhiteSpace(f.PlantillaSinResolucion) ? PlantillaUrl.SinResolucion(f.PlantillaExacta) : f.PlantillaSinResolucion;
            else
                plantilla = string.IsNullOrWhiteSpace(f.PlantillaMayor) ? f.PlantillaExacta : f.PlantillaMayor;

            Uri url;
            try
            {
                url = PlantillaUrl.Rellenar(plantilla, texto, pedida, pagina, f.Clave);
                if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp) throw new UriFormatException();
            }
            catch (UriFormatException)
            {
                return new ResultadoFuente(f.Nombre, resultados, hayMas, $"{f.Nombre}: la dirección de la plantilla no es válida");
            }

            PaginaResultados? leida;
            try
            {
                if (!leidas.TryGetValue(url, out leida)) leidas[url] = leida = LectorRespuestaJson.Leer(await PedirAsync(f, url, ct), f);
            }
            catch (FalloFuente ex)
            {
                return new ResultadoFuente(f.Nombre, resultados, hayMas, $"{f.Nombre}: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Formato cambiado, codificación no válida…: nunca sale de aquí, es un error de esta fuente.
                return new ResultadoFuente(f.Nombre, resultados, hayMas, $"{f.Nombre}: formato de respuesta no reconocido");
            }

            foreach (var leido in leida.Resultados)
            {
                var r = leido;
                Encaje? encaje = null;
                if (pedida is { } p)
                {
                    if (r.Archivos is { Count: > 0 } archivos)
                    {
                        // El archivo más pequeño que cubre la casilla: el que menos ocupa sin perder nitidez.
                        var elegido = archivos.Where(a => EncajeResolucion.DeVideo(a.Tamano, p) is not null)
                                              .MinBy(a => (long)a.Tamano.Ancho * a.Tamano.Alto);
                        if (elegido is null) continue;
                        r = r with { Imagen = elegido.Url, Tamano = elegido.Tamano };
                    }
                    if ((encaje = esVideo ? EncajeResolucion.DeVideo(r.Tamano, p) : EncajeResolucion.De(r.Tamano, p)) is null) continue;
                }
                // Un vídeo sale una vez aunque encaje en varias casillas (con otro archivo en cada una).
                var clave = r.Archivos is not null ? r.Ficha ?? r.Miniatura : r.Imagen;
                if (vistas.Add(clave)) resultados.Add(new ResultadoEncajado(r, encaje, pedida));
            }
            hayMas |= leida.UltimaPagina is { } ultima ? pagina < ultima : leida.Resultados.Count > 0;
        }
        return new ResultadoFuente(f.Nombre, resultados, hayMas, null);
    }

    /// <summary>La fuente usa {clave} (en la dirección o en una cabecera) y el usuario aún no la ha escrito.</summary>
    public static bool FaltaClave(FuenteBusqueda f) =>
        string.IsNullOrWhiteSpace(f.Clave)
        && new[] { f.PlantillaExacta, f.PlantillaMayor, f.PlantillaSinResolucion }.Concat(f.Cabeceras.Values)
            .Any(t => t?.Contains("{clave}", StringComparison.Ordinal) == true);

    /// <summary>Hace la petición respetando el turno de la fuente; reintenta una vez tras un 429.</summary>
    private async Task<string> PedirAsync(FuenteBusqueda f, Uri url, CancellationToken ct)
    {
        for (var intento = 0; ; intento++)
        {
            await EsperarTurnoAsync(f.Nombre, ct);
            using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
            peticion.Headers.UserAgent.ParseAdd(AgenteUsuario);
            foreach (var (nombre, valor) in f.Cabeceras)
                peticion.Headers.TryAddWithoutValidation(nombre, valor.Replace("{clave}", f.Clave.Trim(), StringComparison.Ordinal));

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TiempoMaximo);
            HttpResponseMessage respuesta;
            try
            {
                respuesta = await _http.SendAsync(peticion, limite.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new FalloFuente("no responde");
            }
            catch (HttpRequestException)
            {
                throw new FalloFuente("sin conexión");
            }

            using (respuesta)
            {
                if (respuesta.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var espera = respuesta.Headers.RetryAfter?.Delta ?? EsperaPor429;
                    if (intento > 0 || espera > EsperaMaxima429) throw new FalloFuente("demasiadas peticiones; espera un momento");
                    await _esperar(espera, ct);
                    continue;
                }
                if (!respuesta.IsSuccessStatusCode) throw new FalloFuente($"error {(int)respuesta.StatusCode}");
                try
                {
                    return await respuesta.Content.ReadAsStringAsync(limite.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new FalloFuente("no responde");
                }
            }
        }
    }

    /// <summary>Reserva el siguiente hueco de la fuente (como mucho una petición por <see cref="Intervalo"/>) y lo espera.</summary>
    private async Task EsperarTurnoAsync(string fuente, CancellationToken ct)
    {
        TimeSpan espera;
        lock (_turnos)
        {
            var ahora = _reloj.Elapsed;
            var siguiente = _siguientePeticion.GetValueOrDefault(fuente, ahora);
            var turno = siguiente > ahora ? siguiente : ahora;
            espera = turno - ahora;
            _siguientePeticion[fuente] = turno + Intervalo;
        }
        if (espera > TimeSpan.Zero) await _esperar(espera, ct);
    }

    private sealed class FalloFuente(string mensaje) : Exception(mensaje);
}
