using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

/// <summary>
/// Links a historical request to the Entra object identifier (Authentication Contract section 3.4).
/// The only evidence is an exact string match between the signed-in requester's onprem_sid and the
/// request's SubmittedBySecurityIdentifier. No account name, no preferred_username, and no approximate
/// match is ever used, and no mapping is created.
/// </summary>
public static class HistoricalLinking
{
    public static async Task LinkAsync(
        PosterPrintRequestDbContext db,
        InstitutionalActor actor,
        CancellationToken cancellationToken)
    {
        if (!actor.IsValid || actor.OnPremisesSecurityIdentifier is not { } sid)
        {
            return;
        }

        // The database comparison is case-insensitive and ignores trailing spaces, so the candidates
        // are confirmed with an ordinal comparison before anything is written.
        var candidates = await db.PosterRequests
            .AsNoTracking()
            .Where(request => request.SubmittedByObjectId == null && request.SubmittedBySecurityIdentifier == sid)
            .Select(request => new { request.PosterRequestId, request.SubmittedBySecurityIdentifier })
            .ToListAsync(cancellationToken);
        var exact = candidates
            .Where(candidate => string.Equals(candidate.SubmittedBySecurityIdentifier, sid, StringComparison.Ordinal))
            .Select(candidate => candidate.PosterRequestId)
            .ToList();
        if (exact.Count == 0)
        {
            return;
        }

        await db.PosterRequests
            .Where(request => exact.Contains(request.PosterRequestId) && request.SubmittedByObjectId == null)
            .ExecuteUpdateAsync(
                update => update.SetProperty(request => request.SubmittedByObjectId, actor.ObjectId),
                cancellationToken);
    }
}
