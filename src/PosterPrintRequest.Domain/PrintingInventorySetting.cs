namespace PosterPrintRequest.Domain;

public sealed class PrintingInventorySetting
{
    public int PrintingInventorySettingId { get; set; }

    public int? ExpirationWarningDays { get; set; }
}
