using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public static class UserActivityQuery
{
    public static async Task<UserActivity> ForUserAsync(
        PosterPrintRequestDbContext db,
        string userName,
        CancellationToken cancellationToken)
    {
        var posters = await db.PosterRequests
            .AsNoTracking()
            .Where(request => request.SubmittedByUserName == userName)
            .OrderByDescending(request => request.DateIn)
            .Select(request => new UserPosterActivity(
                request.PosterId,
                request.ReasonName,
                request.PosterProcessing.Printed,
                request.DateIn))
            .ToListAsync(cancellationToken);

        var printed = posters.Count(poster => poster.Printed);
        var events = posters
            .Select(poster => poster.EventName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new UserActivity(printed, events, posters);
    }
}

public sealed record UserActivity(int PostersPrinted, int EventsParticipated, IReadOnlyList<UserPosterActivity> Posters)
{
    public static UserActivity Empty { get; } = new(0, 0, []);
}

public sealed record UserPosterActivity(string PosterId, string? EventName, bool Printed, DateTime DateIn);
