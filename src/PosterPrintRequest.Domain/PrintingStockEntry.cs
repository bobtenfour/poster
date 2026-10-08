namespace PosterPrintRequest.Domain;

public sealed class PrintingStockEntry
{
    public int PrintingStockEntryId { get; set; }

    public int PrintingConsumableId { get; set; }

    public PrintingConsumable PrintingConsumable { get; set; } = null!;

    public int Quantity { get; set; }

    public DateOnly? ExpirationDate { get; set; }
}
