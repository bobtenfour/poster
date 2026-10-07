namespace PosterPrintRequest.Domain;

public sealed class PrintingConsumable
{
    public int PrintingConsumableId { get; set; }

    public string Category { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Capacity { get; set; }

    public bool HasExpirationDate { get; set; }

    public int CurrentQuantity { get; set; }

    public int LowStockThreshold { get; set; }

    public int CriticalStockThreshold { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateOnly? ExpirationDate { get; set; }

    public bool Active { get; set; }
}
