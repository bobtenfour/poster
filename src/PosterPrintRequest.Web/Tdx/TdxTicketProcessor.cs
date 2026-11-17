using Microsoft.Extensions.Logging;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Tdx;

namespace PosterPrintRequest.Web.Tdx;

/// <summary>
/// Creates the TDX ticket of accepted requests exactly once (TDX Integration Contract sections 8 and 8A).
/// It is the only code path that calls TDX for a request. Logs carry the operation, Poster ID, ticket number and
/// HTTP status only; never the credential, the token, the requester's name or email (section 10.5).
/// </summary>
public sealed class TdxTicketProcessor
{
    /// <summary>Lookup rounds for an Uncertain request in one process lifetime; after them it stays "Verify in TDX".</summary>
    public const int MaxUncertainRounds = 5;

    public const int BatchSize = 20;

    private static readonly TimeSpan FirstBackOff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxBackOff = TimeSpan.FromHours(1);

    private readonly ITdxConnectionFactory _connections;
    private readonly Func<TdxCredentialValue> _credential;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Dictionary<int, (int Failures, DateTimeOffset NotBefore)> _backOff = new();
    private readonly object _sync = new();

    public TdxTicketProcessor(
        ITdxConnectionFactory connections,
        Func<TdxCredentialValue> credential,
        TdxOptions options,
        TimeProvider time,
        ILogger logger)
    {
        _connections = connections;
        _credential = credential;
        _time = time;
        _logger = logger;
        var calls = 7 + options.Lookups.Attempts;
        AttemptBudget = options.RequestTimeout * calls
            + options.Lookups.Interval * options.Lookups.Attempts
            + TimeSpan.FromMinutes(2);
        Lease = AttemptBudget + TimeSpan.FromMinutes(2);
    }

    /// <summary>Every TDX call of one attempt is cancelled after this time (section 8A.3).</summary>
    public TimeSpan AttemptBudget { get; }

    /// <summary>The claim lasts longer than one attempt can (section 8A.3).</summary>
    public TimeSpan Lease { get; }

    /// <summary>Processes the requests that are due. Returns the number of attempts made.</summary>
    public async Task<int> ProcessDueAsync(ITdxTicketStore store, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var due = await store.DueAsync(now.UtcDateTime, BatchSize, cancellationToken);
        var attempts = 0;
        foreach (var id in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Ready(id, _time.GetUtcNow()))
            {
                continue;
            }

            attempts++;
            await ProcessOneAsync(store, id, cancellationToken);
        }

