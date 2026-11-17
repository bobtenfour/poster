using PosterPrintRequest.Tdx;

namespace PosterPrintRequest.Web.Tdx;

/// <summary>The TDX operations the processor uses (TDX Integration Contract section 10.1). One connection per attempt.</summary>
public interface ITdxConnection : IDisposable
{
    Task AuthenticateAsync(TdxCredentialValue credential, CancellationToken cancellationToken);

    Task<RequestorResolution> ResolveRequestorAsync(string? email, string? name, CancellationToken cancellationToken);

    Task<LookupResult> LookupAsync(TicketRequest request, CancellationToken cancellationToken);

    Task<CreationResult> CreateIfAbsentAsync(TicketRequest request, Func<CancellationToken, Task> beforeCreate, CancellationToken cancellationToken);
}

public interface ITdxConnectionFactory
{
    ITdxConnection Open();
}

/// <summary>Opens connections over the shared library client, through one shared rate-limited handler.</summary>
public sealed class TdxConnectionFactory : ITdxConnectionFactory, IDisposable
{
    private readonly TdxOptions _options;
    private readonly HttpMessageHandler _handler;

    public TdxConnectionFactory(TdxOptions options, TimeProvider time)
    {
        _options = options;
        _handler = new TdxRateLimitHandler(new SocketsHttpHandler { AllowAutoRedirect = false }, time);
    }

    public ITdxConnection Open() =>
        new TdxClientConnection(new TdxClient(
            _options.BaseUrl,
            _options.Ticket,
            _options.Lookups,
            _handler,
            requestTimeout: _options.RequestTimeout));

    public void Dispose() => _handler.Dispose();

    private sealed class TdxClientConnection : ITdxConnection
    {
        private readonly TdxClient _client;

        public TdxClientConnection(TdxClient client)
        {
            _client = client;
        }

        public Task AuthenticateAsync(TdxCredentialValue credential, CancellationToken cancellationToken) =>
            _client.AuthenticateAsync(credential.UserName, credential.Password, cancellationToken);

        public Task<RequestorResolution> ResolveRequestorAsync(string? email, string? name, CancellationToken cancellationToken) =>
            _client.ResolveRequestorAsync(email, name, cancellationToken);

        public Task<LookupResult> LookupAsync(TicketRequest request, CancellationToken cancellationToken) =>
            _client.LookupAsync(request, cancellationToken);

        public Task<CreationResult> CreateIfAbsentAsync(TicketRequest request, Func<CancellationToken, Task> beforeCreate, CancellationToken cancellationToken) =>
            _client.CreateIfAbsentAsync(request, beforeCreate, cancellationToken);

        public void Dispose() => _client.Dispose();
    }
}

/// <summary>
/// Keeps every TDX call of this process under the documented limit of 60 calls per 60 seconds
/// (TDX Integration Contract section 8.6), with a margin.
/// </summary>
public sealed class TdxRateLimitHandler : DelegatingHandler
{
    public const int CallsPerWindow = 50;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly Queue<DateTimeOffset> _calls = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time;

    public TdxRateLimitHandler(HttpMessageHandler inner, TimeProvider time)
        : base(inner)
    {
        _time = time;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                var now = _time.GetUtcNow();
                while (_calls.Count > 0 && now - _calls.Peek() >= Window)
                {
                    _calls.Dequeue();
                }

                if (_calls.Count < CallsPerWindow)
                {
                    _calls.Enqueue(now);
                    break;
                }

                await Task.Delay(Window - (now - _calls.Peek()), _time, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }

        return await base.SendAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _gate.Dispose();
        }

        base.Dispose(disposing);
    }
}
