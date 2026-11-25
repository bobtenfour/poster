namespace PosterPrintRequest.Domain;

/// <summary>One immutable row of the stock-movement register (Consumables Inventory Contract section 7).</summary>
public sealed class PrintingStockMovement
{
    public long PrintingStockMovementId { get; set; }

    public Guid OperationId { get; set; }

    public int PrintingConsumableId { get; set; }

    public PrintingConsumable PrintingConsumable { get; set; } = null!;

    /// <summary>The lot: the consumable and this date; null is the undated lot.</summary>
    public DateOnly? ExpirationDate { get; set; }

    /// <summary>One of <see cref="StockMovementTypes"/>.</summary>
    public string MovementType { get; set; } = string.Empty;

    public int QuantityChange { get; set; }

    public int LotQuantityAfter { get; set; }

    public bool OutOfOrder { get; set; }

    public string? Reason { get; set; }

    /// <summary>UTC.</summary>
    public DateTime OccurredAt { get; set; }

    public Guid PerformedByObjectId { get; set; }

    public string? PerformedByAccountName { get; set; }
}

public static class StockMovementTypes
{
    public const string Received = "Received";

    public const string Used = "Used";

    public const string Adjustment = "Adjustment";
}
