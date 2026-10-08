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

    public string EarliestExpiration { get; set; } = "";

    public string? EarliestAlert { get; set; }

    public string Status { get; set; } = "";

    public static ConsumableDraft From(ConsumableRow row) => new()
    {
        Id = row.PrintingConsumableId,
        Category = row.Category,
        HasExpiration = row.HasExpirationDate,
        Name = row.Name,
        Code = row.Code ?? "",
        Capacity = row.Capacity ?? "",
        Quantity = row.CurrentQuantity.ToString(CultureInfo.InvariantCulture),
        EarliestExpiration = row.EarliestExpirationDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
        EarliestAlert = row.EarliestExpirationAlert,
        Status = row.Status
    };
}
