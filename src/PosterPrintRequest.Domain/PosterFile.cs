namespace PosterPrintRequest.Domain;

public sealed class PosterFile
{
    public int PosterFileId { get; set; }

    public int PosterRequestId { get; set; }

    public PosterRequest PosterRequest { get; set; } = null!;

    public string OriginalFileName { get; set; } = string.Empty;

    public string DetectedFormat { get; set; } = string.Empty;

    public int PageCount { get; set; }

    public decimal Width { get; set; }

    public decimal Length { get; set; }

    public string StoragePath { get; set; } = string.Empty;
}
