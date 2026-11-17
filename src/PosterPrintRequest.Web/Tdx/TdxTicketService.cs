using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PosterPrintRequest.Web.Tdx;

/// <summary>Wakes the ticket processor after a request is accepted. Acceptance never waits for TDX (section 4.4).</summary>
public sealed class TdxTicketSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<bool> Reader => _channel.Reader;

    public void Notify() => _channel.Writer.TryWrite(true);
}

/// <summary>
/// The single background processor of section 8A.1. When the TDX configuration or credential is missing or invalid,
/// the application still starts and accepts posters; tickets stay Pending and the problem is logged without secrets
/// (section 9.4).
/// </summary>
public sealed class TdxTicketService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly TdxTicketSignal _signal;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<TdxTicketService> _logger;

    public TdxTicketService(
        IServiceScopeFactory scopes,
        TdxTicketSignal signal,
        IConfiguration configuration,
        TimeProvider time,
        ILogger<TdxTicketService> logger)
    {
        _scopes = scopes;
        _signal = signal;
        _configuration = configuration;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = TdxOptions.Read(_configuration, out var problem);
        if (options is null)
        {
            _logger.LogWarning("TDX ticket creation is disabled: {Problem} Accepted requests keep their tickets pending.", problem);
            return;
        }

        using var connections = new TdxConnectionFactory(options, _time);
        string? lastCredentialProblem = null;
        TdxCredentialValue ReadCredential()
        {
            try
            {
                var value = TdxCredential.Read(options.CredentialFile);
                lastCredentialProblem = null;
                return value;
            }
            catch (TdxCredentialException exception)
            {
                if (lastCredentialProblem != exception.Message)
                {
                    lastCredentialProblem = exception.Message;
                    _logger.LogWarning("TDX credential problem: {Problem} Tickets stay pending.", exception.Message);
                }

                throw;
            }
        }

        var processor = new TdxTicketProcessor(connections, ReadCredential, options, _time, _logger);
        _logger.LogInformation("TDX ticket processor started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<ITdxTicketStore>();
                await processor.ProcessDueAsync(store, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning("TDX ticket pass failed ({Error}).", exception.GetType().Name);
            }

            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            try
            {
                var delay = Task.Delay(PollInterval, _time, wait.Token);
                var wake = _signal.Reader.WaitToReadAsync(wait.Token).AsTask();
                await Task.WhenAny(delay, wake);
                await wait.CancelAsync();
                while (_signal.Reader.TryRead(out _))
                {
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
