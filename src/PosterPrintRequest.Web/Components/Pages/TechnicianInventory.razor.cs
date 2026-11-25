using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PosterPrintRequest.Domain;
using PosterPrintRequest.Web.Requests;

namespace PosterPrintRequest.Web.Components.Pages;

public partial class TechnicianInventory : ComponentBase
{
    [Inject]
    private IPrintingInventory Inventory { get; set; } = null!;

    [Inject]
    private AuthenticationStateProvider AuthenticationState { get; set; } = null!;

    [Inject]
    private IInstitutionalActorSource Actors { get; set; } = null!;

    private InstitutionalActor? Actor { get; set; }

    private List<PanelDrafts> Panels { get; set; } = [];

    private string AddConsumableId { get; set; } = "";

    private string AddQuantity { get; set; } = "";

    private string AddExpiration { get; set; } = "";

    private string? AddStockError { get; set; }

    private string RemoveConsumableId { get; set; } = "";

    private string RemoveQuantity { get; set; } = "";

    private bool EarliestAcknowledged { get; set; }

    private string? RemoveStockError { get; set; }

    private bool Busy { get; set; }

    private ConsumableLots? Lots { get; set; }

    private string? LotError { get; set; }

    private string? PendingOutOfOrder { get; set; }

    private string? OutOfOrderMessage { get; set; }

    private Dictionary<string, string> RemoveQuantities { get; } = new();

    private Dictionary<string, string> AdjustDirections { get; } = new();

    private Dictionary<string, string> AdjustQuantities { get; } = new();

    private Dictionary<string, string> AdjustReasons { get; } = new();

    private string? CorrectingLot { get; set; }

    private bool AddingLot { get; set; }

    private string NewLotDate { get; set; } = "";

    private string NewLotQuantity { get; set; } = "";

    private string NewLotReason { get; set; } = "";

    private string LotTitle => Lots is null
        ? ""
        : string.IsNullOrWhiteSpace(Lots.Consumable.Code) ? Lots.Consumable.Name : Lots.Consumable.Name + " " + Lots.Consumable.Code;

