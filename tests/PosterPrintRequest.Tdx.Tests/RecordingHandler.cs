using System.Net;
using System.Text;

namespace PosterPrintRequest.Tdx.Tests;

public sealed record RecordedCall(HttpMethod Method, Uri Uri, string? Body, bool HasAuthorization);

public sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Func<RecordedCall, int, HttpResponseMessage> _responder;

    public RecordingHandler(Func<RecordedCall, int, HttpResponseMessage> responder) => _responder = responder;

    public List<RecordedCall> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var call = new RecordedCall(request.Method, request.RequestUri!, body, request.Headers.Authorization is not null);
        Calls.Add(call);
        return _responder(call, Calls.Count);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Plain(HttpStatusCode status, string text) =>
        new(status) { Content = new StringContent(text, Encoding.UTF8, "text/plain") };
}
