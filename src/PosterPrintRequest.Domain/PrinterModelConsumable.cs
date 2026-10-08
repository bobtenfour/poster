namespace PosterPrintRequest.Domain;

public sealed class PrinterModelConsumable
{
    public int PrinterModelConsumableId { get; set; }

    public int PrinterModelId { get; set; }

    public PrinterModel PrinterModel { get; set; } = null!;

    public int PrintingConsumableId { get; set; }

    public PrintingConsumable PrintingConsumable { get; set; } = null!;

    public bool Active { get; set; } = true;
}
