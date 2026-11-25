using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class Technician : ComponentBase
{
    [SupplyParameterFromQuery(Name = "posterId")]
    public string? PosterIdQuery { get; set; }

    [SupplyParameterFromQuery(Name = "period")]
    public string? PeriodQuery { get; set; }

    // Technician Search Contract v1, section 3.
    [SupplyParameterFromQuery(Name = "by")]
    public string? SearchByQuery { get; set; }

    [SupplyParameterFromQuery(Name = "q")]
    public string? SearchTextQuery { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? SearchFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = "to")]
    public string? SearchToQuery { get; set; }

    [SupplyParameterFromQuery(Name = "event")]
    public string? SearchEventQuery { get; set; }

    [Inject]
    private ITechnicianSearch Search { get; set; } = null!;

    private string SearchMode { get; set; } = PosterSearchModes.PosterId;

    private string? SearchMessage { get; set; }

    private string SearchSummary { get; set; } = "";

    private PosterSearchResult? SearchResults { get; set; }

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private ITechnicianDashboard Dashboard { get; set; } = null!;

    [Inject]
    private IPrintingInventory Inventory { get; set; } = null!;

    [Inject]
    private ITechnicianWorkflow Workflow { get; set; } = null!;

    [Inject]
    private IServiceProvider ApplicationServices { get; set; } = null!;

    private TechnicianDashboardSnapshot Snapshot { get; set; } = Empty();

    private PrinterInkProbe.Reading InkProbe { get; set; } = PrinterInkProbe.Reading.Empty;

    private ConsumableAttention Attention { get; set; } = EmptyAttention();

    private TechnicianView? SearchView { get; set; }

    private string? SearchId { get; set; }

    private string SearchState { get; set; } = "";

    private string PeriodValue =>
        Snapshot.Year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture)
        + "-"
        + Snapshot.Month.ToString("00", System.Globalization.CultureInfo.InvariantCulture);

    protected override async Task OnInitializedAsync()
    {
        if (!TechnicianPeriods.TryParse(PeriodQuery, out var year, out var month))
        {
            (year, month) = TechnicianPeriods.Current();
        }

        Snapshot = await Dashboard.LoadAsync(year, month, CancellationToken.None);
        Attention = await Inventory.CriticalAlertsAsync(CancellationToken.None);
        InkProbe = await PrinterInkProbe.ReadAsync(
            ApplicationServices.GetService(typeof(IConfiguration)) as IConfiguration,
            CancellationToken.None);
        await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        // The earlier ?posterId= links keep working as a Poster ID search.
        var searching = !string.IsNullOrWhiteSpace(SearchByQuery) || !string.IsNullOrWhiteSpace(PosterIdQuery);
        if (!searching)
        {
            return;
        }

        SearchMode = string.IsNullOrWhiteSpace(SearchByQuery) ? PosterSearchModes.PosterId : PosterSearchModes.Normalize(SearchByQuery);
        var text = (SearchTextQuery ?? PosterIdQuery)?.Trim() ?? "";
        switch (SearchMode)
        {
            case PosterSearchModes.PosterId:
                await FindPosterAsync(text);
                break;
            case PosterSearchModes.Name:
                if (text.Length == 0)
                {
                    SearchMessage = "Enter part of a name.";
                    return;
                }

                SearchSummary = "Name contains \u201c" + text + "\u201d";
                SearchResults = await Search.ByNameAsync(text, CancellationToken.None);
                break;
            case PosterSearchModes.Date:
                var hasFrom = TryDate(SearchFromQuery, out var from);
                var hasTo = TryDate(SearchToQuery, out var to);
                if (!hasFrom && !hasTo)
                {
                    SearchMessage = "Choose a date.";
                    return;
                }

                if (hasFrom && hasTo && from > to)
                {
                    SearchMessage = "The start date is after the end date.";
                    return;
                }

                SearchSummary = (hasFrom, hasTo) switch
                {
                    (true, true) => "Submitted from " + Format(from) + " to " + Format(to),
                    (true, false) => "Submitted from " + Format(from),
                    _ => "Submitted up to " + Format(to)
                };
                SearchResults = await Search.ByDateAsync(hasFrom ? from : null, hasTo ? to : null, CancellationToken.None);
                break;
            case PosterSearchModes.Event:
                var events = await Search.EventsAsync(CancellationToken.None);
                var chosen = int.TryParse(SearchEventQuery, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var reasonId)
                    ? events.SingleOrDefault(item => item.Id == reasonId)
                    : null;
                if (chosen is null)
                {
                    SearchMessage = "Choose an event.";
                    return;
                }

                SearchSummary = "Event " + chosen.Name;
                SearchResults = await Search.ByEventAsync(chosen.Id, CancellationToken.None);
                break;
        }
    }

    private async Task FindPosterAsync(string posterId)
    {
        SearchId = posterId;
        if (!PosterIds.IsPublic(SearchId))
        {
            SearchState = "invalid";
            return;
        }

        SearchView = await Workflow.FindAsync(SearchId, CancellationToken.None);
        SearchState = SearchView is null ? "missing" : "found";
    }

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private string SearchKicker => PosterSearchModes.All.Single(option => option.Value == SearchMode).Label;

    private static string StageClass(string status) => "epx-stage epx-stage-" + status.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant();

    private static ConsumableAttention EmptyAttention() => new()
    {
        Depleted = [],
        LowStock = [],
        ExpiringSoon = [],
        NeedsReorder = EmptySummary(),
        ExpiringSoonSummary = EmptySummary()
    };

    private static SupplyCategorySummary EmptySummary() => new()
    {
        ByType = []
    };

    private static TechnicianDashboardSnapshot Empty()
    {
        var (year, month) = TechnicianPeriods.Current();
        return new TechnicianDashboardSnapshot
        {
            Year = year,
            Month = month,
            PeriodLabel = "",
            PendingPrinting = 0,
            PrintedThisMonth = 0,
            PrintedThisYear = 0,
            AwaitingPickup = 0,
            TotalProcessed = 0,
            Performance = [],
            Queue = []
        };
    }
}
