namespace PosterPrintRequest.Domain;

/// <summary>One replacement of a poster file by its requester (TDX Integration Contract section 17).</summary>
public sealed class PosterReplacement
{
    public int PosterReplacementId { get; set; }

    public int PosterRequestId { get; set; }

    public PosterRequest PosterRequest { get; set; } = null!;

    /// <summary>UTC time of the replacement.</summary>
    public DateTime ReplacedAt { get; set; }

    public string Note { get; set; } = string.Empty;

    /// <summary>Path of the archived (replaced) file, relative to the shared-storage root.</summary>
    public string ArchivedFilePath { get; set; } = string.Empty;

    public Guid ReplacedByObjectId { get; set; }
}
