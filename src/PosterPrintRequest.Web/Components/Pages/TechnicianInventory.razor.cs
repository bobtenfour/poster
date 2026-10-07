using System.Globalization;
using Microsoft.AspNetCore.Components;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class TechnicianInventory : ComponentBase
{
    [Inject]
    private IPrintingInventory Inventory { get; set; } = null!;

    private List<ConsumableDraft> CartridgeDrafts { get; set; } = [];

    private List<ConsumableDraft> PaperDrafts { get; set; } = [];

    private List<ConsumableDraft> LaminatingDrafts { get; set; } = [];

    private string WarningDays { get; set; } = "";

    private string? WarningError { get; set; }

    private string NewName { get; set; } = "";

    private string NewCode { get; set; } = "";

    private string NewCapacity { get; set; } = "";

    private string? AddError { get; set; }

    private bool Busy { get; set; }

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        var view = await Inventory.LoadAsync(CancellationToken.None);
        WarningDays = view.ExpirationWarningDays?.ToString(CultureInfo.InvariantCulture) ?? "";
        CartridgeDrafts = view.Cartridges.Select(ConsumableDraft.From).ToList();
        PaperDrafts = view.Paper.Select(ConsumableDraft.From).ToList();
        LaminatingDrafts = view.LaminatingMaterials.Select(ConsumableDraft.From).ToList();
    }

    private void OnWarningDaysChanged(ChangeEventArgs args) => WarningDays = args.Value?.ToString() ?? "";

    private void OnNewNameChanged(ChangeEventArgs args) => NewName = args.Value?.ToString() ?? "";

    private void OnNewCodeChanged(ChangeEventArgs args) => NewCode = args.Value?.ToString() ?? "";

    private void OnNewCapacityChanged(ChangeEventArgs args) => NewCapacity = args.Value?.ToString() ?? "";

    private async Task SaveWarningAsync()
    {
        WarningError = null;
        int? days = null;
        if (!string.IsNullOrWhiteSpace(WarningDays))
        {
            if (!int.TryParse(WarningDays.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                WarningError = "Enter a warning period of zero or more days.";
                return;
            }

            days = parsed;
        }

        Busy = true;
        var result = await Inventory.SetExpirationWarningDaysAsync(days, CancellationToken.None);
        Busy = false;
        if (!result.Completed)
        {
            WarningError = result.Message;
            return;
        }

        await ReloadAsync();
    }

    private async Task AddMaterialAsync()
    {
        AddError = null;
        Busy = true;
        var result = await Inventory.AddLaminatingMaterialAsync(NewName, NewCode, NewCapacity, CancellationToken.None);
        Busy = false;
        if (!result.Completed)
        {
            AddError = result.Message;
            return;
        }

        NewName = "";
        NewCode = "";
        NewCapacity = "";
        await ReloadAsync();
    }

    private async Task SaveAsync(int id)
    {
        var draft = CartridgeDrafts.Concat(PaperDrafts).Concat(LaminatingDrafts).Single(item => item.Id == id);
        draft.Error = null;
        var quantityOk = TryCount(draft.Quantity, "Enter a quantity of zero or more.", out var quantity, out var quantityError);
        var criticalOk = TryCount(draft.Critical, "Enter a threshold of zero or more.", out var critical, out var criticalError);
        var lowOk = TryCount(draft.Low, "Enter a threshold of zero or more.", out var low, out var lowError);
        if (!quantityOk || !criticalOk || !lowOk)
        {
            draft.Error = quantityError ?? criticalError ?? lowError;
            return;
        }

        DateOnly? expiration = null;
        if (draft.HasExpiration && !string.IsNullOrWhiteSpace(draft.Expiration))
        {
            if (!DateOnly.TryParseExact(draft.Expiration.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                draft.Error = "Enter a valid expiration date.";
                return;
            }

            expiration = parsed;
        }

        Busy = true;
        var result = await Inventory.UpdateAsync(new ConsumableUpdate
        {
            PrintingConsumableId = draft.Id,
            CurrentQuantity = quantity,
            LowStockThreshold = low,
            CriticalStockThreshold = critical,
            ExpirationDate = expiration,
            Name = draft.Name,
            Code = draft.Code,
            Capacity = draft.Capacity
        }, CancellationToken.None);
        Busy = false;
        if (!result.Completed)
        {
            draft.Error = result.Message;
            return;
        }

        await ReloadAsync();
    }

    private static bool TryCount(string value, string message, out int count, out string? error)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 0)
        {
            error = message;
            count = 0;
            return false;
        }

        error = null;
        return true;
    }
}

public sealed class ConsumableDraft
{
    public int Id { get; set; }

    public bool HasExpiration { get; set; }

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";

    public string Capacity { get; set; } = "";

    public string Quantity { get; set; } = "0";

    public string Low { get; set; } = "0";

    public string Critical { get; set; } = "0";

    public string Expiration { get; set; } = "";

    public string Status { get; set; } = "";

    public string? Error { get; set; }

    public static ConsumableDraft From(ConsumableRow row) => new()
    {
        Id = row.PrintingConsumableId,
        HasExpiration = row.HasExpirationDate,
        Name = row.Name,
        Code = row.Code ?? "",
        Capacity = row.Capacity ?? "",
        Quantity = row.CurrentQuantity.ToString(CultureInfo.InvariantCulture),
        Low = row.LowStockThreshold.ToString(CultureInfo.InvariantCulture),
        Critical = row.CriticalStockThreshold.ToString(CultureInfo.InvariantCulture),
        Expiration = row.ExpirationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        Status = row.Status
    };
}
