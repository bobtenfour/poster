namespace PosterPrintRequest.Domain;

public sealed class ApprovalSheet
{
    public int ApprovalSheetId { get; set; }

    public int PosterRequestId { get; set; }

    public PosterRequest PosterRequest { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;
}
