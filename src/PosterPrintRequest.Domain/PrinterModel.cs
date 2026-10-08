namespace PosterPrintRequest.Domain;

public sealed class PrinterModel
{
    public int PrinterModelId { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public ICollection<PrinterModelConsumable> CompatibleConsumables { get; set; } = new List<PrinterModelConsumable>();
}
