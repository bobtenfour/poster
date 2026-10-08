using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PosterPrintRequest.Infrastructure.Persistence;

namespace PosterPrintRequest.Web.Requests;

public sealed class TechnicianDashboardSnapshot
{
    public required int Year { get; init; }

    public required int Month { get; init; }

    public required string PeriodLabel { get; init; }

    public required int PendingPrinting { get; init; }

    public required int PrintedThisMonth { get; init; }

    public required int PrintedThisYear { get; init; }

    public required int AwaitingPickup { get; init; }

    public required int TotalProcessed { get; init; }

    public required IReadOnlyList<TechnicianPerformance> Performance { get; init; }

    public required IReadOnlyList<TechnicianWorkItem> Queue { get; init; }
}

public sealed class TechnicianPerformance
{
    public required string Technician { get; init; }

    public required int PostersPrinted { get; init; }

    public required int Rank { get; init; }
}

public sealed class TechnicianWorkItem
{
    public required string PosterId { get; init; }

    public required string RequesterName { get; init; }

    public string? EventName { get; init; }

    public required string Stage { get; init; }

    public required DateTime DateIn { get; init; }

    public required bool LaminationRequested { get; init; }
}

public interface ITechnicianDashboard
{
    Task<TechnicianDashboardSnapshot> LoadAsync(int year, int month, CancellationToken cancellationToken);
}

public static class TechnicianPeriods
{
    public static (int Year, int Month) Current()
    {
        var now = DateTime.Now;
        return (now.Year, now.Month);
    }

    public static bool TryParse(string? value, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Trim().Split('-');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out month))
        {
            return false;
        }

        return year is >= 2000 and <= 9999 && month is >= 1 and <= 12;
    }
}

public sealed class TechnicianDashboard : ITechnicianDashboard
{
    public const string Unassigned = "Unassigned";

    private readonly PosterPrintRequestDbContext _db;

    public TechnicianDashboard(PosterPrintRequestDbContext db)
    {
        _db = db;
    }

    public async Task<TechnicianDashboardSnapshot> LoadAsync(int year, int month, CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        var rows = await _db.PosterRequests
            .AsNoTracking()
            .Select(request => new DashboardRow(
                request.PosterId,
                request.Name,
                request.ReasonName,
                request.DateIn,
                request.LaminationRequested,
                request.PosterProcessing.ITPerson,
                request.PosterProcessing.Received,
                request.PosterProcessing.Printed,
                request.PosterProcessing.Laminated,
                request.PosterProcessing.Notified,
                request.PosterProcessing.DateOut))
            .ToListAsync(cancellationToken);

        var printedThisMonth = rows.Where(row => PrintedIn(row, year, month)).ToList();
        var performance = Rank(printedThisMonth);
        var queue = rows
            .Where(row => row.DateOut is null)
            .Select(row => new TechnicianWorkItem
            {
                PosterId = row.PosterId,
                RequesterName = row.RequesterName,
                EventName = row.EventName,
                Stage = Stage(row),
                DateIn = row.DateIn,
                LaminationRequested = row.LaminationRequested
            })
            .OrderBy(item => StageOrder(item.Stage))
            .ThenBy(item => item.DateIn)
            .ThenBy(item => item.PosterId, StringComparer.Ordinal)
            .ToList();

        return new TechnicianDashboardSnapshot
        {
            Year = year,
            Month = month,
            PeriodLabel = new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            PendingPrinting = rows.Count(row => !row.Printed),
            PrintedThisMonth = printedThisMonth.Count,
            PrintedThisYear = rows.Count(row => row.Printed && row.Received?.Year == year),
            AwaitingPickup = rows.Count(row => row.Notified && row.DateOut is null),
            TotalProcessed = rows.Count(row => row.DateOut is not null),
            Performance = performance,
            Queue = queue
        };
    }

    private static bool PrintedIn(DashboardRow row, int year, int month) =>
        row.Printed && row.Received is { } received && received.Year == year && received.Month == month;

    private static List<TechnicianPerformance> Rank(IReadOnlyList<DashboardRow> printed)
    {
        var grouped = printed
            .GroupBy(row => string.IsNullOrWhiteSpace(row.ItPerson) ? Unassigned : row.ItPerson.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Technician = group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Technician, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranked = new List<TechnicianPerformance>(grouped.Count);
        var rank = 0;
        var previous = -1;
        for (var index = 0; index < grouped.Count; index++)
        {
            if (grouped[index].Count != previous)
            {
                rank = index + 1;
                previous = grouped[index].Count;
            }

            ranked.Add(new TechnicianPerformance
            {
                Technician = grouped[index].Technician,
                PostersPrinted = grouped[index].Count,
                Rank = rank
            });
        }

        return ranked;
    }

    private static string Stage(DashboardRow row)
    {
        if (row.Received is null)
        {
            return "Receive";
        }

        if (!row.Printed)
        {
            return "Print";
        }

        if (row.LaminationRequested && !row.Laminated)
        {
            return "Laminate";
        }

        if (!row.Notified)
        {
            return "Notify";
        }

        return "Pickup";
    }

    private static int StageOrder(string stage) => stage switch
    {
        "Receive" => 0,
        "Print" => 1,
        "Laminate" => 2,
        "Notify" => 3,
        "Pickup" => 4,
        _ => 5
    };

    private sealed record DashboardRow(
        string PosterId,
        string RequesterName,
        string? EventName,
        DateTime DateIn,
        bool LaminationRequested,
        string? ItPerson,
        DateOnly? Received,
        bool Printed,
        bool Laminated,
        bool Notified,
        DateOnly? DateOut);
}
