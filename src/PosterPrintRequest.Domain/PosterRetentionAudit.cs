namespace PosterPrintRequest.Domain;

/// <summary>Durable record of a temporary deletion, restoration, or permanent expiration.</summary>
public sealed class PosterRetentionAudit
{
    public int PosterRetentionAuditId { get; set; }

    public int PosterRequestId { get; set; }

    public string PosterId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }

    public Guid? ActorObjectId { get; set; }

    public string OriginalDirectory { get; set; } = string.Empty;
}
