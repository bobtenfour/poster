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

    private string AddConsumableId { get; set; } = "";

    private string AddQuantity { get; set; } = "";

    private string AddExpiration { get; set; } = "";

    private string? AddStockError { get; set; }

    private string RemoveConsumableId { get; set; } = "";

    private string RemoveQuantity { get; set; } = "";

    private bool EarliestAcknowledged { get; set; }

    private string? RemoveStockError { get; set; }

    private string NewName { get; set; } = "";

    private string NewCode { get; set; } = "";

    private string NewCapacity { get; set; } = "";

    private string? AddError { get; set; }

    private bool Busy { get; set; }

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        var view = await Inventory.LoadAsync(CancellationToken.None);
        CartridgeDrafts = view.Cartridges.Select(ConsumableDraft.From).ToList();
        PaperDrafts = view.Paper.Select(ConsumableDraft.From).ToList();
        LaminatingDrafts = view.LaminatingMaterials.Select(ConsumableDraft.From).ToList();
    }

    private IEnumerable<ConsumableDraft> StockChoices => CartridgeDrafts.Concat(PaperDrafts).Concat(LaminatingDrafts);

    private ConsumableDraft? AddChoice => FindChoice(AddConsumableId);

    private ConsumableDraft? RemoveChoice => FindChoice(RemoveConsumableId);

    private void OnAddConsumableChanged(ChangeEventArgs args)
    {
        AddConsumableId = args.Value?.ToString() ?? "";
        if (AddChoice?.HasExpiration != true)
        {
            AddExpiration = "";
        }
    }

    private void OnAddQuantityChanged(ChangeEventArgs args) => AddQuantity = args.Value?.ToString() ?? "";

    private void OnAddExpirationChanged(ChangeEventArgs args) => AddExpiration = args.Value?.ToString() ?? "";

    private void OnRemoveConsumableChanged(ChangeEventArgs args)
    {
        RemoveConsumableId = args.Value?.ToString() ?? "";
        EarliestAcknowledged = false;
    }

    private void OnRemoveQuantityChanged(ChangeEventArgs args) => RemoveQuantity = args.Value?.ToString() ?? "";

    private void OnAcknowledgeChanged(ChangeEventArgs args) =>
        EarliestAcknowledged = args.Value is bool selected && selected;

    private void OnNewNameChanged(ChangeEventArgs args) => NewName = args.Value?.ToString() ?? "";

    private void OnNewCodeChanged(ChangeEventArgs args) => NewCode = args.Value?.ToString() ?? "";

    private void OnNewCapacityChanged(ChangeEventArgs args) => NewCapacity = args.Value?.ToString() ?? "";

    private async Task AddStockAsync()
    {
        AddStockError = null;
        if (!TryMovement(AddConsumableId, AddQuantity, out var id, out var quantity, out var error))
        {
            AddStockError = error;
            return;
        }

        DateOnly? expiration = null;
        if (AddChoice?.HasExpiration == true && !string.IsNullOrWhiteSpace(AddExpiration))
        {
            if (!DateOnly.TryParseExact(AddExpiration.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                AddStockError = "Enter a valid expiration date.";
                return;
            }

            expiration = parsed;
        }

        Busy = true;
        var result = await Inventory.AddStockAsync(id, quantity, expiration, CancellationToken.None);
        Busy = false;
        if (!result.Completed)
        {
            AddStockError = result.Message;
            return;
        }

        AddQuantity = "";
        AddExpiration = "";
        await ReloadAsync();
    }

    private async Task RemoveStockAsync()
    {
        RemoveStockError = null;
        if (!TryMovement(RemoveConsumableId, RemoveQuantity, out var id, out var quantity, out var error))
        {
            RemoveStockError = error;
            return;
        }

        DateOnly? acknowledged = null;
        if (RemoveChoice?.Category == ConsumableCategory.Cartridge
            && DateOnly.TryParseExact(RemoveChoice.EarliestExpiration, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var earliest))
        {
            acknowledged = earliest;
        }

        Busy = true;
        var result = await Inventory.RemoveStockAsync(id, quantity, EarliestAcknowledged, acknowledged, CancellationToken.None);
        Busy = false;
        if (!result.Completed)
        {
            RemoveStockError = result.Message;
            return;
        }

        RemoveQuantity = "";
        EarliestAcknowledged = false;
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
        var criticalOk = TryCount(draft.Critical, "Enter a threshold of zero or more.", out var critical, out var criticalError);
        var lowOk = TryCount(draft.Low, "Enter a threshold of zero or more.", out var low, out var lowError);
        if (!criticalOk || !lowOk)
        {
            draft.Error = criticalError ?? lowError;
            return;
        }

        Busy = true;
        var result = await Inventory.UpdateAsync(new ConsumableUpdate
        {
            PrintingConsumableId = draft.Id,
            LowStockThreshold = low,
            CriticalStockThreshold = critical,
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

    private ConsumableDraft? FindChoice(string id) =>
        StockChoices.FirstOrDefault(item => item.Id.ToString(CultureInfo.InvariantCulture) == id);

    private static string ChoiceLabel(ConsumableDraft draft) =>
        string.IsNullOrWhiteSpace(draft.Code) ? draft.Name : draft.Name + " " + draft.Code;

    private static bool TryMovement(string consumableId, string quantityText, out int id, out int quantity, out string? error)
    {
        id = 0;
        quantity = 0;
        if (!int.TryParse(consumableId, NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            error = "Select a consumable.";
            return false;
        }

        if (!int.TryParse(quantityText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out quantity) || quantity <= 0)
        {
            error = "Enter a quantity greater than zero.";
            return false;
        }

        error = null;
        return true;
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

    public string Category { get; set; } = "";

    public bool HasExpiration { get; set; }

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";

    public string Capacity { get; set; } = "";

    public string Quantity { get; set; } = "0";

    public string Low { get; set; } = "0";

    public string Critical { get; set; } = "0";

    public string EarliestExpiration { get; set; } = "";

    public string? EarliestAlert { get; set; }

    public IReadOnlyList<StockEntryDraft> Entries { get; set; } = [];

    public string Status { get; set; } = "";

    public string? Error { get; set; }

    public static ConsumableDraft From(ConsumableRow row) => new()
    {
        Id = row.PrintingConsumableId,
        Category = row.Category,
        HasExpiration = row.HasExpirationDate,
        Name = row.Name,
        Code = row.Code ?? "",
        Capacity = row.Capacity ?? "",
        Quantity = row.CurrentQuantity.ToString(CultureInfo.InvariantCulture),
        Low = row.LowStockThreshold.ToString(CultureInfo.InvariantCulture),
        Critical = row.CriticalStockThreshold.ToString(CultureInfo.InvariantCulture),
        EarliestExpiration = row.EarliestExpirationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        EarliestAlert = row.EarliestExpirationAlert,
        Entries = row.Entries.Select(entry => new StockEntryDraft
        {
            Id = entry.PrintingStockEntryId,
            Quantity = entry.Quantity,
            Expiration = entry.ExpirationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            Alert = entry.ExpirationAlert
        }).ToList(),
        Status = row.Status
    };
}

public sealed class StockEntryDraft
{
    public int Id { get; set; }

    public int Quantity { get; set; }

    public string Expiration { get; set; } = "";

    public string? Alert { get; set; }
}
