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

public sealed class AcceptanceLookup
{
    private readonly PosterPrintRequestDbContext _db;

    public AcceptanceLookup(PosterPrintRequestDbContext db)
    {
        _db = db;
    }

    public async Task<AcceptanceDetails?> FindAsync(string? posterId, CancellationToken cancellationToken)
    {
        if (!PosterIds.IsPublic(posterId))
        {
            return null;
        }

        return await _db.PosterRequests
            .AsNoTracking()
            .Where(request => request.PosterId == posterId)
            .Select(request => new AcceptanceDetails
            {
                PosterId = request.PosterId,
                RequesterName = request.Name,
                EventName = request.ReasonName,
                DateIn = request.DateIn,
                LaminationRequested = request.LaminationRequested
            })
            .SingleOrDefaultAsync(cancellationToken);
    }
}
