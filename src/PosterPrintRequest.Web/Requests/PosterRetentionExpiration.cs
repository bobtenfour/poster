using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PosterPrintRequest.Web.Requests;

/// <summary>
/// Deletes retained poster directories once 48 hours have passed since deletion.
/// Startup runs first so posters that expired while the application was down are removed
/// before requests are served. The loop then waits until the next expiration, and at least hourly.
/// </summary>
public sealed class PosterRetentionExpiration : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PosterRetentionSchedule _schedule;
    private readonly ILogger<PosterRetentionExpiration> _logger;

    public PosterRetentionExpiration(
        IServiceScopeFactory scopes,
        PosterRetentionSchedule schedule,
        ILogger<PosterRetentionExpiration> logger)
    {
        _scopes = scopes;
        _schedule = schedule;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await ExpireSafeAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await DelaySafeAsync(stoppingToken);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var delayTask = Task.Delay(delay, wait.Token);
            var wakeTask = _schedule.Reader.WaitToReadAsync(stoppingToken).AsTask();
            try
            {
                await Task.WhenAny(delayTask, wakeTask);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await wait.CancelAsync();
            try
            {
                await delayTask;
            }
            catch (OperationCanceledException)
            {
            }
            while (_schedule.Reader.TryRead(out _))
            {
            }

            await ExpireSafeAsync(stoppingToken);
        }
    }

    private async Task<TimeSpan> DelaySafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var retention = scope.ServiceProvider.GetRequiredService<IPosterRetention>();
            return await retention.DelayUntilNextExpirationAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogWarning("Temporary poster expiration could not be scheduled.");
            return TimeSpan.FromHours(1);
        }
    }

    private async Task ExpireSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var retention = scope.ServiceProvider.GetRequiredService<IPosterRetention>();
            await retention.ExpireAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            _logger.LogWarning("Temporary poster expiration could not be processed.");
        }
    }
}
