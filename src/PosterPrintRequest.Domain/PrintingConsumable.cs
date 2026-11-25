namespace PosterPrintRequest.Domain;

public sealed class PrintingConsumable
{
    public int PrintingConsumableId { get; set; }

    public int ConsumableTypeId { get; set; }

    public ConsumableType ConsumableType { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Capacity { get; set; }

    public bool HasExpirationDate { get; set; }

    public int LowStockThreshold { get; set; }

    public int CriticalStockThreshold { get; set; }

    public string Status { get; set; } = string.Empty;

    public bool Active { get; set; }

    public List<PrintingStockEntry> StockEntries { get; set; } = [];

    public List<PrinterModelConsumable> PrinterCompatibilities { get; set; } = [];

    public List<PrintingStockMovement> StockMovements { get; set; } = [];
}