    /// <summary>Panel anchor derived from the type id, never from a type name (contract section 4.9).</summary>
    public static string PanelId(int consumableTypeId) => "type-" + consumableTypeId.ToString(CultureInfo.InvariantCulture);

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthenticationState.GetAuthenticationStateAsync();
        Actor = Actors.FromPrincipal(state.User);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var view = await Inventory.LoadAsync(CancellationToken.None);
        Panels = view.Types
            .Select(type => new PanelDrafts(type.ConsumableTypeId, type.Name, type.StockMode, type.Rows.Select(ConsumableDraft.From).ToList()))
            .ToList();
    }

    private IEnumerable<ConsumableDraft> StockChoices => Panels.SelectMany(panel => panel.Drafts);

    private ConsumableDraft? AddChoice => FindChoice(AddConsumableId);

    private ConsumableDraft? RemoveChoice => FindChoice(RemoveConsumableId);

    private static string ModeHelp(string stockMode) => stockMode switch
    {
        ConsumableStockModes.Undated => "This type has no expiration date. Receipts share one undated lot.",
        ConsumableStockModes.DateRequired => "Every lot has an expiration date. Total quantity and first expiration come from the lots.",
        _ => "Dated stock is grouped by expiration date. Undated stock is a separate lot and is used after dated stock."
    };

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
            if (!TryDate(AddExpiration, out var parsed))
            {
                AddStockError = "Enter a valid expiration date.";
                return;
            }

            expiration = parsed;
        }

        Busy = true;
        var result = await Inventory.AddStockAsync(id, quantity, expiration, Actor, CancellationToken.None);
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
        if (RemoveChoice?.AcknowledgeEarliest == true && TryDate(RemoveChoice.EarliestExpiration, out var earliest))
        {
            acknowledged = earliest;
        }

        Busy = true;
        var result = await Inventory.RemoveStockAsync(id, quantity, EarliestAcknowledged, acknowledged, Actor, CancellationToken.None);
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

    private async Task OpenLotsAsync(int printingConsumableId)
    {
        ClearLotFields();
        Lots = await Inventory.LoadLotsAsync(printingConsumableId, CancellationToken.None);
        if (Lots is null)
        {
            await ReloadAsync();
        }
    }

    private void CloseLots()
    {
        Lots = null;
        ClearLotFields();
    }

    private void ClearLotFields()
    {
        LotError = null;
        PendingOutOfOrder = null;
        OutOfOrderMessage = null;
        RemoveQuantities.Clear();
        AdjustDirections.Clear();
        AdjustQuantities.Clear();
        AdjustReasons.Clear();
        NewLotDate = "";
        NewLotQuantity = "";
        NewLotReason = "";
        CorrectingLot = null;
        AddingLot = false;
    }

    private void ToggleCorrection(string key)
    {
        CorrectingLot = CorrectingLot == key ? null : key;
        LotError = null;
    }

    private async Task RemoveFromLotAsync(DateOnly? expirationDate, bool confirmed)
    {
        if (Lots is null)
        {
            return;
        }

        LotError = null;
        var key = LotKey(expirationDate);
        if (!TryQuantity(Field(RemoveQuantities, key, "1"), out var quantity))
        {
            LotError = "Enter a quantity greater than zero.";
            return;
        }

        Busy = true;
        var result = await Inventory.RemoveFromLotAsync(Lots.Consumable.PrintingConsumableId, expirationDate, quantity, confirmed, Actor, CancellationToken.None);
        Busy = false;
        if (result.NeedsOutOfOrderConfirmation)
        {
            PendingOutOfOrder = key;
            OutOfOrderMessage = result.Message;
            return;
        }

        await AfterLotOperationAsync(result);
    }

    private async Task AdjustLotAsync(DateOnly? expirationDate, string key)
    {
        if (Lots is null)
        {
            return;
        }

        LotError = null;
        if (!TryQuantity(Field(AdjustQuantities, key), out var quantity))
        {
            LotError = "Enter a quantity greater than zero.";
            return;
        }

        var change = Field(AdjustDirections, key, "subtract") == "add" ? quantity : -quantity;
        Busy = true;
        var result = await Inventory.AdjustLotAsync(Lots.Consumable.PrintingConsumableId, expirationDate, change, Field(AdjustReasons, key), Actor, CancellationToken.None);
        Busy = false;
        await AfterLotOperationAsync(result);
    }

    private async Task AddMissingLotAsync()
    {
        if (Lots is null)
        {
            return;
        }

        LotError = null;
        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(NewLotDate))
        {
            if (!TryDate(NewLotDate, out var parsed))
            {
                LotError = "Enter a valid expiration date.";
                return;
            }

            date = parsed;
        }

        if (!TryQuantity(NewLotQuantity, out var quantity))
        {
            LotError = "Enter a quantity greater than zero.";
            return;
        }

        Busy = true;
        var result = await Inventory.AdjustLotAsync(Lots.Consumable.PrintingConsumableId, date, quantity, NewLotReason, Actor, CancellationToken.None);
        Busy = false;
        await AfterLotOperationAsync(result);
    }

    private async Task AfterLotOperationAsync(InventoryOutcome result)
    {
        if (!result.Completed)
        {
            LotError = result.Message;
            return;
        }

        var id = Lots!.Consumable.PrintingConsumableId;
        ClearLotFields();
        Lots = await Inventory.LoadLotsAsync(id, CancellationToken.None);
        await ReloadAsync();
    }

    private static string LotKey(DateOnly? date) =>
        date?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? "nodate";

    private static string Field(Dictionary<string, string> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value) ? value : fallback;

    private static string AlertClass(string alert) => ConsumableStockTable.AlertClass(alert);

    private static string MovementLabel(string movementType) => movementType switch
    {
        StockMovementTypes.Received => "Received",
        StockMovementTypes.Used => "Used",
        StockMovementTypes.Adjustment => "Adjustment (correction)",
        _ => movementType
    };

    private ConsumableDraft? FindChoice(string id) =>
        StockChoices.FirstOrDefault(item => item.Id.ToString(CultureInfo.InvariantCulture) == id);

    private static string ChoiceLabel(ConsumableDraft draft) =>
        (string.IsNullOrWhiteSpace(draft.Code) ? draft.Name : draft.Name + " " + draft.Code) + " · " + draft.TypeName;

    private static bool TryDate(string? text, out DateOnly date) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static bool TryQuantity(string? text, out int quantity) =>
        int.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out quantity) && quantity > 0;

    private static bool TryMovement(string consumableId, string quantityText, out int id, out int quantity, out string? error)
    {
        quantity = 0;
        if (!int.TryParse(consumableId, NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            error = "Select a consumable.";
            return false;
        }

        if (!TryQuantity(quantityText, out quantity))
        {
            error = "Enter a quantity greater than zero.";
            return false;
        }

        error = null;
        return true;
    }

    private sealed record PanelDrafts(int ConsumableTypeId, string Name, string StockMode, List<ConsumableDraft> Drafts);
}

public sealed class ConsumableDraft
{
    public int Id { get; set; }

    public string TypeName { get; set; } = "";

    public string StockMode { get; set; } = "";

    public bool AcknowledgeEarliest { get; set; }

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
        TypeName = row.TypeName,
        StockMode = row.StockMode,
        AcknowledgeEarliest = row.AcknowledgeEarliestExpiration,
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
