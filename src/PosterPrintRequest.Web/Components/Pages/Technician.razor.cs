using Microsoft.AspNetCore.Components;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class Technician : ComponentBase
{
    [SupplyParameterFromQuery(Name = "posterId")]
    public string? PosterIdQuery { get; set; }

    [SupplyParameterFromQuery(Name = "period")]
    public string? PeriodQuery { get; set; }

    [Inject]
    private ITechnicianDashboard Dashboard { get; set; } = null!;

    [Inject]
    private IPrintingInventory Inventory { get; set; } = null!;

    [Inject]
    private ITechnicianWorkflow Workflow { get; set; } = null!;

    private TechnicianDashboardSnapshot Snapshot { get; set; } = Empty();

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
        Attention = await Inventory.CriticalAlertsAsync(DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        if (string.IsNullOrWhiteSpace(PosterIdQuery))
        {
            return;
        }

        SearchId = PosterIdQuery.Trim();
        if (!PosterIds.IsPublic(SearchId))
        {
            SearchState = "invalid";
            return;
        }

        SearchView = await Workflow.FindAsync(SearchId, CancellationToken.None);
        SearchState = SearchView is null ? "missing" : "found";
    }

    private static string SupplyCardAction(int count, string action) => count == 0 ? "Clear" : action;

    private static ConsumableAttention EmptyAttention() => new()
    {
        Depleted = [],
        LowStock = [],
        ExpiringSoon = []
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
