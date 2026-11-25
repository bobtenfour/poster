using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

/// <summary>Search categories of the technician search bar (Technician Search Contract v1, section 3).</summary>
public static class PosterSearchModes
{
    public const string PosterId = "poster";

    public const string Name = "name";

    public const string Date = "date";

    public const string Event = "event";

    /// <summary>The dropdown first shows Select (no category), then these categories in this order.</summary>
    public const string SelectionLabel = "Select";

    public static readonly IReadOnlyList<(string Value, string Label)> All =
    [
        (PosterId, "Poster ID"),
        (Name, "Name"),
        (Date, "Date"),
        (Event, "Event")
    ];

    public static string Normalize(string? mode) =>
        All.Any(option => option.Value == mode) ? mode! : PosterId;
}

/// <summary>One row of a list result (section 5).</summary>
public sealed record PosterSearchRow(string PosterId, string Name, string? EventName, DateTime DateIn, int? TicketId, string Status);

public sealed class PosterSearchResult
{
    public required IReadOnlyList<PosterSearchRow> Rows { get; init; }

    /// <summary>True when more than <see cref="TechnicianSearch.MaxRows"/> posters match.</summary>
    public required bool More { get; init; }
}

public sealed record PosterSearchEvent(int Id, string Name);

public interface ITechnicianSearch
{
    Task<PosterSearchResult> ByNameAsync(string text, CancellationToken cancellationToken);

    Task<PosterSearchResult> ByDateAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    Task<PosterSearchResult> ByEventAsync(int reasonId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PosterSearchEvent>> EventsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Read-only poster search for technicians (Technician Search Contract v1). It reads PosterRequests and their
/// processing and changes nothing.
/// </summary>
public sealed class TechnicianSearch : ITechnicianSearch
{
    public const int MaxRows = 50;

    // Case- and accent-insensitive comparison for the name search (section 4.2).
    private const string NameCollation = "Latin1_General_100_CI_AI";

    private readonly PosterPrintRequestDbContext _db;

    public TechnicianSearch(PosterPrintRequestDbContext db)
    {
        _db = db;
    }

    public Task<PosterSearchResult> ByNameAsync(string text, CancellationToken cancellationToken)
    {
        var term = text.Trim();
        return ListAsync(
            _db.PosterRequests.Where(request => EF.Functions.Collate(request.Name, NameCollation).Contains(term)),
            cancellationToken);
    }

    public Task<PosterSearchResult> ByDateAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = _db.PosterRequests.AsQueryable();
        if (from is DateOnly start)
        {
            var lower = start.ToDateTime(TimeOnly.MinValue);
            query = query.Where(request => request.DateIn >= lower);
        }

        if (to is DateOnly end)
        {
            var upper = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(request => request.DateIn < upper);
        }

        return ListAsync(query, cancellationToken);
    }

    public Task<PosterSearchResult> ByEventAsync(int reasonId, CancellationToken cancellationToken) =>
        ListAsync(_db.PosterRequests.Where(request => request.ReasonId == reasonId), cancellationToken);

    public async Task<IReadOnlyList<PosterSearchEvent>> EventsAsync(CancellationToken cancellationToken) =>
        await _db.Reasons
            .AsNoTracking()
            .OrderBy(reason => reason.Name)
            .Select(reason => new PosterSearchEvent(reason.ReasonId, reason.Name))
            .ToListAsync(cancellationToken);

    private static async Task<PosterSearchResult> ListAsync(IQueryable<PosterRequest> query, CancellationToken cancellationToken)
    {
        var rows = await query
            .AsNoTracking()
            .OrderByDescending(request => request.DateIn)
            .ThenByDescending(request => request.PosterRequestId)
            .Take(MaxRows + 1)
            .Select(request => new
            {
                request.PosterId,
                request.Name,
                request.ReasonName,
                request.DateIn,
                request.TdxTicketId,
                Deleted = request.TemporaryDeletionAt != null || request.PermanentlyDeletedAt != null,
                request.PosterProcessing.Received,
                request.PosterProcessing.Printed,
                request.LaminationRequested,
                request.PosterProcessing.Laminated,
                request.PosterProcessing.Notified,
                request.PosterProcessing.DateOut
            })
            .ToListAsync(cancellationToken);

        return new PosterSearchResult
        {
            More = rows.Count > MaxRows,
            Rows = rows
                .Take(MaxRows)
                .Select(row => new PosterSearchRow(
                    row.PosterId,
                    row.Name,
                    row.ReasonName,
                    row.DateIn,
                    row.TdxTicketId,
                    Status(row.Deleted, row.Received, row.Printed, row.LaminationRequested, row.Laminated, row.Notified, row.DateOut)))
                .ToArray()
        };
    }

    // The dashboard stage names, plus Picked up and Deleted (section 5.2).
    public static string Status(bool deleted, DateOnly? received, bool printed, bool laminationRequested, bool laminated, bool notified, DateOnly? dateOut)
    {
        if (deleted)
        {
            return "Deleted";
        }

        if (dateOut is not null)
        {
            return "Picked up";
        }

        if (received is null)
        {
            return "Receive";
        }

        if (!printed)
        {
            return "Print";
        }

        if (laminationRequested && !laminated)
        {
            return "Laminate";
        }

        return notified ? "Pickup" : "Notify";
    }
}
