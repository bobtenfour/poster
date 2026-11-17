using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Tdx;

/// <summary>What the processor needs of one claimed request.</summary>
public sealed record TdxWork(
    int PosterRequestId,
    string PosterId,
    string? PosterTitle,
    string Name,
    string Email,
    string State,
    DateTime ClaimedUntil);

/// <summary>
/// Database operations of the exactly-once coordination (TDX Integration Contract section 8A).
/// Every write after the claim requires the same claim (TdxClaimedUntil equal to the claimer's value).
/// </summary>
public interface ITdxTicketStore
{
    /// <summary>Requests that need work: Pending or Uncertain, no ticket number, not claimed or claim expired.</summary>
    Task<IReadOnlyList<int>> DueAsync(DateTime nowUtc, int max, CancellationToken cancellationToken);

    /// <summary>Section 8A.2: one atomic conditional UPDATE. Returns the request only when this caller claimed it.</summary>
    Task<TdxWork?> ClaimAsync(int posterRequestId, DateTime nowUtc, DateTime claimUntil, CancellationToken cancellationToken);

    /// <summary>Section 8A.5: commits Uncertain under the claim before the creation is sent. False when the claim was lost.</summary>
    Task<bool> MarkUncertainAsync(TdxWork work, CancellationToken cancellationToken);

    /// <summary>Section 8A.4: stores the ticket number and Created, under the claim, only where no ticket number exists.</summary>
    Task<bool> CompleteCreatedAsync(TdxWork work, int ticketId, CancellationToken cancellationToken);

    /// <summary>Section 8A.4: stores the state and clears the claim, under the claim.</summary>
    Task<bool> ReleaseAsync(TdxWork work, string state, CancellationToken cancellationToken);
}

public sealed class TdxTicketStore : ITdxTicketStore
{
    private readonly PosterPrintRequestDbContext _db;

    public TdxTicketStore(PosterPrintRequestDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<int>> DueAsync(DateTime nowUtc, int max, CancellationToken cancellationToken) =>
        await _db.PosterRequests
            .AsNoTracking()
            .Where(request => request.TdxTicketId == null
                && (request.TdxTicketState == TdxTicketStates.Pending || request.TdxTicketState == TdxTicketStates.Uncertain)
                && (request.TdxClaimedUntil == null || request.TdxClaimedUntil < nowUtc))
            .OrderBy(request => request.PosterRequestId)
            .Select(request => request.PosterRequestId)
            .Take(max)
            .ToListAsync(cancellationToken);

    public async Task<TdxWork?> ClaimAsync(int posterRequestId, DateTime nowUtc, DateTime claimUntil, CancellationToken cancellationToken)
    {
        var changed = await _db.PosterRequests
            .Where(request => request.PosterRequestId == posterRequestId
                && request.TdxTicketId == null
                && (request.TdxTicketState == TdxTicketStates.Pending || request.TdxTicketState == TdxTicketStates.Uncertain)
                && (request.TdxClaimedUntil == null || request.TdxClaimedUntil < nowUtc))
            .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.TdxClaimedUntil, claimUntil), cancellationToken);
        if (changed != 1)
        {
            return null;
        }

        return await _db.PosterRequests
            .AsNoTracking()
            .Where(request => request.PosterRequestId == posterRequestId && request.TdxClaimedUntil == claimUntil)
            .Select(request => new TdxWork(
                request.PosterRequestId,
                request.PosterId,
                request.PosterTitle,
                request.Name,
                request.Email,
                request.TdxTicketState,
                claimUntil))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> MarkUncertainAsync(TdxWork work, CancellationToken cancellationToken) =>
        await Claimed(work)
            .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.TdxTicketState, TdxTicketStates.Uncertain), cancellationToken) == 1;

    public async Task<bool> CompleteCreatedAsync(TdxWork work, int ticketId, CancellationToken cancellationToken) =>
        await Claimed(work)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.TdxTicketId, ticketId)
                .SetProperty(request => request.TdxTicketState, TdxTicketStates.Created)
                .SetProperty(request => request.TdxClaimedUntil, (DateTime?)null), cancellationToken) == 1;

    public async Task<bool> ReleaseAsync(TdxWork work, string state, CancellationToken cancellationToken) =>
        await Claimed(work)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(request => request.TdxTicketState, state)
                .SetProperty(request => request.TdxClaimedUntil, (DateTime?)null), cancellationToken) == 1;

    private IQueryable<PosterRequest> Claimed(TdxWork work) =>
        _db.PosterRequests.Where(request => request.PosterRequestId == work.PosterRequestId
            && request.TdxTicketId == null
            && request.TdxClaimedUntil == work.ClaimedUntil);
}
