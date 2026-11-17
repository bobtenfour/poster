using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public static class UserActivityQuery
{
    public static async Task<UserActivity> ForActorAsync(
        PosterPrintRequestDbContext db,
        InstitutionalActor actor,
        CancellationToken cancellationToken)
    {
        await HistoricalLinking.LinkAsync(db, actor, cancellationToken);
        var posters = await db.PosterRequests
            .AsNoTracking()
            .Where(request => request.SubmittedByObjectId == actor.ObjectId)
            .OrderByDescending(request => request.DateIn)
            .Select(request => new UserPosterActivity(
                request.PosterId,
                request.ReasonName,
                request.PosterProcessing.Printed,
                request.DateIn)
            {
                CanReplace = request.TemporaryDeletionAt == null && request.PermanentlyDeletedAt == null
            })
            .ToListAsync(cancellationToken);
        var replacements = await db.PosterReplacements
            .AsNoTracking()
            .CountAsync(replacement => replacement.PosterRequest.SubmittedByObjectId == actor.ObjectId, cancellationToken);

        var printed = posters.Count(poster => poster.Printed);
        var events = posters
            .Select(poster => poster.EventName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new UserActivity(printed, events, posters) { Replacements = replacements };
    }
}

public sealed record UserActivity(int PostersPrinted, int EventsParticipated, IReadOnlyList<UserPosterActivity> Posters)
{
    /// <summary>Replacements on the requester's own requests (TDX Integration Contract section 17.12).</summary>
    public int Replacements { get; init; }

    public static UserActivity Empty { get; } = new(0, 0, []);
}

public sealed record UserPosterActivity(string PosterId, string? EventName, bool Printed, DateTime DateIn)
{
    /// <summary>The poster can be replaced by its requester (section 17.3): not deleted.</summary>
    public bool CanReplace { get; init; }
}