        return attempts;
    }

    /// <summary>One attempt for one request. Does nothing when another processor holds the claim.</summary>
    public async Task ProcessOneAsync(ITdxTicketStore store, int posterRequestId, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var work = await store.ClaimAsync(posterRequestId, now, now + Lease, cancellationToken);
        if (work is null)
        {
            return;
        }

        var markerWritten = false;
        var completed = false;
        string finalState = work.State;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(AttemptBudget);
        try
        {
            using var connection = _connections.Open();
            await connection.AuthenticateAsync(_credential(), attempt.Token);
            var request = BuildRequest(work, null);

            if (work.State == TdxTicketStates.Uncertain)
            {
                // Section 8.5: Uncertain never creates. Lookups only.
                var lookup = await connection.LookupAsync(request, attempt.Token);
                if (lookup.Succeeded && lookup.MatchingTicketIds.Count == 1)
                {
                    await CompleteAsync(store, work, lookup.MatchingTicketIds[0], "lookup");
                    completed = true;
                    return;
                }

                _logger.LogWarning(
                    "TDX ticket of {PosterId} is uncertain; lookup found {Count} ticket(s), HTTP {Status}.",
                    work.PosterId,
                    lookup.Succeeded ? lookup.MatchingTicketIds.Count : null,
                    lookup.Evidence.HttpStatus);
                RecordFailure(work.PosterRequestId, uncertain: true);
                return;
            }

            var requestor = await connection.ResolveRequestorAsync(work.Email, work.Name, attempt.Token);
            if (requestor.Retry)
            {
                _logger.LogWarning("TDX people search for {PosterId} failed; the ticket stays pending.", work.PosterId);
                RecordFailure(work.PosterRequestId, uncertain: false);
                return;
            }

            request = BuildRequest(work, requestor);
            var result = await connection.CreateIfAbsentAsync(
                request,
                async token =>
                {
                    // Section 8A.5: the marker is committed before the creation is sent; without the claim, nothing is sent.
                    if (!await store.MarkUncertainAsync(work, CancellationToken.None))
                    {
                        throw new TdxClaimLostException();
                    }

                    markerWritten = true;
                    finalState = TdxTicketStates.Uncertain;
                },
                attempt.Token);

            if (result.TicketId is int ticketId && result.Outcome is TdxOutcome.Created or TdxOutcome.AlreadyExists)
            {
                await CompleteAsync(store, work, ticketId, result.Outcome == TdxOutcome.Created ? "create" : "lookup");
                completed = true;
                return;
            }

            if (!result.CreateSent)
            {
                // The lookup before creation failed or found several tickets: nothing was sent.
                finalState = markerWritten ? TdxTicketStates.Uncertain : TdxTicketStates.Pending;
            }
            else if (result.Outcome == TdxOutcome.Failed && result.CreateRejected)
            {
                // Section 8A.5: a readable 4xx other than 408 proves no ticket was created.
                finalState = TdxTicketStates.Pending;
            }
            else
            {
                finalState = TdxTicketStates.Uncertain;
            }

            _logger.LogWarning(
                "TDX ticket of {PosterId} was not stored: outcome {Outcome}, HTTP {Status}; state {State}.",
                work.PosterId,
                result.Outcome,
                result.Operations.LastOrDefault()?.HttpStatus,
                finalState);
            RecordFailure(work.PosterRequestId, finalState == TdxTicketStates.Uncertain);
        }
        catch (TdxClaimLostException)
        {
            _logger.LogWarning("TDX claim of {PosterId} was lost before creation; nothing was sent.", work.PosterId);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "TDX attempt for {PosterId} stopped ({Error}); state {State}.",
                work.PosterId,
                exception.GetType().Name,
                markerWritten ? TdxTicketStates.Uncertain : work.State);
            RecordFailure(work.PosterRequestId, markerWritten || work.State == TdxTicketStates.Uncertain);
        }
        finally
        {
            if (!completed)
            {
                // Section 8A.4: the state is written under the claim and the claim is cleared. Once the marker is
                // committed, only a readable 4xx (finalState Pending after the creation) may return it to Pending.
                await ReleaseAsync(store, work, markerWritten && finalState != TdxTicketStates.Pending ? TdxTicketStates.Uncertain : finalState);
            }
        }
    }

    /// <summary>Section 5: title, description, account and requestor of the ticket.</summary>
    public static TicketRequest BuildRequest(TdxWork work, RequestorResolution? requestor) => new()
    {
        PosterId = work.PosterId,
        Title = work.PosterId + " — " + (work.PosterTitle ?? string.Empty).Trim(),
        Description = "Requester: " + work.Name,
        RequestorUid = requestor?.RequestorUid,
        AccountId = requestor?.RequestorUid is null ? null : requestor.AccountId,
        Match = TicketMatch.TitleStartsWithPosterId,
        DisallowRequestorCreation = true
    };

    private async Task CompleteAsync(ITdxTicketStore store, TdxWork work, int ticketId, string how)
    {
        lock (_sync)
        {
            _backOff.Remove(work.PosterRequestId);
        }

        if (await store.CompleteCreatedAsync(work, ticketId, CancellationToken.None))
        {
            _logger.LogInformation("TDX ticket {TicketId} stored for {PosterId} ({How}).", ticketId, work.PosterId, how);
        }
        else
        {
            _logger.LogWarning("TDX ticket {TicketId} for {PosterId} was not stored: the claim was lost.", ticketId, work.PosterId);
        }
    }

    private async Task ReleaseAsync(ITdxTicketStore store, TdxWork work, string state)
    {
        try
        {
            await store.ReleaseAsync(work, state, CancellationToken.None);
        }
        catch (Exception)
        {
            // The claim expires on its own; the state written before (Uncertain marker) is already durable.
            _logger.LogWarning("TDX claim of {PosterId} could not be released.", work.PosterId);
        }
    }

    private bool Ready(int id, DateTimeOffset now)
    {
        lock (_sync)
        {
            return !_backOff.TryGetValue(id, out var entry) || now >= entry.NotBefore;
        }
    }

    private void RecordFailure(int id, bool uncertain)
    {
        lock (_sync)
        {
            var failures = _backOff.TryGetValue(id, out var entry) ? entry.Failures + 1 : 1;
            var delay = TimeSpan.FromTicks(Math.Min(MaxBackOff.Ticks, FirstBackOff.Ticks * (1L << Math.Min(failures - 1, 10))));
            var notBefore = uncertain && failures >= MaxUncertainRounds
                ? DateTimeOffset.MaxValue
                : _time.GetUtcNow() + delay;
            _backOff[id] = (failures, notBefore);
        }
    }

    private sealed class TdxClaimLostException : Exception
    {
    }
}
