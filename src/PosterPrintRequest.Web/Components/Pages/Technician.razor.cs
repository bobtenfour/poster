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

    private IReadOnlyList<SupplyAttentionRow> SupplyRows => BuildSupplyRows(Attention);

    private static string SupplyCardAction(int count, string action) => count == 0 ? "Clear" : action;

    private static bool ShowStockStatus(string status) =>
        status is ConsumableStock.Depleted or ConsumableStock.LowStock;

    private static string StockTone(string status) =>
        status is ConsumableStock.Depleted or ConsumableStock.Expired ? "epx-status-danger" : "epx-status-warning";

    private static ConsumableAttention EmptyAttention() => new()
    {
        Depleted = [],
        LowStock = [],
        ExpiringSoon = []
    };

    private static IReadOnlyList<SupplyAttentionRow> BuildSupplyRows(ConsumableAttention attention)
    {
        var expiration = attention.ExpiringSoon.ToDictionary(alert => alert.PrintingConsumableId);
        var rows = new Dictionary<int, SupplyAttentionRow>();
        var order = new List<int>();
        foreach (var alert in attention.Depleted.Concat(attention.LowStock).Concat(attention.ExpiringSoon))
        {
            if (rows.ContainsKey(alert.PrintingConsumableId))
            {
                continue;
            }

            expiration.TryGetValue(alert.PrintingConsumableId, out var expiring);
            rows[alert.PrintingConsumableId] = new SupplyAttentionRow
            {
                Id = alert.PrintingConsumableId,
                Name = alert.Name,
                Code = alert.Code,
                Type = alert.Category,
                Quantity = alert.CurrentQuantity,
                StockStatus = alert.Status,
                ExpirationStatus = ExpirationLabel(expiring),
                ExpirationDate = alert.ExpirationDate
            };
            order.Add(alert.PrintingConsumableId);
        }

        return order.Select(id => rows[id]).ToList();
    }

    private static string? ExpirationLabel(ConsumableAlert? alert)
    {
        if (alert is null)
        {
            return null;
        }

        if (alert.Detail.StartsWith(ConsumableStock.Expired, StringComparison.Ordinal))
        {
            return ConsumableStock.Expired;
        }

        if (alert.Detail.StartsWith(ConsumableStock.ExpiringSoon, StringComparison.Ordinal))
        {
            return ConsumableStock.ExpiringSoon;
        }

        return null;
    }

    private sealed class SupplyAttentionRow
    {
        public required int Id { get; init; }

        public required string Name { get; init; }

        public string? Code { get; init; }

        public required string Type { get; init; }

        public required int Quantity { get; init; }

        public required string StockStatus { get; init; }

        public string? ExpirationStatus { get; init; }

        public DateOnly? ExpirationDate { get; init; }
    }

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
