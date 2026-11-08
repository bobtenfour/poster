using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public sealed class AcceptanceDetails
{
    public required string PosterId { get; init; }

    public required string RequesterName { get; init; }

    public string? EventName { get; init; }

    public required DateTime DateIn { get; init; }

    public required bool LaminationRequested { get; init; }
}

/// <summary>
/// The outcome of looking up an accepted request: the details, nothing found, or a historical request that
/// could not be linked to the signed-in account (Authentication Contract section 3.6).
/// </summary>
public sealed record AcceptanceResult(AcceptanceDetails? Details, bool HistoricalRequestNotLinked)
{
    public static AcceptanceResult NotFound { get; } = new(null, false);

    public static AcceptanceResult NotLinked { get; } = new(null, true);
}

public sealed class AcceptanceLookup
{
    public const string HistoricalNotLinkedMessage =
        "Your historical request could not be linked to your current account. Please contact a technician for assistance.";

    private readonly PosterPrintRequestDbContext _db;
    private readonly ILogger<AcceptanceLookup> _logger;

    public AcceptanceLookup(PosterPrintRequestDbContext db, ILogger<AcceptanceLookup> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AcceptanceResult> FindAsync(string? posterId, InstitutionalActor? actor, CancellationToken cancellationToken)
    {
        if (!PosterIds.IsPublic(posterId) || actor is null || !actor.IsValid)
        {
            return AcceptanceResult.NotFound;
        }

        await HistoricalLinking.LinkAsync(_db, actor, cancellationToken);
        var found = await _db.PosterRequests
            .AsNoTracking()
            .Where(request => request.PosterId == posterId)
            .Select(request => new
            {
                request.SubmittedByObjectId,
                Details = new AcceptanceDetails
                {
                    PosterId = request.PosterId,
                    RequesterName = request.Name,
                    EventName = request.ReasonName,
                    DateIn = request.DateIn,
                    LaminationRequested = request.LaminationRequested
                }
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return AcceptanceResult.NotFound;
        }

        if (found.SubmittedByObjectId == actor.ObjectId)
        {
            return new AcceptanceResult(found.Details, false);
        }

        if (found.SubmittedByObjectId is not null)
        {
            return AcceptanceResult.NotFound;
        }

        _logger.LogWarning(
            "Operation {Operation} for Poster ID {PosterId} was denied: {Reason}.",
            "AcceptanceLookup",
            found.Details.PosterId,
            "The historical request has no verifiable link to the signed-in account");
        return AcceptanceResult.NotLinked;
    }
}
