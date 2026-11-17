using System.Net;

namespace DeskWallpaper.Pruebas.Online;

/// <summary>Manejador HTTP de prueba: guarda las peticiones y devuelve las respuestas programadas en orden.</summary>
internal sealed class ManejadorFalso : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _respuestas = new();
    public List<HttpRequestMessage> Peticiones { get; } = [];

    /// <summary>Respuesta para cuando se acaban las programadas.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? PorDefecto { get; set; }

    public ManejadorFalso Responder(Func<HttpRequestMessage, HttpResponseMessage> respuesta)
    {
        _respuestas.Enqueue(respuesta);
        return this;
    }

    public ManejadorFalso Responder(HttpStatusCode estado, string cuerpo, string tipo = "application/json") =>
        Responder(_ => new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, tipo) });

    public ManejadorFalso Responder(byte[] cuerpo, string tipo) =>
        Responder(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(cuerpo) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo) } },
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Peticiones.Add(peticion);
        var r = _respuestas.Count > 0 ? _respuestas.Dequeue() : PorDefecto
                ?? throw new InvalidOperationException("No hay más respuestas programadas");
        return Task.FromResult(r(peticion));
    }
}
